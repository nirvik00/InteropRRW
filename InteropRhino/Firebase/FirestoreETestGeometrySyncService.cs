using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;


using InteropRhino.Commands;

namespace InteropRhino.Firebase
{

    public static class FirestoreETestGeometrySyncService
    {
        private const string CollectionName = "e-test";
        private const string LayerName = "e-test";
        private const string FirestorePathKey = "rhinoReview.firestorePath";
        private const string FirestoreCollectionKey = "rhinoReview.firestoreCollection";
        private const string FirestoreDocumentIdKey = "rhinoReview.firestoreDocumentId";

        private static readonly object Gate = new();
        private static readonly HashSet<string> KnownDocumentPaths = [];
        private static FirestoreChangeListener? _listener;
        private static FirestoreETestGeometrySyncStatus? _status;

        public static bool IsRunning
        {
            get
            {
                lock (Gate)
                {
                    return _listener is not null;
                }
            }
        }

        public static Task<FirestoreETestGeometrySyncStatus> ToggleAsync(CancellationToken cancellationToken = default)
        {
            return IsRunning ? StopAsync(cancellationToken) : StartAsync(cancellationToken);
        }

        public static Task<FirestoreETestGeometrySyncStatus> StartAsync(CancellationToken cancellationToken = default)
        {
            lock (Gate)
            {
                if (_listener is not null && _status is not null)
                {
                    return Task.FromResult(_status with { AlreadyRunning = true });
                }
            }

            var options = FirebaseOptionsLoader.LoadForFirestore();
            var db = new FirestoreDbBuilder
            {
                ProjectId = options.ProjectId,
                GoogleCredential = CredentialFactory
                    .FromJson<ServiceAccountCredential>(options.CredentialsJson)
                    .ToGoogleCredential()
            }.Build();

            var collection = db.Collection(CollectionName);
            var status = new FirestoreETestGeometrySyncStatus(collection.Path, true, false);
            var listener = collection.Listen(OnSnapshot);

            lock (Gate)
            {
                _listener = listener;
                _status = status;
            }

            return Task.FromResult(status);
        }

        public static async Task<FirestoreETestGeometrySyncStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            FirestoreChangeListener? listener;
            FirestoreETestGeometrySyncStatus? status;

            lock (Gate)
            {
                listener = _listener;
                status = _status;
                _listener = null;
                _status = null;
                KnownDocumentPaths.Clear();
            }

            if (listener is not null)
            {
                await listener.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            return status is null
                ? new FirestoreETestGeometrySyncStatus(CollectionName, false, true)
                : status with { Running = false, AlreadyRunning = false };
        }

        private static void OnSnapshot(QuerySnapshot snapshot)
        {
            var documents = new List<ETestGeometryDocument>();
            var snapshotPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var document in snapshot.Documents)
            {
                snapshotPaths.Add(document.Reference.Path);

                if (TryReadDocument(document, out var geometryDocument, out var issue))
                {
                    documents.Add(geometryDocument);
                }
                else
                {
                    CommandUi.WriteLine($"Skipped Firestore document {document.Reference.Path}: {issue}");
                }
            }

            List<string> deletedPaths;
            lock (Gate)
            {
                deletedPaths = KnownDocumentPaths
                    .Where(path => !snapshotPaths.Contains(path))
                    .ToList();

                KnownDocumentPaths.Clear();
                foreach (var path in snapshotPaths)
                {
                    KnownDocumentPaths.Add(path);
                }
            }

            RhinoApp.InvokeOnUiThread((Action)(() => ApplyToActiveDocument(documents, deletedPaths, CollectionName)));
        }

        private static void ApplyToActiveDocument(
            IReadOnlyCollection<ETestGeometryDocument> documents,
            IReadOnlyCollection<string> deletedPaths,
            string collectionPath)
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc is null)
            {
                CommandUi.WriteLine($"Firestore {collectionPath} changed, but no Rhino document is active.");
                return;
            }

            var layerIndex = EnsureLayer(doc);
            var updated = 0;
            var created = 0;
            var deleted = 0;

            foreach (var documentPath in deletedPaths)
            {
                var existing = FindExistingObject(doc, documentPath);
                if (existing is not null && doc.Objects.Delete(existing, quiet: true))
                {
                    deleted++;
                }
            }

            foreach (var firestoreDocument in documents)
            {
                var geometry = BuildColumnGeometry(firestoreDocument);
                var existing = FindExistingObject(doc, firestoreDocument.Path);

                if (existing is null)
                {
                    var attributes = BuildAttributes(firestoreDocument, layerIndex);
                    if (doc.Objects.AddBrep(geometry, attributes) != Guid.Empty)
                    {
                        created++;
                    }

                    continue;
                }

                if (doc.Objects.Replace(existing.Id, geometry))
                {
                    var attributes = existing.Attributes.Duplicate();
                    ApplyAttributes(attributes, firestoreDocument, layerIndex);
                    doc.Objects.ModifyAttributes(existing.Id, attributes, quiet: true);
                    updated++;
                }
            }

            doc.Views.Redraw();
            CommandUi.WriteLine(
                $"Firestore geometry sync applied {collectionPath}: {created} created, {updated} updated, {deleted} deleted.");
        }

        private static Brep BuildColumnGeometry(ETestGeometryDocument document)
        {
            var width = document.Width > 0 ? document.Width : document.Thickness;
            var depth = document.Depth > 0 ? document.Depth : document.Thickness;
            var height = document.Height > 0 ? document.Height : document.Thickness;

            width = width > RhinoMath.ZeroTolerance ? width : 1.0;
            depth = depth > RhinoMath.ZeroTolerance ? depth : width;
            height = height > RhinoMath.ZeroTolerance ? height : width;

            if (string.Equals(document.ProfileShape, "circular", StringComparison.OrdinalIgnoreCase))
            {
                var radius = width / 2.0;
                var circle = new Circle(new Plane(document.CenterPoint, Vector3d.ZAxis), radius);
                return new Cylinder(circle, height).ToBrep(capBottom: true, capTop: true);
            }

            var min = new Point3d(
                document.CenterPoint.X - width / 2.0,
                document.CenterPoint.Y - depth / 2.0,
                document.CenterPoint.Z);
            var max = new Point3d(
                document.CenterPoint.X + width / 2.0,
                document.CenterPoint.Y + depth / 2.0,
                document.CenterPoint.Z + height);

            return new Box(new BoundingBox(min, max)).ToBrep();
        }

        private static int EnsureLayer(RhinoDoc doc)
        {
            var existing = doc.Layers.FindName(LayerName);
            if (existing is not null)
            {
                return existing.Index;
            }

            return doc.Layers.Add(LayerName, System.Drawing.Color.DeepSkyBlue);
        }

        private static RhinoObject? FindExistingObject(RhinoDoc doc, string documentPath)
        {
            var settings = new ObjectEnumeratorSettings
            {
                NormalObjects = true,
                LockedObjects = true,
                HiddenObjects = true,
                ReferenceObjects = false,
                IncludeLights = false,
                IncludeGrips = false,
                IncludePhantoms = false,
                ObjectTypeFilter = ObjectType.AnyObject
            };

            foreach (var rhinoObject in doc.Objects.GetObjectList(settings))
            {
                if (string.Equals(rhinoObject.Attributes.GetUserString(FirestorePathKey), documentPath, StringComparison.OrdinalIgnoreCase))
                {
                    return rhinoObject;
                }
            }

            return null;
        }

        private static ObjectAttributes BuildAttributes(ETestGeometryDocument document, int layerIndex)
        {
            var attributes = new ObjectAttributes();
            ApplyAttributes(attributes, document, layerIndex);
            return attributes;
        }

        private static void ApplyAttributes(ObjectAttributes attributes, ETestGeometryDocument document, int layerIndex)
        {
            attributes.Name = $"e-test:{document.Id}";
            attributes.LayerIndex = layerIndex;
            attributes.SetUserString(FirestorePathKey, document.Path);
            attributes.SetUserString(FirestoreCollectionKey, CollectionName);
            attributes.SetUserString(FirestoreDocumentIdKey, document.Id);
        }

        private static bool TryReadDocument(
            DocumentSnapshot snapshot,
            out ETestGeometryDocument document,
            out string issue)
        {
            document = default!;
            issue = string.Empty;

            var data = snapshot.ToDictionary();
            if (!TryReadMap(data, "centerpoint", out var centerPoint)
                && !TryReadMap(data, "centerPoint", out centerPoint))
            {
                issue = "missing centerpoint field";
                return false;
            }

            var x = ReadDouble(centerPoint, "x");
            var y = ReadDouble(centerPoint, "y");
            var z = ReadDouble(centerPoint, "z");
            var id = ReadString(data, "id");
            var thickness = ReadDouble(data, "thickness");
            var height = ReadDouble(data, "height");
            var profileShape = ReadString(data, "profileShape");
            var width = ReadDouble(data, "width");
            var depth = ReadDouble(data, "depth");

            if (TryReadMap(data, "profile", out var profile))
            {
                profileShape = ReadString(profile, "shape") ?? profileShape;
                width = ReadDouble(profile, "width", width);
                depth = ReadDouble(profile, "depth", depth);
            }

            if (thickness <= 0)
            {
                thickness = width > 0 ? width : 1.0;
            }

            document = new ETestGeometryDocument(
                id ?? snapshot.Id,
                snapshot.Reference.Path,
                new Point3d(x, y, z),
                thickness,
                height,
                width,
                depth,
                profileShape ?? "rectangular");
            return true;
        }

        private static bool TryReadMap(
            IReadOnlyDictionary<string, object> data,
            string key,
            out IReadOnlyDictionary<string, object> value)
        {
            if (data.TryGetValue(key, out var raw) && raw is IReadOnlyDictionary<string, object> map)
            {
                value = map;
                return true;
            }

            value = new Dictionary<string, object>();
            return false;
        }

        private static string? ReadString(IReadOnlyDictionary<string, object> data, string key)
        {
            return data.TryGetValue(key, out var value) ? value?.ToString() : null;
        }

        private static double ReadDouble(IReadOnlyDictionary<string, object> data, string key, double fallback = 0)
        {
            if (!data.TryGetValue(key, out var value) || value is null)
            {
                return fallback;
            }

            return value switch
            {
                double number => number,
                float number => number,
                decimal number => (double)number,
                long number => number,
                int number => number,
                string text when double.TryParse(text, out var parsed) => parsed,
                _ => fallback
            };
        }
    }

    internal sealed record ETestGeometryDocument(
        string Id,
        string Path,
        Point3d CenterPoint,
        double Thickness,
        double Height,
        double Width,
        double Depth,
        string ProfileShape);

    public sealed record FirestoreETestGeometrySyncStatus(
        string CollectionPath,
        bool Running,
        bool AlreadyRunning);

}
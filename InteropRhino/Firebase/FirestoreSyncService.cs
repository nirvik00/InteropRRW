using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Rhino;
using InteropRhino.Extraction;
using InteropRhino.Schema;

namespace InteropRhino.Firebase
{

    public static class FirestoreSyncService
    {
        private static readonly object Gate = new();
        private static readonly string WriterInstanceId = Guid.NewGuid().ToString("D");
        private static readonly HashSet<string> PendingOwnWriteOperationIds = new(StringComparer.OrdinalIgnoreCase);
        private static FirestoreChangeListener? _listener;
        private static FirestoreLiveSyncStatus? _status;
        private static uint? _targetDocumentSerialNumber;
        private static bool _skipInitialApply;

        public static async Task<FirestorePushResult> PushLatestAsync(
            InteropPayload payload,
            CancellationToken cancellationToken = default)
        {
            var options = FirebaseOptionsLoader.LoadForFirestore();
            var db = CreateDb(options);
            var document = LatestDocument(db, options);
            var writerOperationId = Guid.NewGuid().ToString("D");

            var firestorePayload = new Dictionary<string, object>
            {
                ["schemaVersion"] = payload.SchemaVersion,
                ["writerInstanceId"] = WriterInstanceId,
                ["writerOperationId"] = writerOperationId,
                ["updatedAtUtc"] = Timestamp.FromDateTime(DateTime.UtcNow),
                ["floors"] = FirestoreValueConverter.ToFirestoreValue(payload.Floors),
                ["walls"] = FirestoreValueConverter.ToFirestoreValue(payload.Walls),
                ["beams"] = FirestoreValueConverter.ToFirestoreValue(payload.Beams),
                ["columns"] = FirestoreValueConverter.ToFirestoreValue(payload.Columns)
            };

            lock (Gate)
            {
                PendingOwnWriteOperationIds.Add(writerOperationId);
            }

            try
            {
                await document.SetAsync(firestorePayload, SetOptions.Overwrite, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                lock (Gate)
                {
                    PendingOwnWriteOperationIds.Remove(writerOperationId);
                }

                throw;
            }

            return new FirestorePushResult(
                options.ModelId,
                document.Path,
                payload.Floors.Count,
                payload.Walls.Count,
                payload.Beams.Count,
                payload.Columns.Count);
        }

        public static Task<FirestoreLiveSyncStatus> StartLatestListenerAsync(CancellationToken cancellationToken = default)
        {
            return StartLatestListenerAsync(null, cancellationToken);
        }

        public static Task<FirestoreLiveSyncStatus> StartLatestListenerAsync(
            uint? targetDocumentSerialNumber,
            CancellationToken cancellationToken = default)
        {
            return StartLatestListenerAsync(targetDocumentSerialNumber, true, cancellationToken);
        }

        public static Task<FirestoreLiveSyncStatus> StartLatestListenerAsync(
            uint? targetDocumentSerialNumber,
            bool applyInitialSnapshot,
            CancellationToken cancellationToken = default)
        {
            lock (Gate)
            {
                if (_listener is not null && _status is not null)
                {
                    if (targetDocumentSerialNumber.HasValue)
                    {
                        _targetDocumentSerialNumber = targetDocumentSerialNumber;
                        _skipInitialApply = !applyInitialSnapshot;
                    }

                    return Task.FromResult(_status with { AlreadyRunning = true });
                }
            }

            var options = FirebaseOptionsLoader.LoadForFirestore();
            var db = CreateDb(options);
            var document = LatestDocument(db, options);
            var status = new FirestoreLiveSyncStatus(options.ModelId, document.Path, false);
            var listener = document.Listen(snapshot => OnLatestSnapshot(snapshot, status));

            lock (Gate)
            {
                _listener = listener;
                _status = status;
                _targetDocumentSerialNumber = targetDocumentSerialNumber;
                _skipInitialApply = targetDocumentSerialNumber.HasValue && !applyInitialSnapshot;
            }

            return Task.FromResult(status);
        }

        public static async Task<bool> StopLatestListenerAsync(CancellationToken cancellationToken = default)
        {
            FirestoreChangeListener? listener;
            lock (Gate)
            {
                listener = _listener;
                _listener = null;
                _status = null;
                _targetDocumentSerialNumber = null;
                _skipInitialApply = false;
            }

            if (listener is null)
            {
                return false;
            }

            await listener.StopAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        private static FirestoreDb CreateDb(FirestoreConnectionOptions options)
        {
            return new FirestoreDbBuilder
            {
                ProjectId = options.ProjectId,
                GoogleCredential = CredentialFactory
                    .FromJson<ServiceAccountCredential>(options.CredentialsJson)
                    .ToGoogleCredential()
            }.Build();
        }

        private static DocumentReference LatestDocument(FirestoreDb db, FirestoreConnectionOptions options)
        {
            return DocumentByPath(db, options.FirestoreDocumentPath);
        }

        private static DocumentReference DocumentByPath(FirestoreDb db, string documentPath)
        {
            var segments = documentPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var document = db.Collection(segments[0]).Document(segments[1]);
            for (var index = 2; index < segments.Length; index += 2)
            {
                document = document.Collection(segments[index]).Document(segments[index + 1]);
            }

            return document;
        }

        private static void OnLatestSnapshot(DocumentSnapshot snapshot, FirestoreLiveSyncStatus status)
        {
            uint? targetDocumentSerialNumber;
            bool skipInitialApply;
            lock (Gate)
            {
                targetDocumentSerialNumber = _targetDocumentSerialNumber;
                skipInitialApply = _skipInitialApply;
                _skipInitialApply = false;
            }

            var message = snapshot.Exists
                ? BuildSnapshotMessage(snapshot, status)
                : $"Firestore live sync: {status.DocumentPath} does not exist yet.";

            if (!snapshot.Exists || !targetDocumentSerialNumber.HasValue)
            {
                RhinoApp.InvokeOnUiThread((Action)(() => RhinoApp.WriteLine(message)));
                return;
            }

            var snapshotPath = snapshot.Reference.Path;
            var snapshotData = snapshot.ToDictionary();

            if (skipInitialApply)
            {
                TryConsumeOwnWrite(snapshotData);
                FirestoreRhinoGeometryApplier.RememberSnapshotHashes(snapshotData, snapshotPath);
                RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    RhinoApp.WriteLine(message);
                    RhinoApp.WriteLine($"Firestore -> Rhino skipped initial snapshot for {snapshotPath}; local Rhino state will push first.");
                }));
                return;
            }

            if (TryConsumeOwnWrite(snapshotData))
            {
                FirestoreRhinoGeometryApplier.RememberSnapshotHashes(snapshotData, snapshotPath);
                RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    RhinoApp.WriteLine(message);
                    RhinoApp.WriteLine($"Firestore -> Rhino ignored own write for {snapshotPath}.");
                }));
                return;
            }

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                RhinoApp.WriteLine(message);

                try
                {
                    var result = FirestoreRhinoGeometryApplier.ApplyLatestSnapshot(
                        snapshotData,
                        snapshotPath,
                        targetDocumentSerialNumber.Value);

                    RhinoApp.WriteLine(
                        $"Firestore -> Rhino applied {snapshotPath}: {result.Created} created, {result.Updated} updated, {result.Deleted} deleted, {result.Skipped} skipped.");
                }
                catch (Exception exception)
                {
                    RhinoApp.WriteLine($"Firestore -> Rhino apply failed: {exception.Message}");
                }
            }));
        }

        private static string BuildSnapshotMessage(DocumentSnapshot snapshot, FirestoreLiveSyncStatus status)
        {
            var data = snapshot.ToDictionary();
            var counts = new Dictionary<string, int>
            {
                ["floors"] = CountArray(data, "floors"),
                ["walls"] = CountArray(data, "walls"),
                ["beams"] = CountArray(data, "beams"),
                ["columns"] = CountArray(data, "columns")
            };

            var countText = string.Join(", ", counts.Select(pair => $"{pair.Key}: {pair.Value}"));
            return $"Firestore live sync update for {status.ModelId} at {snapshot.Reference.Path}: {countText}";
        }

        private static int CountArray(IReadOnlyDictionary<string, object> data, string fieldName)
        {
            if (!data.TryGetValue(fieldName, out var raw) || raw is not IEnumerable<object> values)
            {
                return 0;
            }

            return values.Count();
        }

        private static bool TryConsumeOwnWrite(IReadOnlyDictionary<string, object> snapshotData)
        {
            if (!snapshotData.TryGetValue("writerInstanceId", out var writerInstanceId)
                || !string.Equals(writerInstanceId?.ToString(), WriterInstanceId, StringComparison.OrdinalIgnoreCase)
                || !snapshotData.TryGetValue("writerOperationId", out var writerOperationId)
                || writerOperationId is null)
            {
                return false;
            }

            lock (Gate)
            {
                return PendingOwnWriteOperationIds.Remove(writerOperationId.ToString() ?? string.Empty);
            }
        }
    }

    public sealed record FirestorePushResult(
        string ModelId,
        string DocumentPath,
        int Floors,
        int Walls,
        int Beams,
        int Columns);

    public sealed record FirestoreLiveSyncStatus(
        string ModelId,
        string DocumentPath,
        bool AlreadyRunning);

    internal static class FirestoreValueConverter
    {
        public static object ToFirestoreValue<T>(T value)
        {
            using var document = JsonSerializer.SerializeToDocument(value, JsonOptions.Pretty);
            return ConvertElement(document.RootElement);
        }

        private static object ConvertElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => ConvertObject(element),
                JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToList(),
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number when element.TryGetInt64(out var number) => number,
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null!,
                _ => string.Empty
            };
        }

        private static Dictionary<string, object> ConvertObject(JsonElement element)
        {
            var map = new Dictionary<string, object>();
            foreach (var property in element.EnumerateObject())
            {
                map[property.Name] = ConvertElement(property.Value);
            }

            return map;
        }
    }
}
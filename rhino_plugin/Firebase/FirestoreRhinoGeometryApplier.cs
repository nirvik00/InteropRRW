using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SixCharis.RhinoReviewInterop.Firebase;

public static class FirestoreRhinoGeometryApplier
{
    private const string ElementIdKey = "rhinoReview.elementId";
    private const string ElementHashKey = "rhinoReview.elementHash";
    private const string ElementTypeKey = "rhinoReview.elementType";
    private const string SnapshotPathKey = "rhinoReview.snapshotPath";
    private static readonly object HashGate = new();
    private static readonly Dictionary<string, Dictionary<string, string>> LastElementHashesBySnapshot = new(StringComparer.OrdinalIgnoreCase);

    public static void RememberSnapshotHashes(
        IReadOnlyDictionary<string, object> snapshotData,
        string snapshotPath)
    {
        lock (HashGate)
        {
            LastElementHashesBySnapshot[snapshotPath] = ReadElementHashes(snapshotData);
        }
    }

    public static FirestoreApplyResult ApplyLatestSnapshot(
        IReadOnlyDictionary<string, object> snapshotData,
        string snapshotPath,
        uint documentSerialNumber)
    {
        var doc = RhinoDoc.FromRuntimeSerialNumber(documentSerialNumber);
        if (doc is null)
        {
            return new FirestoreApplyResult(0, 0, 0, 1);
        }

        var elements = new List<RemoteInteropElement>();
        var skipped = 0;

        ReadFloors(snapshotData, snapshotPath, elements, ref skipped);
        ReadWalls(snapshotData, snapshotPath, elements, ref skipped);
        ReadBeams(snapshotData, snapshotPath, elements, ref skipped);
        ReadColumns(snapshotData, snapshotPath, elements, ref skipped);

        return FirestoreAutoSyncService.SuppressPushWhileApplyingRemoteChange(() =>
            ApplyElements(doc, snapshotPath, elements, skipped));
    }

    private static FirestoreApplyResult ApplyElements(
        RhinoDoc doc,
        string snapshotPath,
        IReadOnlyCollection<RemoteInteropElement> elements,
        int skipped)
    {
        var created = 0;
        var updated = 0;
        var deleted = 0;
        var activeElementIds = elements
            .Select(element => element.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousHashes = GetPreviousHashes(snapshotPath);
        var currentHashes = elements.ToDictionary(
            element => element.Id,
            element => element.DataHash,
            StringComparer.OrdinalIgnoreCase);

        foreach (var existing in FindSyncedObjects(doc, snapshotPath))
        {
            var elementId = existing.Attributes.GetUserString(ElementIdKey);
            if (!string.IsNullOrWhiteSpace(elementId) && activeElementIds.Contains(elementId))
            {
                continue;
            }

            if (doc.Objects.Delete(existing, quiet: true))
            {
                deleted++;
            }
        }

        foreach (var element in elements)
        {
            var existing = FindExistingObject(doc, snapshotPath, element);
            var layerIndex = EnsureLayer(doc, element);
            var elementChanged = !previousHashes.TryGetValue(element.Id, out var previousHash)
                || !string.Equals(previousHash, element.DataHash, StringComparison.OrdinalIgnoreCase);

            if (existing is null)
            {
                var attributes = BuildAttributes(element, layerIndex);
                if (doc.Objects.AddBrep(element.Geometry, attributes) != Guid.Empty)
                {
                    created++;
                }

                continue;
            }

            if (!elementChanged && string.Equals(
                existing.Attributes.GetUserString(ElementHashKey),
                element.DataHash,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!elementChanged)
            {
                var attributes = existing.Attributes.Duplicate();
                ApplyAttributes(attributes, element, layerIndex);
                doc.Objects.ModifyAttributes(existing.Id, attributes, quiet: true);
                continue;
            }

            if (doc.Objects.Replace(existing.Id, element.Geometry))
            {
                var attributes = existing.Attributes.Duplicate();
                ApplyAttributes(attributes, element, layerIndex);
                doc.Objects.ModifyAttributes(existing.Id, attributes, quiet: true);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        doc.Views.Redraw();
        SetPreviousHashes(snapshotPath, currentHashes);
        return new FirestoreApplyResult(created, updated, deleted, skipped);
    }

    private static Dictionary<string, string> GetPreviousHashes(string snapshotPath)
    {
        lock (HashGate)
        {
            return LastElementHashesBySnapshot.TryGetValue(snapshotPath, out var hashes)
                ? new Dictionary<string, string>(hashes, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SetPreviousHashes(string snapshotPath, Dictionary<string, string> hashes)
    {
        lock (HashGate)
        {
            LastElementHashesBySnapshot[snapshotPath] = hashes;
        }
    }

    private static IEnumerable<RhinoObject> FindSyncedObjects(RhinoDoc doc, string snapshotPath)
    {
        foreach (var rhinoObject in GetEditableObjects(doc))
        {
            if (string.Equals(
                rhinoObject.Attributes.GetUserString(SnapshotPathKey),
                snapshotPath,
                StringComparison.OrdinalIgnoreCase))
            {
                yield return rhinoObject;
            }
        }
    }

    private static RhinoObject? FindExistingObject(RhinoDoc doc, string snapshotPath, RemoteInteropElement element)
    {
        foreach (var rhinoObject in GetEditableObjects(doc))
        {
            var attributes = rhinoObject.Attributes;
            var elementId = attributes.GetUserString(ElementIdKey);
            var objectSnapshotPath = attributes.GetUserString(SnapshotPathKey);

            if (string.Equals(elementId, element.Id, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(objectSnapshotPath)
                    || string.Equals(objectSnapshotPath, snapshotPath, StringComparison.OrdinalIgnoreCase)))
            {
                return rhinoObject;
            }

            if (Guid.TryParse(element.Id, out var elementObjectId) && rhinoObject.Id == elementObjectId)
            {
                return rhinoObject;
            }
        }

        return null;
    }

    private static IEnumerable<RhinoObject> GetEditableObjects(RhinoDoc doc)
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

        return doc.Objects.GetObjectList(settings);
    }

    private static ObjectAttributes BuildAttributes(RemoteInteropElement element, int layerIndex)
    {
        var attributes = new ObjectAttributes();
        ApplyAttributes(attributes, element, layerIndex);
        return attributes;
    }

    private static void ApplyAttributes(ObjectAttributes attributes, RemoteInteropElement element, int layerIndex)
    {
        attributes.Name = $"{element.ElementType}:{element.Id}";
        attributes.LayerIndex = layerIndex;
        attributes.SetUserString(ElementIdKey, element.Id);
        attributes.SetUserString(ElementHashKey, element.DataHash);
        attributes.SetUserString(ElementTypeKey, element.ElementType);
        attributes.SetUserString(SnapshotPathKey, element.SnapshotPath);
    }

    private static int EnsureLayer(RhinoDoc doc, RemoteInteropElement element)
    {
        var layerName = DefaultLayerName(element.ElementType);

        var existing = doc.Layers.FindName(layerName);
        if (existing is not null)
        {
            return existing.Index;
        }

        return doc.Layers.Add(layerName, LayerColor(element.ElementType));
    }

    private static string DefaultLayerName(string elementType)
    {
        return elementType switch
        {
            "floor" => "Floor",
            "wall" => "Wall",
            "beam" => "Beam",
            "column" => "Column",
            _ => "RhinoReview"
        };
    }

    private static System.Drawing.Color LayerColor(string elementType)
    {
        return elementType switch
        {
            "floor" => System.Drawing.Color.LightSteelBlue,
            "wall" => System.Drawing.Color.IndianRed,
            "beam" => System.Drawing.Color.Goldenrod,
            "column" => System.Drawing.Color.MediumSeaGreen,
            _ => System.Drawing.Color.DeepSkyBlue
        };
    }

    private static void ReadFloors(
        IReadOnlyDictionary<string, object> snapshotData,
        string snapshotPath,
        ICollection<RemoteInteropElement> elements,
        ref int skipped)
    {
        foreach (var map in ReadArray(snapshotData, "floors"))
        {
            try
            {
                var points = ReadPointArray(map, "polyline");
                var thickness = ReadDouble(map, "thickness");
                var geometry = BuildFloor(points, thickness);
                if (geometry is null)
                {
                    skipped++;
                    continue;
                }

                elements.Add(BuildElement("floor", map, snapshotPath, geometry));
            }
            catch
            {
                skipped++;
            }
        }
    }

    private static void ReadWalls(
        IReadOnlyDictionary<string, object> snapshotData,
        string snapshotPath,
        ICollection<RemoteInteropElement> elements,
        ref int skipped)
    {
        foreach (var map in ReadArray(snapshotData, "walls"))
        {
            try
            {
                var points = ReadPointArray(map, "polyline");
                var thickness = ReadDouble(map, "thickness", 1.0);
                var height = ReadDouble(map, "height", 10.0);
                var geometry = BuildWall(points, thickness, height);
                if (geometry is null)
                {
                    skipped++;
                    continue;
                }

                elements.Add(BuildElement("wall", map, snapshotPath, geometry));
            }
            catch
            {
                skipped++;
            }
        }
    }

    private static void ReadBeams(
        IReadOnlyDictionary<string, object> snapshotData,
        string snapshotPath,
        ICollection<RemoteInteropElement> elements,
        ref int skipped)
    {
        foreach (var map in ReadArray(snapshotData, "beams"))
        {
            try
            {
                var line = ReadLine(map, "line");
                var xandy = ReadXandy(map);
                var geometry = BuildMember(line.Start, line.End, xandy.B, xandy.H);
                if (geometry is null)
                {
                    skipped++;
                    continue;
                }

                elements.Add(BuildElement("beam", map, snapshotPath, geometry));
            }
            catch
            {
                skipped++;
            }
        }
    }

    private static void ReadColumns(
        IReadOnlyDictionary<string, object> snapshotData,
        string snapshotPath,
        ICollection<RemoteInteropElement> elements,
        ref int skipped)
    {
        foreach (var map in ReadArray(snapshotData, "columns"))
        {
            try
            {
                var line = ReadLine(map, "line");
                var xandy = ReadXandy(map);
                var geometry = BuildMember(line.Start, line.End, xandy.B, xandy.H);
                if (geometry is null)
                {
                    skipped++;
                    continue;
                }

                elements.Add(BuildElement("column", map, snapshotPath, geometry));
            }
            catch
            {
                skipped++;
            }
        }
    }

    private static RemoteInteropElement BuildElement(
        string elementType,
        IReadOnlyDictionary<string, object> map,
        string snapshotPath,
        Brep geometry)
    {
        var id = ReadString(map, "id");
        var elementId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("D") : id;

        return new RemoteInteropElement(
            elementId,
            ComputeElementHash(map),
            elementType,
            snapshotPath,
            geometry);
    }

    private static Brep? BuildFloor(IReadOnlyList<Point3d> points, double thickness)
    {
        if (points.Count < 3)
        {
            return null;
        }

        var polyline = new Polyline(points);
        if (!polyline.IsClosed)
        {
            polyline.Add(points[0]);
        }

        var curve = new PolylineCurve(polyline);
        if (thickness > RhinoMath.ZeroTolerance)
        {
            var extrusion = Extrusion.Create(curve, thickness, true);
            return extrusion?.ToBrep();
        }

        return Brep.CreatePlanarBreps(curve, RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01)
            ?.FirstOrDefault();
    }

    private static Brep? BuildWall(IReadOnlyList<Point3d> points, double thickness, double height)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var segments = new List<Brep>();
        for (var index = 0; index < points.Count - 1; index++)
        {
            var segment = BuildWallSegment(points[index], points[index + 1], thickness, height);
            if (segment is not null)
            {
                segments.Add(segment);
            }
        }

        if (segments.Count == 0)
        {
            return null;
        }

        return Brep.JoinBreps(segments, RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01)?.FirstOrDefault()
            ?? segments[0];
    }

    private static Brep? BuildWallSegment(Point3d start, Point3d end, double thickness, double height)
    {
        var axis = new Vector3d(end.X - start.X, end.Y - start.Y, 0);
        if (!axis.Unitize())
        {
            return null;
        }

        var side = Vector3d.CrossProduct(Vector3d.ZAxis, axis);
        if (!side.Unitize())
        {
            side = Vector3d.YAxis;
        }

        var length = start.DistanceTo(end);
        var plane = new Plane(start, axis, side);
        return new Box(
            plane,
            new Interval(0, Math.Max(length, RhinoMath.ZeroTolerance)),
            new Interval(-Math.Max(thickness, 0.1) / 2.0, Math.Max(thickness, 0.1) / 2.0),
            new Interval(0, Math.Max(height, 0.1))).ToBrep();
    }

    private static Brep? BuildMember(Point3d start, Point3d end, double width, double depth)
    {
        var axis = end - start;
        if (!axis.Unitize())
        {
            return null;
        }

        var side = Vector3d.CrossProduct(Vector3d.ZAxis, axis);
        if (!side.Unitize())
        {
            side = Vector3d.YAxis;
        }

        var length = start.DistanceTo(end);
        var plane = new Plane(start, axis, side);
        return new Box(
            plane,
            new Interval(0, Math.Max(length, RhinoMath.ZeroTolerance)),
            new Interval(-Math.Max(width, 0.1) / 2.0, Math.Max(width, 0.1) / 2.0),
            new Interval(-Math.Max(depth, 0.1) / 2.0, Math.Max(depth, 0.1) / 2.0)).ToBrep();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object>> ReadArray(
        IReadOnlyDictionary<string, object> data,
        string key)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is IEnumerable<object> values)
        {
            return values
                .OfType<IReadOnlyDictionary<string, object>>()
                .ToList();
        }

        return [];
    }

    private static IReadOnlyDictionary<string, object> ReadMapOrEmpty(
        IReadOnlyDictionary<string, object> data,
        string key)
    {
        return data.TryGetValue(key, out var raw) && raw is IReadOnlyDictionary<string, object> map
            ? map
            : new Dictionary<string, object>();
    }

    private static RemoteLine ReadLine(IReadOnlyDictionary<string, object> data, string key)
    {
        var points = ReadPointArray(data, key);
        if (points.Count < 2)
        {
            return new RemoteLine(Point3d.Unset, Point3d.Unset);
        }

        return new RemoteLine(points[0], points[1]);
    }

    private static IReadOnlyList<Point3d> ReadPointArray(
        IReadOnlyDictionary<string, object> data,
        string key)
    {
        return ReadArray(data, key)
            .Select(ReadPoint)
            .ToList();
    }

    private static Point3d ReadPoint(IReadOnlyDictionary<string, object> data, string key)
    {
        return ReadPoint(ReadMapOrEmpty(data, key));
    }

    private static Point3d ReadPoint(IReadOnlyDictionary<string, object> data)
    {
        return new Point3d(
            ReadDouble(data, "x"),
            ReadDouble(data, "y"),
            ReadDouble(data, "z"));
    }

    private static RemoteXandy ReadXandy(IReadOnlyDictionary<string, object> data)
    {
        var xandy = ReadMapOrEmpty(data, "xandy");
        var b = ReadDouble(xandy, "b", 1.0);
        var h = ReadDouble(xandy, "h", b);
        return new RemoteXandy(b, h);
    }

    private static string? ReadString(IReadOnlyDictionary<string, object> data, string key)
    {
        return data.TryGetValue(key, out var raw) ? raw?.ToString() : null;
    }

    private static double ReadDouble(
        IReadOnlyDictionary<string, object> data,
        string key,
        double fallback = 0)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
        {
            return fallback;
        }

        return raw switch
        {
            double value => value,
            float value => value,
            decimal value => (double)value,
            long value => value,
            int value => value,
            string value when double.TryParse(value, out var parsed) => parsed,
            _ => fallback
        };
    }

    private static Dictionary<string, string> ReadElementHashes(IReadOnlyDictionary<string, object> snapshotData)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ReadElementHashes(snapshotData, "floors", hashes);
        ReadElementHashes(snapshotData, "walls", hashes);
        ReadElementHashes(snapshotData, "beams", hashes);
        ReadElementHashes(snapshotData, "columns", hashes);
        return hashes;
    }

    private static void ReadElementHashes(
        IReadOnlyDictionary<string, object> snapshotData,
        string key,
        IDictionary<string, string> hashes)
    {
        foreach (var map in ReadArray(snapshotData, key))
        {
            var id = ReadString(map, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                hashes[id] = ComputeElementHash(map);
            }
        }
    }

    private static string ComputeElementHash(IReadOnlyDictionary<string, object> map)
    {
        var builder = new StringBuilder();
        AppendStableValue(builder, map);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendStableValue(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case IReadOnlyDictionary<string, object> map:
                builder.Append('{');
                foreach (var key in map.Keys.OrderBy(key => key, StringComparer.Ordinal))
                {
                    builder.Append(key);
                    builder.Append(':');
                    AppendStableValue(builder, map[key]);
                    builder.Append(',');
                }

                builder.Append('}');
                break;
            case IEnumerable<object> values:
                builder.Append('[');
                foreach (var item in values)
                {
                    AppendStableValue(builder, item);
                    builder.Append(',');
                }

                builder.Append(']');
                break;
            case IFormattable formattable:
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append(value);
                break;
        }
    }
}

internal sealed record RemoteInteropElement(
    string Id,
    string DataHash,
    string ElementType,
    string SnapshotPath,
    Brep Geometry);

internal sealed record RemoteLine(Point3d Start, Point3d End);

internal sealed record RemoteXandy(double B, double H);

public sealed record FirestoreApplyResult(int Created, int Updated, int Deleted, int Skipped);

using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using SixCharis.RhinoReviewInterop.Schema;

namespace SixCharis.RhinoReviewInterop.Extraction;

internal static class GeometryConverters
{
    public static PointData ToPointData(Point3d point)
    {
        return new PointData
        {
            X = Round(point.X),
            Y = Round(point.Y),
            Z = Round(point.Z)
        };
    }

    public static List<PointData> ToPointPair(Line line)
    {
        return [ToPointData(line.From), ToPointData(line.To)];
    }

    public static List<PointData> ToPointList(IEnumerable<Point3d> points)
    {
        return points.Select(ToPointData).ToList();
    }

    public static bool TryGetCurveLine(Curve curve, out Line line)
    {
        if (curve is LineCurve lineCurve)
        {
            line = lineCurve.Line;
            return true;
        }

        var start = curve.PointAtStart;
        var end = curve.PointAtEnd;
        line = new Line(start, end);
        return line.IsValid && line.Length > RhinoMath.ZeroTolerance;
    }

    public static List<PointData> GetFootprint(GeometryBase geometry, double tolerance)
    {
        if (TryGetPlanarCurveFootprint(geometry, tolerance, out var curvePoints))
        {
            return ToPointList(ClosePolyline(curvePoints));
        }

        return ToPointList(BoundingBoxFootprint(geometry.GetBoundingBox(true)));
    }

    public static Line LongestBoundingBoxAxis(BoundingBox box)
    {
        var center = box.Center;
        var xLength = box.Max.X - box.Min.X;
        var yLength = box.Max.Y - box.Min.Y;
        var zLength = box.Max.Z - box.Min.Z;

        if (zLength >= xLength && zLength >= yLength)
        {
            return new Line(
                new Point3d(center.X, center.Y, box.Min.Z),
                new Point3d(center.X, center.Y, box.Max.Z));
        }

        if (xLength >= yLength)
        {
            return new Line(
                new Point3d(box.Min.X, center.Y, center.Z),
                new Point3d(box.Max.X, center.Y, center.Z));
        }

        return new Line(
            new Point3d(center.X, box.Min.Y, center.Z),
            new Point3d(center.X, box.Max.Y, center.Z));
    }

    public static XandyData ProfileFromBoundingBox(BoundingBox box)
    {
        var dimensions = new[]
        {
            Math.Abs(box.Max.X - box.Min.X),
            Math.Abs(box.Max.Y - box.Min.Y),
            Math.Abs(box.Max.Z - box.Min.Z)
        }.OrderBy(value => value).ToArray();

        return new XandyData
        {
            B = Round(dimensions[0]),
            H = Round(dimensions[1])
        };
    }

    public static double Round(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public static double ReadDouble(RhinoObject rhinoObject, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = rhinoObject.Attributes.GetUserString(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = rhinoObject.Geometry.GetUserString(key);
            }

            if (double.TryParse(value, out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    public static string ReadMaterial(RhinoObject rhinoObject)
    {
        var material = rhinoObject.Attributes.GetUserString("material");
        if (string.IsNullOrWhiteSpace(material))
        {
            material = rhinoObject.Geometry.GetUserString("material");
        }

        return string.Equals(material, "wood", StringComparison.OrdinalIgnoreCase)
            ? "wood"
            : "concrete";
    }

    private static bool TryGetPlanarCurveFootprint(
        GeometryBase geometry,
        double tolerance,
        out List<Point3d> points)
    {
        points = [];

        if (geometry is Curve curve)
        {
            return TryCurveToPoints(curve, tolerance, out points);
        }

        if (geometry is Brep brep)
        {
            return TryBrepPlanarFaceFootprint(brep, tolerance, out points);
        }

        if (geometry is Extrusion extrusion)
        {
            return TryBrepPlanarFaceFootprint(extrusion.ToBrep(), tolerance, out points);
        }

        return false;
    }

    private static bool TryBrepPlanarFaceFootprint(Brep brep, double tolerance, out List<Point3d> points)
    {
        points = [];
        Curve? bestCurve = null;
        var bestArea = 0.0;

        foreach (var face in brep.Faces)
        {
            if (!face.TryGetPlane(out var plane, tolerance))
            {
                continue;
            }

            if (Math.Abs(plane.Normal.Z) < 0.9)
            {
                continue;
            }

            var loopCurve = face.OuterLoop?.To3dCurve();
            if (loopCurve is null)
            {
                continue;
            }

            var areaProperties = AreaMassProperties.Compute(loopCurve);
            var area = areaProperties?.Area ?? 0;
            if (area > bestArea)
            {
                bestCurve?.Dispose();
                bestCurve = loopCurve;
                bestArea = area;
            }
            else
            {
                loopCurve.Dispose();
            }
        }

        if (bestCurve is null)
        {
            return false;
        }

        using (bestCurve)
        {
            return TryCurveToPoints(bestCurve, tolerance, out points);
        }
    }

    private static bool TryCurveToPoints(Curve curve, double tolerance, out List<Point3d> points)
    {
        points = [];

        if (curve.TryGetPolyline(out var polyline))
        {
            points = polyline.ToList();
            return points.Count >= 2;
        }

        using var polylineCurve = curve.ToPolyline(tolerance, Math.PI / 36, tolerance, 0);
        if (polylineCurve is null)
        {
            return false;
        }

        points = polylineCurve.ToPolyline().ToList();
        return points.Count >= 2;
    }

    private static IEnumerable<Point3d> ClosePolyline(IReadOnlyList<Point3d> points)
    {
        foreach (var point in points)
        {
            yield return point;
        }

        if (points.Count > 0 && points[0].DistanceTo(points[^1]) > RhinoMath.ZeroTolerance)
        {
            yield return points[0];
        }
    }

    private static IEnumerable<Point3d> BoundingBoxFootprint(BoundingBox box)
    {
        var z = box.Min.Z;
        yield return new Point3d(box.Min.X, box.Min.Y, z);
        yield return new Point3d(box.Max.X, box.Min.Y, z);
        yield return new Point3d(box.Max.X, box.Max.Y, z);
        yield return new Point3d(box.Min.X, box.Max.Y, z);
        yield return new Point3d(box.Min.X, box.Min.Y, z);
    }
}

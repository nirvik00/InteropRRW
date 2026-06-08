using Rhino.Geometry;
using SixCharis.RhinoReviewInterop.Schema;

namespace SixCharis.RhinoReviewInterop.Extraction;

public static class WallExtractor
{
    public static List<WallElement> Extract(IEnumerable<LayerObject> objects, ExtractionContext context)
    {
        var walls = new List<WallElement>();

        foreach (var layerObject in objects)
        {
            var geometry = layerObject.RhinoObject.Geometry;
            var box = geometry.GetBoundingBox(true);
            if (!box.IsValid)
            {
                context.AddIssue("invalid_geometry", "Wall object has an invalid bounding box.", layerObject.RhinoObject, layerObject.LayerName);
                continue;
            }

            var centerLine = geometry is Curve curve && GeometryConverters.TryGetCurveLine(curve, out var curveLine)
                ? curveLine
                : HorizontalCenterLine(box);

            var height = GeometryConverters.ReadDouble(layerObject.RhinoObject, "height", "wallHeight");
            if (height <= 0)
            {
                height = Math.Abs(box.Max.Z - box.Min.Z);
            }

            var thickness = GeometryConverters.ReadDouble(layerObject.RhinoObject, "thickness", "wallThickness", "width");
            if (thickness <= 0)
            {
                thickness = HorizontalThickness(box);
            }

            walls.Add(new WallElement
            {
                Id = context.StableElementId(layerObject.RhinoObject, layerObject.LayerName),
                Polyline = GeometryConverters.ToPointPair(centerLine),
                Thickness = GeometryConverters.Round(thickness),
                Height = GeometryConverters.Round(height),
                Material = GeometryConverters.ReadMaterial(layerObject.RhinoObject)
            });
        }

        return walls;
    }

    private static Line HorizontalCenterLine(BoundingBox box)
    {
        var center = box.Center;
        var xLength = Math.Abs(box.Max.X - box.Min.X);
        var yLength = Math.Abs(box.Max.Y - box.Min.Y);

        if (xLength >= yLength)
        {
            return new Line(
                new Point3d(box.Min.X, center.Y, box.Min.Z),
                new Point3d(box.Max.X, center.Y, box.Min.Z));
        }

        return new Line(
            new Point3d(center.X, box.Min.Y, box.Min.Z),
            new Point3d(center.X, box.Max.Y, box.Min.Z));
    }

    private static double HorizontalThickness(BoundingBox box)
    {
        var xLength = Math.Abs(box.Max.X - box.Min.X);
        var yLength = Math.Abs(box.Max.Y - box.Min.Y);
        return Math.Min(xLength, yLength);
    }
}

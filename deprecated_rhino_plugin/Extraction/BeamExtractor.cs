using Rhino.Geometry;
using SixCharis.RhinoReviewInterop.Schema;

namespace SixCharis.RhinoReviewInterop.Extraction;

public static class BeamExtractor
{
    public static List<BeamElement> Extract(IEnumerable<LayerObject> objects, ExtractionContext context)
    {
        var beams = new List<BeamElement>();

        foreach (var layerObject in objects)
        {
            var geometry = layerObject.RhinoObject.Geometry;
            var box = geometry.GetBoundingBox(true);
            if (!box.IsValid)
            {
                context.AddIssue("invalid_geometry", "Beam object has an invalid bounding box.", layerObject.RhinoObject, layerObject.LayerName);
                continue;
            }

            var centerLine = geometry is Curve curve && GeometryConverters.TryGetCurveLine(curve, out var curveLine)
                ? curveLine
                : GeometryConverters.LongestBoundingBoxAxis(box);

            var profile = ReadProfile(layerObject, box);

            beams.Add(new BeamElement
            {
                Id = context.StableElementId(layerObject.RhinoObject, layerObject.LayerName),
                Line = GeometryConverters.ToPointPair(centerLine),
                Xandy = profile
            });
        }

        return beams;
    }

    private static XandyData ReadProfile(LayerObject layerObject, BoundingBox box)
    {
        var width = GeometryConverters.ReadDouble(layerObject.RhinoObject, "profileWidth", "beamWidth", "width");
        var depth = GeometryConverters.ReadDouble(layerObject.RhinoObject, "profileDepth", "beamDepth", "depth", "height");

        if (width > 0 && depth > 0)
        {
            return new XandyData
            {
                B = GeometryConverters.Round(width),
                H = GeometryConverters.Round(depth)
            };
        }

        return GeometryConverters.ProfileFromBoundingBox(box);
    }
}

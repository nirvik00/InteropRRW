using System;
using System.Collections.Generic;

using Rhino.Geometry;

using InteropRhino.Extraction;
using InteropRhino.Schema;
namespace InteropRhino.Extraction
{


    public static class FloorExtractor
    {
        public static List<FloorElement> Extract(IEnumerable<LayerObject> objects, ExtractionContext context)
        {
            var floors = new List<FloorElement>();

            foreach (var layerObject in objects)
            {
                var geometry = layerObject.RhinoObject.Geometry;
                var box = geometry.GetBoundingBox(true);
                if (!box.IsValid)
                {
                    context.AddIssue("invalid_geometry", "Floor object has an invalid bounding box.", layerObject.RhinoObject, layerObject.LayerName);
                    continue;
                }

                floors.Add(new FloorElement
                {
                    Id = context.StableElementId(layerObject.RhinoObject, layerObject.LayerName),
                    Polyline = GeometryConverters.GetFootprint(geometry, context.Tolerance),
                    Thickness = GeometryConverters.Round(Math.Abs(box.Max.Z - box.Min.Z)),
                    Material = GeometryConverters.ReadMaterial(layerObject.RhinoObject)
                });
            }

            return floors;
        }
    }
}
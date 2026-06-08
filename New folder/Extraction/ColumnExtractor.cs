using System;
using System.Collections.Generic;

using Rhino.Geometry;

using InteropRhino.Extraction;
using InteropRhino.Schema;
namespace InteropRhino.Extraction
{


    public static class ColumnExtractor
    {
        public static List<ColumnElement> Extract(IEnumerable<LayerObject> objects, ExtractionContext context)
        {
            var columns = new List<ColumnElement>();

            foreach (var layerObject in objects)
            {
                var geometry = layerObject.RhinoObject.Geometry;
                var box = geometry.GetBoundingBox(true);
                if (!box.IsValid)
                {
                    context.AddIssue("invalid_geometry", "Column object has an invalid bounding box.", layerObject.RhinoObject, layerObject.LayerName);
                    continue;
                }

                var centerPoint = new Point3d(box.Center.X, box.Center.Y, box.Min.Z);
                var height = GeometryConverters.ReadDouble(layerObject.RhinoObject, "height", "columnHeight");
                if (height <= 0)
                {
                    height = Math.Abs(box.Max.Z - box.Min.Z);
                }
                var topPoint = new Point3d(centerPoint.X, centerPoint.Y, centerPoint.Z + height);

                columns.Add(new ColumnElement
                {
                    Id = context.StableElementId(layerObject.RhinoObject, layerObject.LayerName),
                    Line = [GeometryConverters.ToPointData(centerPoint), GeometryConverters.ToPointData(topPoint)],
                    Xandy = ReadProfile(layerObject, box)
                });
            }

            return columns;
        }

        private static XandyData ReadProfile(LayerObject layerObject, BoundingBox box)
        {
            var width = GeometryConverters.ReadDouble(layerObject.RhinoObject, "profileWidth", "columnWidth", "width");
            var depth = GeometryConverters.ReadDouble(layerObject.RhinoObject, "profileDepth", "columnDepth", "depth");

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

}
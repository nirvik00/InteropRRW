using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CharisRevitConnector
{
    /// <summary>
    /// One pending change parsed from a Firestore document (on the listener thread),
    /// applied later on the Revit thread. A single shape covers all families; each
    /// handler reads only the fields relevant to its category. Lengths are in feet.
    /// </summary>
    internal sealed class ElementUpdate
    {
        public ElementCategory Category { get; }
        /// <summary>Firestore document key — maps to one Revit element.</summary>
        public string Id { get; }
        public bool IsDeleted { get; }
        /// <summary>Floor outline (closed) or wall centerline.</summary>
        public IReadOnlyList<XYZ> Polyline { get; }
        /// <summary>Beam/column member line [start, end].</summary>
        public LineEndpoints? Line { get; }
        public double Thickness { get; }  // floor / wall
        public double Height { get; }     // wall
        public double B { get; }          // beam / column cross-section width
        public double H { get; }          // beam / column cross-section depth
        public string Material { get; }   // floor / wall

        public ElementUpdate(
            ElementCategory category,
            string id,
            bool isDeleted,
            IReadOnlyList<XYZ> polyline,
            LineEndpoints? line,
            double thickness,
            double height,
            double b,
            double h,
            string material)
        {
            Category = category;
            Id = id;
            IsDeleted = isDeleted;
            Polyline = polyline;
            Line = line;
            Thickness = thickness;
            Height = height;
            B = b;
            H = h;
            Material = material;
        }

        public ElementUpdate(
           ElementCategory category,
           string id,
           IReadOnlyList<XYZ> polyline,
           double thickness,
           double height,
           string material)
        {
            Category = category;
            Id = id;
            Polyline = polyline;
            Thickness = thickness;
            Height = height;
            Material = material;
        }
    }
}
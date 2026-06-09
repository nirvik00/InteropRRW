using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CharisRevitConnector
{
    internal sealed class ElementState
    {
        public ElementCategory Category { get; }
        public string Id { get; }
        public IReadOnlyList<XYZ> Polyline { get; }
        public LineEndpoints? Line { get; }
        public double Thickness { get; }
        public double Height { get; }
        public double B { get; }
        public double H { get; }
        public string Material { get; }

        public ElementState(
            ElementCategory category,
            string id,
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
            Polyline = polyline;
            Line = line;
            Thickness = thickness;
            Height = height;
            B = b;
            H = h;
            Material = material;
        }
    }

    internal struct LineEndpoints
    {
        public XYZ Start { get; }
        public XYZ End { get; }

        public LineEndpoints(XYZ start, XYZ end)
        {
            Start = start;
            End = end;
        }
    }

    internal sealed class ManagedElement
    {
        public ElementId ElementId { get; }
        public ElementState State { get; }

        public ManagedElement(ElementId elementId, ElementState state)
        {
            ElementId = elementId;
            State = state;
        }
    }
}
using Autodesk.Revit.DB;

namespace CharisRevitConnector;

/// <summary>
/// Current state of a Charis-managed element, read from the model for the
/// reverse (Revit → Firestore) sync. Lengths are in feet.
/// </summary>
internal sealed class ElementState
{
    public required ElementCategory Category { get; init; }
    public required string Id { get; init; }

    public IReadOnlyList<XYZ>? Polyline { get; init; }
    public (XYZ Start, XYZ End)? Line { get; init; }

    public double Thickness { get; init; }
    public double Height { get; init; }
    public double B { get; init; }
    public double H { get; init; }

    public string? Material { get; init; }
}

/// <summary>A managed element paired with its read-back state (for the registry + push).</summary>
internal readonly record struct ManagedElement(ElementId ElementId, ElementState State);

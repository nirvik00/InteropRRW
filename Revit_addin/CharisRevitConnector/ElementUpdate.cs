using Autodesk.Revit.DB;

namespace CharisRevitConnector;

/// <summary>
/// One pending change parsed from a Firestore document (on the listener thread),
/// applied later on the Revit thread. A single shape covers all families; each
/// handler reads only the fields relevant to its category. Lengths are in feet.
/// </summary>
internal sealed class ElementUpdate
{
    public required ElementCategory Category { get; init; }

    /// <summary>Firestore document key — maps to one Revit element.</summary>
    public required string Id { get; init; }

    public bool IsDeleted { get; init; }

    /// <summary>Floor outline (closed) or wall centerline.</summary>
    public IReadOnlyList<XYZ>? Polyline { get; init; }

    /// <summary>Beam/column member line [start, end].</summary>
    public (XYZ Start, XYZ End)? Line { get; init; }

    public double Thickness { get; init; } // floor / wall
    public double Height { get; init; }    // wall
    public double B { get; init; }         // beam / column cross-section width
    public double H { get; init; }         // beam / column cross-section depth

    public string? Material { get; init; } // floor / wall
}

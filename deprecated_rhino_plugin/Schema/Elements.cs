namespace SixCharis.RhinoReviewInterop.Schema;

public abstract class InteropElement
{
    public string Id { get; init; } = Guid.NewGuid().ToString("D");
    public string Type { get; init; } = string.Empty;
}

public sealed class FloorElement : InteropElement
{
    public FloorElement()
    {
        Type = "floor";
    }

    public List<PointData> Polyline { get; init; } = [];
    public double Thickness { get; init; }
    public string Material { get; init; } = "concrete";
}

public sealed class WallElement : InteropElement
{
    public WallElement()
    {
        Type = "wall";
    }

    public List<PointData> Polyline { get; init; } = [];
    public double Thickness { get; init; }
    public double Height { get; init; }
    public string Material { get; init; } = "concrete";
}

public sealed class BeamElement : InteropElement
{
    public BeamElement()
    {
        Type = "beam";
    }

    public List<PointData> Line { get; init; } = [];
    public XandyData Xandy { get; init; } = new();
}

public sealed class ColumnElement : InteropElement
{
    public ColumnElement()
    {
        Type = "column";
    }

    public List<PointData> Line { get; init; } = [];
    public XandyData Xandy { get; init; } = new();
}

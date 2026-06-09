namespace SixCharis.RhinoReviewInterop.Schema;

public sealed class PointData
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
}

public sealed class LineData
{
    public PointData Start { get; init; } = new();
    public PointData End { get; init; } = new();
}

public sealed class XandyData
{
    public double B { get; init; }
    public double H { get; init; }
}

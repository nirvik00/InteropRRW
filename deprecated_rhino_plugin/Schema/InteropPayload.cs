namespace SixCharis.RhinoReviewInterop.Schema;

public sealed class InteropPayload
{
    public string SchemaVersion { get; init; } = "rhino-review-interop/0.2";
    public string SourceApplication { get; init; } = "Rhino";
    public string? DocumentName { get; init; }
    public string Units { get; init; } = string.Empty;
    public DateTimeOffset ExtractedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<FloorElement> Floors { get; init; } = [];
    public List<WallElement> Walls { get; init; } = [];
    public List<BeamElement> Beams { get; init; } = [];
    public List<ColumnElement> Columns { get; init; } = [];
    public List<ExtractionIssue> Issues { get; init; } = [];
}

public sealed class ExtractionIssue
{
    public string Type { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? RhinoObjectId { get; init; }
    public string? Layer { get; init; }
}

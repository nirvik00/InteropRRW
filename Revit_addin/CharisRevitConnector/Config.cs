namespace CharisRevitConnector;

/// <summary>
/// Sync target + interop constants for the rhino-review document model: a single
/// document holding floors[]/walls[]/beams[]/columns[] arrays plus origin
/// metadata (writerInstanceId / writerOperationId / updatedAtUtc / schemaVersion).
/// </summary>
internal static class Config
{
    public const string CollectionId = "rhinoReviewV2";
    public const string DocumentId = "test-model";

    /// <summary>Marks documents this connector authors, so we ignore our own echoes.</summary>
    public const string WriterInstanceId = "revit-charis";

    public const string SchemaVersion = "rhino-review-interop/0.2";
}

namespace SixCharis.RhinoReviewInterop.Firebase;

public sealed class FirebaseOptions
{
    public string DatabaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string ModelId { get; init; } = "test-model";
    public string FirestoreCollection { get; init; } = "rhinoReview";
    public string FirestoreDocumentPath { get; init; } = string.Empty;
    public string ServiceAccountKeyPath { get; init; } = string.Empty;

    public void ValidateRealtimeDatabase()
    {
        if (string.IsNullOrWhiteSpace(DatabaseUrl))
        {
            throw new InvalidOperationException("Firebase config is missing databaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Firebase config is missing apiKey.");
        }

        if (string.IsNullOrWhiteSpace(ModelId))
        {
            throw new InvalidOperationException("Firebase config is missing modelId.");
        }
    }

    public void ValidateFirestore()
    {
        if (string.IsNullOrWhiteSpace(ModelId))
        {
            throw new InvalidOperationException("Firebase config is missing modelId.");
        }

        if (string.IsNullOrWhiteSpace(FirestoreCollection))
        {
            throw new InvalidOperationException("Firebase config is missing firestoreCollection.");
        }
    }
}

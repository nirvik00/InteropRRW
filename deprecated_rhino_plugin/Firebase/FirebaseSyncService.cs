using SixCharis.RhinoReviewInterop.Schema;

namespace SixCharis.RhinoReviewInterop.Firebase;

public static class FirebaseSyncService
{
    public static async Task<FirebasePushResult> PushLatestAsync(
        InteropPayload payload,
        CancellationToken cancellationToken = default)
    {
        var options = FirebaseOptionsLoader.LoadForRealtimeDatabase();
        var client = new FirebaseRealtimeDatabaseClient(options);
        return await client.PushLatestAsync(payload, cancellationToken);
    }
}

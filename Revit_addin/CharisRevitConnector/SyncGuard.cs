namespace CharisRevitConnector;

/// <summary>
/// Breaks the Firestore → Revit → Firestore echo loop. While Firestore-driven
/// edits are being applied to the model, this guard is raised; the
/// <see cref="RevitChangeWatcher"/> checks it and skips writing those changes
/// back to Firestore.
///
/// Thread-local because both the apply (ExternalEvent handler) and the
/// DocumentChanged callback run on Revit's main thread, and the DocumentChanged
/// fires synchronously during the apply's transaction commit.
/// </summary>
internal static class SyncGuard
{
    [ThreadStatic] private static bool _applying;

    public static bool IsApplyingFromFirestore => _applying;

    public static IDisposable Apply() => new Scope();

    private sealed class Scope : IDisposable
    {
        public Scope() => _applying = true;
        public void Dispose() => _applying = false;
    }
}

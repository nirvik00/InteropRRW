namespace CharisRevitConnector;

/// <summary>
/// Tracks the writerOperationId of writes this session issued, so the listener
/// can ignore the snapshot echoes of our own writes — and ONLY those. Unlike a
/// blanket "skip anything stamped revit-charis", this lets the initial connect
/// snapshot apply even when we were the last writer (e.g. from a prior session).
/// </summary>
internal static class WriteTracker
{
    private static readonly HashSet<string> Ours = new();
    private static readonly object Gate = new();

    public static void Remember(string operationId)
    {
        lock (Gate)
            Ours.Add(operationId);
    }

    /// <summary>True (and forgets it) if this op-id is one of ours.</summary>
    public static bool ConsumeIfOurs(string? operationId)
    {
        if (string.IsNullOrEmpty(operationId))
            return false;
        lock (Gate)
            return Ours.Remove(operationId);
    }
}

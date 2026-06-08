using Google.Cloud.Firestore;

namespace CharisRevitConnector;

/// <summary>
/// Writes Revit-side changes back into the single layered document via
/// read-modify-write on one array at a time. Writes are serialized (so two edits
/// don't clobber each other) and fire-and-forget (the Revit thread is never
/// blocked). Every write stamps our writerInstanceId so our own echoes are
/// ignored by the listener.
/// </summary>
internal sealed class DocumentWriter
{
    private readonly FirestoreDb _db;
    private readonly IReadOnlyDictionary<ElementCategory, IFamilyHandler> _handlers;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DocumentWriter(FirestoreDb db, IReadOnlyDictionary<ElementCategory, IFamilyHandler> handlers)
    {
        _db = db;
        _handlers = handlers;
    }

    public void PushElement(ElementState state)
    {
        if (!_handlers.TryGetValue(state.Category, out IFamilyHandler? handler))
            return;

        Dictionary<string, object> entry = handler.ToFirestore(state);
        string arrayKey = handler.ArrayKey;
        string id = state.Id;

        FireAndForget(async () =>
        {
            List<object> list = await ReadArrayAsync(arrayKey);
            int idx = list.FindIndex(m => IdOf(m) == id);
            if (idx >= 0) list[idx] = entry; else list.Add(entry);
            await WriteArrayAsync(arrayKey, list);
        }, $"push {state.Category} {id}");
    }

    public void DeleteElement(ElementCategory category, string id)
    {
        if (!_handlers.TryGetValue(category, out IFamilyHandler? handler))
            return;

        string arrayKey = handler.ArrayKey;
        FireAndForget(async () =>
        {
            List<object> list = await ReadArrayAsync(arrayKey);
            int removed = list.RemoveAll(m => IdOf(m) == id);
            if (removed > 0)
                await WriteArrayAsync(arrayKey, list);
        }, $"delete {category} {id}");
    }

    // ---- internals ------------------------------------------------------

    private DocumentReference Doc => _db.Collection(Config.CollectionId).Document(Config.DocumentId);

    private async Task<List<object>> ReadArrayAsync(string arrayKey)
    {
        DocumentSnapshot snap = await Doc.GetSnapshotAsync();
        if (snap.Exists && snap.ToDictionary() is { } data
            && data.TryGetValue(arrayKey, out object? v) && v is IEnumerable<object> arr)
        {
            return arr.ToList();
        }
        return new List<object>();
    }

    private Task WriteArrayAsync(string arrayKey, List<object> list)
    {
        string operationId = Guid.NewGuid().ToString();
        WriteTracker.Remember(operationId); // so the listener ignores this write's echo

        var update = new Dictionary<string, object>
        {
            [arrayKey] = list,
            ["writerInstanceId"] = Config.WriterInstanceId,
            ["writerOperationId"] = operationId,
            ["updatedAtUtc"] = Timestamp.FromDateTime(DateTime.UtcNow),
            ["schemaVersion"] = Config.SchemaVersion,
        };
        return Doc.SetAsync(update, SetOptions.MergeAll);
    }

    private static string? IdOf(object item) =>
        item is IReadOnlyDictionary<string, object> m && m.TryGetValue("id", out object? v) ? v as string : null;

    private void FireAndForget(Func<Task> action, string what)
    {
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try
            {
                await action();
                Log.Info($"Pushed to Firestore: {what}.");
            }
            catch (Exception ex)
            {
                Log.Error($"Firestore write failed ({what})", ex);
            }
            finally
            {
                _gate.Release();
            }
        });
    }
}

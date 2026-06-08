using Autodesk.Revit.UI;
using Google.Cloud.Firestore;

namespace CharisRevitConnector;

/// <summary>
/// Realtime listener on the single layered document (rhinoReviewV2/test-model).
/// Each snapshot is the full desired model state. The callback runs on a
/// background thread: it parses the arrays into per-family element lists, hands
/// the complete snapshot to the apply handler, and raises the ExternalEvent.
/// Snapshots we authored (writerInstanceId == ours) are ignored to avoid loops.
/// </summary>
internal sealed class DocumentListener
{
    private readonly FirestoreDb _db;
    private readonly IReadOnlyList<IFamilyHandler> _handlers;
    private readonly SyncEventHandler _sink;
    private readonly ExternalEvent _externalEvent;
    private FirestoreChangeListener? _listener;

    public DocumentListener(FirestoreDb db, IReadOnlyList<IFamilyHandler> handlers,
                            SyncEventHandler sink, ExternalEvent externalEvent)
    {
        _db = db;
        _handlers = handlers;
        _sink = sink;
        _externalEvent = externalEvent;
    }

    public void Start()
    {
        DocumentReference docRef = _db.Collection(Config.CollectionId).Document(Config.DocumentId);
        _listener = docRef.Listen(snap =>
        {
            if (!snap.Exists)
            {
                Log.Info($"{Config.CollectionId}/{Config.DocumentId} does not exist yet.");
                return;
            }

            IReadOnlyDictionary<string, object> data = snap.ToDictionary();

            // Ignore only the echoes of writes WE issued this session (by op-id),
            // not the initial state — even if we were the last writer.
            if (WriteTracker.ConsumeIfOurs(FirestoreParse.Get(data, "writerOperationId") as string))
                return;

            var snapshot = new Dictionary<ElementCategory, List<ElementUpdate>>();
            foreach (IFamilyHandler handler in _handlers)
            {
                var list = new List<ElementUpdate>();
                if (FirestoreParse.Get(data, handler.ArrayKey) is IEnumerable<object> arr)
                {
                    foreach (object item in arr)
                    {
                        if (item is IReadOnlyDictionary<string, object> map
                            && FirestoreParse.Get(map, "id") is string id && id.Length > 0)
                        {
                            list.Add(handler.ParseElement(id, map));
                        }
                    }
                }
                snapshot[handler.Category] = list;
            }

            Log.Info($"Snapshot from {Config.CollectionId}/{Config.DocumentId}: "
                     + string.Join(", ", snapshot.Select(kv => $"{kv.Key}={kv.Value.Count}")) + ".");

            _sink.SetPending(snapshot);
            _externalEvent.Raise();
        });
    }

    public async Task StopAsync()
    {
        if (_listener is not null)
        {
            await _listener.StopAsync();
            _listener = null;
        }
    }
}

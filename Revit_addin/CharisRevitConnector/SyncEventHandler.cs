using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System;
using System.Linq;


namespace CharisRevitConnector;

/// <summary>
/// Applies the latest document snapshot on Revit's main thread. The document is
/// the authoritative full state, so per family it creates/updates every element
/// present and deletes managed elements whose id is absent (delete-on-absence).
/// All of it runs in one transaction, guarded so the reverse watcher ignores it.
/// </summary>
internal sealed class SyncEventHandler : IExternalEventHandler
{
    private readonly IReadOnlyDictionary<ElementCategory, IFamilyHandler> _handlers;
    private readonly object _gate = new();
    private Dictionary<ElementCategory, List<ElementUpdate>>? _pending;

    public SyncEventHandler(IReadOnlyDictionary<ElementCategory, IFamilyHandler> handlers) => _handlers = handlers;

    /// <summary>Replace the pending desired state (latest snapshot wins).</summary>
    public void SetPending(Dictionary<ElementCategory, List<ElementUpdate>> snapshot)
    {
        lock (_gate)
            _pending = snapshot;
    }

    public void Execute(UIApplication app)
    {
        Dictionary<ElementCategory, List<ElementUpdate>>? snapshot;
        lock (_gate)
        {
            snapshot = _pending;
            _pending = null;
        }
        if (snapshot is null)
            return;

        Document? doc = app.ActiveUIDocument?.Document;
        if (doc is null)
            return;

        // A transaction can start only when neither read-only nor already modifiable.
        if (doc.IsReadOnly || doc.IsModifiable)
        {
            lock (_gate)
                _pending ??= snapshot; // retry on next raise
            Log.Info("Cannot start a transaction right now; deferred snapshot.");
            return;
        }

        using (SyncGuard.Apply())
        using (var t = new Transaction(doc, "Charis: rhinoReviewV2 sync"))
        {
            t.Start();
            try
            {
                // Phase 1 — create/update everything present.
                var desiredByCategory = new Dictionary<ElementCategory, HashSet<string>>();
                foreach (var pair in _handlers)
                {
                    ElementCategory category = pair.Key;
                    IFamilyHandler handler = pair.Value;

                    List<ElementUpdate> desired =
                        snapshot.TryGetValue(category, out List<ElementUpdate> d)
                            ? d
                            : new List<ElementUpdate>();

                    var desiredIds = new HashSet<string>();

                    foreach (ElementUpdate u in desired)
                    {
                        desiredIds.Add(u.Id);
                        handler.CreateOrUpdate(doc, u);
                    }

                    desiredByCategory[category] = desiredIds;
                }

                // Regenerate so newly-created elements (and their sketches) are
                // queryable before the reconcile pass reads them.
                doc.Regenerate();

                // Phase 2 — delete-on-absence: managed but no longer in the document.
                foreach (var pair in _handlers)
                {
                    ElementCategory category = pair.Key;
                    IFamilyHandler handler = pair.Value;

                    HashSet<string> desiredIds = desiredByCategory[category];

                    foreach (ManagedElement me in handler.ReadAll(doc).ToList())
                    {
                        if (!desiredIds.Contains(me.State.Id))
                            handler.Delete(doc, me.State.Id);
                    }
                }

                doc.Regenerate();
                t.Commit();
            }
            catch (Exception ex)
            {
                if (t.HasStarted() && !t.HasEnded())
                    t.RollBack();
                Log.Error("Failed to apply snapshot", ex);
            }
        }
    }

    public string GetName() => "Charis sync";
}

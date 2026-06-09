using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace CharisRevitConnector;

/// <summary>
/// Reverse sync (Revit → document). Subscribed to ControlledApplication's
/// DocumentChanged. Routes each changed/deleted managed element to its family
/// handler and writes it back into the layered document via the DocumentWriter.
/// Edits made by the forward sync are skipped via <see cref="SyncGuard"/>.
/// </summary>
internal sealed class RevitChangeWatcher
{
    private readonly IReadOnlyList<IFamilyHandler> _handlers;

    // ElementId -> (category, element id) so deletions (element already gone)
    // map back to the right array entry.
    private readonly Dictionary<ElementId, (ElementCategory Category, string Id)> _registry = new();
    private DocumentWriter? _writer;

    public RevitChangeWatcher(IReadOnlyList<IFamilyHandler> handlers) => _handlers = handlers;

    public void StartWritingTo(DocumentWriter writer, Document? activeDoc)
    {
        _writer = writer;
        _registry.Clear();
        if (activeDoc is not null)
            Seed(activeDoc);
    }

    public void StopWriting()
    {
        _writer = null;
        _registry.Clear();
    }

    public void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        Document doc = e.GetDocument();

        var deleted = new List<(ElementCategory Category, string Id)>();
        foreach (ElementId id in e.GetDeletedElementIds())
        {
            if (_registry.TryGetValue(id, out (ElementCategory, string) r))
            {
                deleted.Add(r);
                _registry.Remove(id);
            }
        }

        var changedIds = e.GetAddedElementIds().Concat(e.GetModifiedElementIds()).ToList();
        var pushes = new List<ElementState>();

        foreach (IFamilyHandler handler in _handlers)
        {
            foreach (ManagedElement me in handler.ReadAffected(doc, changedIds))
            {
                _registry[me.ElementId] = (me.State.Category, me.State.Id);
                pushes.Add(me.State);
            }
        }

        if (_writer is null || SyncGuard.IsApplyingFromFirestore)
            return;

        foreach (ElementState state in pushes)
            _writer.PushElement(state);

        foreach ((ElementCategory category, string id) in deleted)
            _writer.DeleteElement(category, id);
    }

    private void Seed(Document doc)
    {
        foreach (IFamilyHandler handler in _handlers)
            foreach (ManagedElement me in handler.ReadAll(doc))
                _registry[me.ElementId] = (me.State.Category, me.State.Id);
    }
}

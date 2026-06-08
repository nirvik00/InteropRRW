using Autodesk.Revit.DB;

namespace CharisRevitConnector;

/// <summary>
/// One per element family. Encapsulates everything family-specific: which array
/// in the layered document it owns, how to parse an element entry, how to
/// create/update/delete the Revit element (inside a caller-managed transaction),
/// how to read a managed element's state for the reverse sync, and how to
/// serialize that state back to an array entry.
/// </summary>
internal interface IFamilyHandler
{
    ElementCategory Category { get; }

    /// <summary>Top-level array key in the document (e.g. "floors").</summary>
    string ArrayKey { get; }

    /// <summary>Parse one element entry (a map from the array) into an update.</summary>
    ElementUpdate ParseElement(string id, IReadOnlyDictionary<string, object> data);

    /// <summary>Create or update the element. Assumes an open transaction.</summary>
    void CreateOrUpdate(Document doc, ElementUpdate update);

    /// <summary>Delete the element for this id. Assumes an open transaction.</summary>
    void Delete(Document doc, string id);

    /// <summary>Managed elements affected by a set of changed element ids (reverse sync).</summary>
    IEnumerable<ManagedElement> ReadAffected(Document doc, IEnumerable<ElementId> changedIds);

    /// <summary>All managed elements in the document (reconcile + delete registry).</summary>
    IEnumerable<ManagedElement> ReadAll(Document doc);

    /// <summary>Serialize a state to its document array entry (includes id + type).</summary>
    Dictionary<string, object> ToFirestore(ElementState state);

    /// <summary>
    /// Optional: log whether the model is ready for this family (required
    /// families/parameters present). Called once on connect. Default: no-op.
    /// </summary>
    void LogReadiness(Document doc) { }
}

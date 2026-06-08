using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CharisRevitConnector;

/// <summary>
/// Shared base for line-driven structural members (beams, columns). Schema:
/// { line: [Vec3, Vec3], xandy: { b, h } }. Cross-section b×h lives on a
/// per-section FamilySymbol named "Test - Beam - 1x2" / "Test - Column - 1x1",
/// duplicated from whatever rectangular family is already loaded (auto-detect).
/// </summary>
internal abstract class LineMemberHandler : IFamilyHandler
{
    private const double Tol = 1.0e-4;

    public abstract ElementCategory Category { get; }
    public abstract string ArrayKey { get; }

    protected abstract BuiltInCategory SymbolCategory { get; }
    protected abstract StructuralType StructuralType { get; }
    protected abstract string TypeName(double b, double h);
    protected abstract string TypePrefix { get; }
    protected abstract string FirestoreType { get; }

    // b is section width, h is section depth/height; tolerate common param names.
    private static readonly string[] BNames = { "b", "Width", "Section Width" };
    private static readonly string[] HNames = { "h", "d", "Height", "Depth", "Section Height", "Section Depth" };

    public ElementUpdate ParseElement(string id, IReadOnlyDictionary<string, object> data)
    {
        double b = 0, h = 0;
        if (FirestoreParse.Get(data, "xandy") is IReadOnlyDictionary<string, object> xy)
        {
            b = FirestoreParse.ToDouble(FirestoreParse.Get(xy, "b"));
            h = FirestoreParse.ToDouble(FirestoreParse.Get(xy, "h"));
        }

        return new ElementUpdate
        {
            Category = Category,
            Id = id,
            Line = FirestoreParse.ReadLine(data, "line"),
            B = b,
            H = h,
        };
    }

    public void CreateOrUpdate(Document doc, ElementUpdate u)
    {
        if (u.Line is not (XYZ start, XYZ end) || start.DistanceTo(end) < Tol)
        {
            Log.Error($"{Category} '{u.Id}' skipped: line needs two distinct points.");
            return;
        }

        FamilySymbol? symbol = ResolveOrCreateSymbol(doc, u.B, u.H);
        if (symbol is null)
        {
            Log.Error($"{Category} '{u.Id}' skipped: no {SymbolCategory} family is loaded to use.");
            return;
        }

        Level level = RevitHelpers.LowestLevel(doc);
        FamilyInstance? existing = FindByKey(doc, u.Id);

        if (existing is not null)
        {
            if (existing.GetTypeId() != symbol.Id)
                existing.ChangeTypeId(symbol.Id);

            UpdateGeometry(existing, start, end, level);
            PreventJoins(existing);
            Log.Info($"Updated {FirestoreType} '{u.Id}' -> '{symbol.Name}'.");
            return;
        }

        FamilyInstance inst = CreateInstance(doc, start, end, symbol, level);
        RevitHelpers.SetComments(inst, u.Id);
        PreventJoins(inst);
        Log.Info($"Created {FirestoreType} '{u.Id}' as '{symbol.Name}'.");
    }

    // ---- geometry hooks (beams: line-based; columns override) -----------

    /// <summary>Create the instance from the member line. Default: line-based framing.</summary>
    protected virtual FamilyInstance CreateInstance(Document doc, XYZ start, XYZ end, FamilySymbol symbol, Level level) =>
        doc.Create.NewFamilyInstance(Line.CreateBound(start, end), symbol, level, StructuralType);

    /// <summary>Update an existing instance's geometry. Default: reset the location curve.</summary>
    protected virtual void UpdateGeometry(FamilyInstance instance, XYZ start, XYZ end, Level level)
    {
        Line line = Line.CreateBound(start, end);
        if (instance.Location is LocationCurve lc && !SameLine(lc.Curve, line))
            lc.Curve = line;
    }

    /// <summary>Read an instance's [start, end] back. Default: from the location curve.</summary>
    protected virtual (XYZ Start, XYZ End)? ReadEndpoints(FamilyInstance instance, Level level) =>
        instance.Location is LocationCurve lc ? (lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1)) : null;

    /// <summary>
    /// Keep streamed members free-standing (no cutback/coping joins to supports).
    /// Default no-op; beams override since join control is framing-specific.
    /// </summary>
    protected virtual void PreventJoins(FamilyInstance instance) { }

    public void Delete(Document doc, string id)
    {
        FamilyInstance? inst = FindByKey(doc, id);
        if (inst is null)
            return;

        ElementId symId = inst.GetTypeId();
        doc.Delete(inst.Id);
        DeleteSymbolIfUnused(doc, symId);
        Log.Info($"Deleted {FirestoreType} '{id}'.");
    }

    public IEnumerable<ManagedElement> ReadAll(Document doc)
    {
        foreach (FamilyInstance inst in ManagedInstances(doc))
            if (TryRead(inst, out ElementState st))
                yield return new ManagedElement(inst.Id, st);
    }

    public IEnumerable<ManagedElement> ReadAffected(Document doc, IEnumerable<ElementId> changedIds)
    {
        var seen = new HashSet<ElementId>();
        foreach (ElementId id in changedIds)
        {
            Element? el = doc.GetElement(id);
            if (el is FamilyInstance inst && IsManaged(inst))
            {
                if (seen.Add(inst.Id) && TryRead(inst, out ElementState st))
                    yield return new ManagedElement(inst.Id, st);
            }
            else if (el is FamilySymbol sym && sym.Name.StartsWith(TypePrefix, StringComparison.Ordinal))
            {
                foreach (FamilyInstance fi in ManagedInstances(doc).Where(i => i.GetTypeId() == id))
                    if (seen.Add(fi.Id) && TryRead(fi, out ElementState st))
                        yield return new ManagedElement(fi.Id, st);
            }
        }
    }

    public Dictionary<string, object> ToFirestore(ElementState state)
    {
        (XYZ s, XYZ e) = state.Line ?? (XYZ.Zero, XYZ.Zero);
        return new Dictionary<string, object>
        {
            ["id"] = state.Id,
            ["type"] = FirestoreType,
            ["line"] = new List<object>
            {
                new Dictionary<string, object> { ["x"] = s.X, ["y"] = s.Y, ["z"] = s.Z },
                new Dictionary<string, object> { ["x"] = e.X, ["y"] = e.Y, ["z"] = e.Z },
            },
            ["xandy"] = new Dictionary<string, object> { ["b"] = state.B, ["h"] = state.H },
        };
    }

    /// <summary>
    /// Choose the family symbol to clone for new sections: prefer one with
    /// literal b + h parameters (rectangular families), then Width + Height,
    /// then anything. Avoids accidentally picking a W-shape steel family.
    /// </summary>
    private FamilySymbol? PickBaseSymbol(Document doc)
    {
        List<FamilySymbol> symbols = new FilteredElementCollector(doc)
            .OfCategory(SymbolCategory).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();

        return symbols.FirstOrDefault(s => RevitHelpers.HasDimension(s, "b") && RevitHelpers.HasDimension(s, "h"))
            ?? symbols.FirstOrDefault(s => RevitHelpers.HasDimension(s, "Width") && RevitHelpers.HasDimension(s, "Height"))
            ?? symbols.FirstOrDefault();
    }

    public void LogReadiness(Document doc)
    {
        FamilySymbol? baseSymbol = PickBaseSymbol(doc);

        if (baseSymbol is null)
        {
            Log.Error($"{Category}: NO {SymbolCategory} family is loaded — {FirestoreType}s cannot be created.");
            return;
        }

        bool hasB = RevitHelpers.HasDimension(baseSymbol, BNames);
        bool hasH = RevitHelpers.HasDimension(baseSymbol, HNames);
        string verdict = hasB && hasH ? "READY (b + h found)" : "NOT READY — missing b/h";
        Log.Info($"{Category} readiness: {verdict}. Family '{baseSymbol.FamilyName}', symbol '{baseSymbol.Name}'. "
                 + $"b found={hasB}, h found={hasH}. Writable numeric params: {RevitHelpers.NumericParamNames(baseSymbol)}");
    }

    // ---- internals ------------------------------------------------------

    private bool TryRead(FamilyInstance inst, out ElementState state)
    {
        state = null!;
        if (!IsManaged(inst))
            return false;

        string? id = RevitHelpers.Comments(inst);
        if (string.IsNullOrEmpty(id))
            return false;

        (XYZ Start, XYZ End)? endpoints = ReadEndpoints(inst, RevitHelpers.LowestLevel(inst.Document));
        if (endpoints is null)
            return false;

        state = new ElementState
        {
            Category = Category,
            Id = id,
            Line = endpoints,
            B = RevitHelpers.GetDimension(inst.Symbol, BNames),
            H = RevitHelpers.GetDimension(inst.Symbol, HNames),
        };
        return true;
    }

    private bool IsManaged(FamilyInstance inst) =>
        inst.Symbol.Name.StartsWith(TypePrefix, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(RevitHelpers.Comments(inst));

    private FamilyInstance? FindByKey(Document doc, string id) =>
        ManagedInstances(doc).FirstOrDefault(i => RevitHelpers.Comments(i) == id);

    private IEnumerable<FamilyInstance> ManagedInstances(Document doc) =>
        new FilteredElementCollector(doc).OfCategory(SymbolCategory).OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(i => i.Symbol.Name.StartsWith(TypePrefix, StringComparison.Ordinal));

    private FamilySymbol? ResolveOrCreateSymbol(Document doc, double b, double h)
    {
        string name = TypeName(b, h);

        FamilySymbol? symbol = new FilteredElementCollector(doc).OfCategory(SymbolCategory).OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>().FirstOrDefault(s => s.Name == name);

        if (symbol is null)
        {
            FamilySymbol? baseSymbol = PickBaseSymbol(doc);
            if (baseSymbol is null)
                return null; // no family loaded

            symbol = (FamilySymbol)baseSymbol.Duplicate(name);
            bool setB = RevitHelpers.SetDimension(symbol, b, BNames);
            bool setH = RevitHelpers.SetDimension(symbol, h, HNames);
            if (!setB || !setH)
            {
                Log.Error($"{Category}: family '{baseSymbol.FamilyName}' is missing a b/h parameter "
                          + $"(b set={setB}, h set={setH}). Section may be wrong. "
                          + $"Available numeric params: {RevitHelpers.NumericParamNames(symbol)}");
            }
        }

        if (!symbol.IsActive)
        {
            symbol.Activate();
            doc.Regenerate();
        }
        return symbol;
    }

    private void DeleteSymbolIfUnused(Document doc, ElementId symbolId)
    {
        if (doc.GetElement(symbolId) is not FamilySymbol sym
            || !sym.Name.StartsWith(TypePrefix, StringComparison.Ordinal))
            return;

        bool used = new FilteredElementCollector(doc).OfCategory(SymbolCategory).OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>().Any(i => i.GetTypeId() == symbolId);
        if (!used)
            doc.Delete(symbolId);
    }

    private static bool SameLine(Curve a, Line b) =>
        a.GetEndPoint(0).DistanceTo(b.GetEndPoint(0)) < Tol
        && a.GetEndPoint(1).DistanceTo(b.GetEndPoint(1)) < Tol;
}

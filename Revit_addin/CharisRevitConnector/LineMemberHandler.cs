using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CharisRevitConnector
{
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

        private static readonly string[] BNames = { "b", "Width", "Section Width" };
        private static readonly string[] HNames = { "h", "d", "Height", "Depth", "Section Height", "Section Depth" };

        // ---- Parse (listener thread) ----------------------------------------

        public ElementUpdate ParseElement(string id, IReadOnlyDictionary<string, object> data)
        {
            double b = 0, h = 0;
            IReadOnlyDictionary<string, object> xy = FirestoreParse.Get(data, "xandy") as IReadOnlyDictionary<string, object>;
            if (xy != null)
            {
                b = FirestoreParse.ToDouble(FirestoreParse.Get(xy, "b"));
                h = FirestoreParse.ToDouble(FirestoreParse.Get(xy, "h"));
            }
            var parsedLine = FirestoreParse.ReadLine(data, "line");

            return new ElementUpdate(
                category: Category,
                id: id,
                isDeleted: false,
                polyline: null,
                line: parsedLine == null ? (LineEndpoints?)null : new LineEndpoints(parsedLine.Value.Item1, parsedLine.Value.Item2),
                thickness: 0,
                height: 0,
                b: b,
                h: h,
                material: null);
        }

        // ---- Forward (Firestore -> Revit) ------------------------------------

        public void CreateOrUpdate(Document doc, ElementUpdate u)
        {
            if (u.Line == null)
            {
                Log.Error(string.Format("{0} '{1}' skipped: line needs two distinct points.", Category, u.Id));
                return;
            }

            XYZ start = u.Line.Value.Start;
            XYZ end = u.Line.Value.End;

            if (start.DistanceTo(end) < Tol)
            {
                Log.Error(string.Format("{0} '{1}' skipped: line needs two distinct points.", Category, u.Id));
                return;
            }

            FamilySymbol symbol = ResolveOrCreateSymbol(doc, u.B, u.H);
            if (symbol == null)
            {
                Log.Error(string.Format("{0} '{1}' skipped: no {2} family is loaded to use.", Category, u.Id, SymbolCategory));
                return;
            }

            Level level = RevitHelpers.LowestLevel(doc);
            FamilyInstance existing = FindByKey(doc, u.Id);

            if (existing != null)
            {
                if (existing.GetTypeId() != symbol.Id)
                    existing.ChangeTypeId(symbol.Id);

                UpdateGeometry(existing, start, end, level);
                PreventJoins(existing);
                Log.Info(string.Format("Updated {0} '{1}' -> '{2}'.", FirestoreType, u.Id, symbol.Name));
                return;
            }

            FamilyInstance inst = CreateInstance(doc, start, end, symbol, level);
            RevitHelpers.SetComments(inst, u.Id);
            PreventJoins(inst);
            Log.Info(string.Format("Created {0} '{1}' as '{2}'.", FirestoreType, u.Id, symbol.Name));
        }

        // ---- geometry hooks (beams: line-based; columns override) -----------

        /// <summary>Create the instance from the member line. Default: line-based framing.</summary>
        protected virtual FamilyInstance CreateInstance(Document doc, XYZ start, XYZ end, FamilySymbol symbol, Level level)
        {
            return doc.Create.NewFamilyInstance(Line.CreateBound(start, end), symbol, level, StructuralType);
        }

        /// <summary>Update an existing instance's geometry. Default: reset the location curve.</summary>
        protected virtual void UpdateGeometry(FamilyInstance instance, XYZ start, XYZ end, Level level)
        {
            Line line = Line.CreateBound(start, end);
            LocationCurve lc = instance.Location as LocationCurve;
            if (lc != null && !SameLine(lc.Curve, line))
                lc.Curve = line;
        }

        /// <summary>Read an instance's [start, end] back. Default: from the location curve.</summary>
        protected virtual Tuple<XYZ, XYZ> ReadEndpoints(FamilyInstance instance, Level level)
        {
            LocationCurve lc = instance.Location as LocationCurve;
            if (lc == null)
                return null;
            return Tuple.Create(lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1));
        }

        /// <summary>
        /// Keep streamed members free-standing (no cutback/coping joins to supports).
        /// Default no-op; beams override since join control is framing-specific.
        /// </summary>
        protected virtual void PreventJoins(FamilyInstance instance) { }

        public void Delete(Document doc, string id)
        {
            FamilyInstance inst = FindByKey(doc, id);
            if (inst == null)
                return;

            ElementId symId = inst.GetTypeId();
            doc.Delete(inst.Id);
            DeleteSymbolIfUnused(doc, symId);
            Log.Info(string.Format("Deleted {0} '{1}'.", FirestoreType, id));
        }

        // ---- Reverse (Revit -> Firestore) ------------------------------------

        public IEnumerable<ManagedElement> ReadAll(Document doc)
        {
            foreach (FamilyInstance inst in ManagedInstances(doc))
            {
                ElementState st;
                if (TryRead(inst, out st))
                    yield return new ManagedElement(inst.Id, st);
            }
        }

        public IEnumerable<ManagedElement> ReadAffected(Document doc, IEnumerable<ElementId> changedIds)
        {
            var seen = new HashSet<ElementId>();
            foreach (ElementId id in changedIds)
            {
                Element el = doc.GetElement(id);
                if (el is FamilyInstance)
                {
                    FamilyInstance inst = (FamilyInstance)el;
                    ElementState st;
                    if (IsManaged(inst) && seen.Add(inst.Id) && TryRead(inst, out st))
                        yield return new ManagedElement(inst.Id, st);
                }
                else if (el is FamilySymbol)
                {
                    FamilySymbol sym = (FamilySymbol)el;
                    if (sym.Name.StartsWith(TypePrefix, StringComparison.Ordinal))
                    {
                        foreach (FamilyInstance fi in ManagedInstances(doc).Where(i => i.GetTypeId() == id))
                        {
                            ElementState st;
                            if (seen.Add(fi.Id) && TryRead(fi, out st))
                                yield return new ManagedElement(fi.Id, st);
                        }
                    }
                }
            }
        }

        public Dictionary<string, object> ToFirestore(ElementState state)
        {
            XYZ s = XYZ.Zero;
            XYZ e = XYZ.Zero;
            if (state.Line != null)
            {
                s = state.Line.Value.Start;
                e = state.Line.Value.End;
            }

            return new Dictionary<string, object>
            {
                { "id", state.Id },
                { "type", FirestoreType },
                { "line", new List<object>
                    {
                        new Dictionary<string, object> { { "x", s.X }, { "y", s.Y }, { "z", s.Z } },
                        new Dictionary<string, object> { { "x", e.X }, { "y", e.Y }, { "z", e.Z } },
                    }
                },
                { "xandy", new Dictionary<string, object> { { "b", state.B }, { "h", state.H } } },
            };
        }

        public void LogReadiness(Document doc)
        {
            FamilySymbol baseSymbol = PickBaseSymbol(doc);

            if (baseSymbol == null)
            {
                Log.Error(string.Format("{0}: NO {1} family is loaded — {2}s cannot be created.", Category, SymbolCategory, FirestoreType));
                return;
            }

            bool hasB = RevitHelpers.HasDimension(baseSymbol, BNames);
            bool hasH = RevitHelpers.HasDimension(baseSymbol, HNames);
            string verdict = hasB && hasH ? "READY (b + h found)" : "NOT READY — missing b/h";
            Log.Info(string.Format("{0} readiness: {1}. Family '{2}', symbol '{3}'. b found={4}, h found={5}. Writable numeric params: {6}",
                Category, verdict, baseSymbol.FamilyName, baseSymbol.Name, hasB, hasH, RevitHelpers.NumericParamNames(baseSymbol)));
        }

        // ---- internals ------------------------------------------------------

        private bool TryRead(FamilyInstance inst, out ElementState state)
        {
            state = null;
            if (!IsManaged(inst))
                return false;

            string id = RevitHelpers.Comments(inst);
            if (string.IsNullOrEmpty(id))
                return false;

            Tuple<XYZ, XYZ> endpoints = ReadEndpoints(inst, RevitHelpers.LowestLevel(inst.Document));
            if (endpoints == null)
            {
                return false;
            }

            var start = endpoints.Item1;
            var end = endpoints.Item2;
            var parsedLine = Line.CreateBound(start, end);

            state = new ElementState(
                category: Category,
                id: id,
                polyline: null,
                line: new LineEndpoints(start, end),
                thickness: 0,
                height: 0,
                b: RevitHelpers.GetDimension(inst.Symbol, BNames),
                h: RevitHelpers.GetDimension(inst.Symbol, HNames),
                material: null);
            return true;
        }

        private bool IsManaged(FamilyInstance inst)
        {
            return inst.Symbol.Name.StartsWith(TypePrefix, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(RevitHelpers.Comments(inst));
        }

        private FamilyInstance FindByKey(Document doc, string id)
        {
            return ManagedInstances(doc).FirstOrDefault(i => RevitHelpers.Comments(i) == id);
        }

        private IEnumerable<FamilyInstance> ManagedInstances(Document doc)
        {
            return new FilteredElementCollector(doc).OfCategory(SymbolCategory).OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(i => i.Symbol.Name.StartsWith(TypePrefix, StringComparison.Ordinal));
        }

        private FamilySymbol PickBaseSymbol(Document doc)
        {
            List<FamilySymbol> symbols = new FilteredElementCollector(doc)
                .OfCategory(SymbolCategory).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();

            return symbols.FirstOrDefault(s => RevitHelpers.HasDimension(s, "b") && RevitHelpers.HasDimension(s, "h"))
                ?? symbols.FirstOrDefault(s => RevitHelpers.HasDimension(s, "Width") && RevitHelpers.HasDimension(s, "Height"))
                ?? symbols.FirstOrDefault();
        }

        private FamilySymbol ResolveOrCreateSymbol(Document doc, double b, double h)
        {
            string name = TypeName(b, h);

            FamilySymbol symbol = new FilteredElementCollector(doc).OfCategory(SymbolCategory).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>().FirstOrDefault(s => s.Name == name);

            if (symbol == null)
            {
                FamilySymbol baseSymbol = PickBaseSymbol(doc);
                if (baseSymbol == null)
                    return null;

                symbol = (FamilySymbol)baseSymbol.Duplicate(name);
                bool setB = RevitHelpers.SetDimension(symbol, b, BNames);
                bool setH = RevitHelpers.SetDimension(symbol, h, HNames);
                if (!setB || !setH)
                {
                    Log.Error(string.Format("{0}: family '{1}' is missing a b/h parameter (b set={2}, h set={3}). " +
                        "Section may be wrong. Available numeric params: {4}",
                        Category, baseSymbol.FamilyName, setB, setH, RevitHelpers.NumericParamNames(symbol)));
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
            FamilySymbol sym = doc.GetElement(symbolId) as FamilySymbol;
            if (sym == null || !sym.Name.StartsWith(TypePrefix, StringComparison.Ordinal))
                return;

            bool used = new FilteredElementCollector(doc).OfCategory(SymbolCategory).OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>().Any(i => i.GetTypeId() == symbolId);
            if (!used)
                doc.Delete(symbolId);
        }

        private static bool SameLine(Curve a, Line b)
        {
            return a.GetEndPoint(0).DistanceTo(b.GetEndPoint(0)) < Tol
                && a.GetEndPoint(1).DistanceTo(b.GetEndPoint(1)) < Tol;
        }
    }
}
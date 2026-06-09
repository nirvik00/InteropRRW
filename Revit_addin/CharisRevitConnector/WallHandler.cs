using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CharisRevitConnector
{
    internal sealed class WallHandler : IFamilyHandler
    {
        private const double MinThicknessFeet = 1.0 / 32.0;
        private const double Tol = 1.0e-4;

        public ElementCategory Category => ElementCategory.Wall;
        public string ArrayKey => "walls";

        // ---- Parse (listener thread) ----------------------------------------

        public ElementUpdate ParseElement(string id, IReadOnlyDictionary<string, object> data)
        {
            return new ElementUpdate(
                category: Category,
                id: id,
                isDeleted: false,
                polyline: FirestoreParse.ReadPolyline(data, "polyline"),
                line: null,
                thickness: FirestoreParse.ToDouble(FirestoreParse.Get(data, "thickness")),
                height: FirestoreParse.ToDouble(FirestoreParse.Get(data, "height")),
                b: 0,
                h: 0,
                material: FirestoreParse.Get(data, "material") as string);
        }

        // ---- Forward (Firestore -> Revit) ------------------------------------

        public void CreateOrUpdate(Document doc, ElementUpdate u)
        {
            if (u.Polyline == null || u.Polyline.Count < 2)
            {
                Log.Error(string.Format("Wall '{0}' skipped: polyline needs >= 2 points.", u.Id));
                return;
            }
            if (u.Height <= Tol)
            {
                Log.Error(string.Format("Wall '{0}' skipped: height must be > 0.", u.Id));
                return;
            }

            double thickness = Math.Max(u.Thickness, MinThicknessFeet);
            ElementId typeId = ResolveOrCreateType(doc, thickness, u.Material);
            List<Wall> existing = FindAllByKey(doc, u.Id);

            if (existing.Count > 0 && SegmentsMatch(existing, u.Polyline, typeId, u.Height))
                return;

            foreach (Wall w in existing)
                doc.Delete(w.Id);

            Level level = LowestLevel(doc);
            int segments = 0;
            for (int i = 0; i < u.Polyline.Count - 1; i++)
            {
                XYZ a = u.Polyline[i];
                XYZ b = u.Polyline[i + 1];
                if (a.DistanceTo(b) < Tol)
                    continue;

                Line baseLine = Line.CreateBound(new XYZ(a.X, a.Y, 0.0), new XYZ(b.X, b.Y, 0.0));
                double offset = a.Z - level.Elevation;
                Wall wall = Wall.Create(doc, baseLine, typeId, level.Id, u.Height, offset, false, false);
                IdTag.Set(wall, u.Id);

                WallUtils.DisallowWallJoinAtEnd(wall, 0);
                WallUtils.DisallowWallJoinAtEnd(wall, 1);

                segments++;
            }

            Log.Info(string.Format("{0} wall '{1}' ({2} seg) as '{3}', height {4} ft.",
                existing.Count > 0 ? "Rebuilt" : "Created",
                u.Id,
                segments,
                TypeNameOf(doc, typeId),
                Naming.Num(u.Height)));
        }

        public void Delete(Document doc, string id)
        {
            List<Wall> walls = FindAllByKey(doc, id);
            if (walls.Count == 0)
                return;

            List<ElementId> typeIds = walls.Select(w => w.GetTypeId()).Distinct().ToList();
            foreach (Wall w in walls)
                doc.Delete(w.Id);

            foreach (ElementId typeId in typeIds)
                DeleteTypeIfUnused(doc, typeId);

            Log.Info(string.Format("Deleted wall '{0}'.", id));
        }

        // ---- Reverse (Revit -> Firestore) ------------------------------------

        public IEnumerable<ManagedElement> ReadAll(Document doc)
        {
            foreach (Wall wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
            {
                ElementState st;
                if (TryRead(doc, wall, out st))
                    yield return new ManagedElement(wall.Id, st);
            }
        }

        public IEnumerable<ManagedElement> ReadAffected(Document doc, IEnumerable<ElementId> changedIds)
        {
            var seen = new HashSet<ElementId>();

            foreach (ElementId id in changedIds)
            {
                Element el = doc.GetElement(id);
                if (el is Wall)
                {
                    Wall wall = (Wall)el;
                    ElementState st;
                    if (seen.Add(wall.Id) && TryRead(doc, wall, out st))
                        yield return new ManagedElement(wall.Id, st);
                }
                else if (el is WallType)
                {
                    WallType wt = (WallType)el;
                    if (!wt.Name.StartsWith(Naming.WallPrefix, StringComparison.Ordinal))
                        continue;

                    foreach (Wall inst in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                        .Where(w => w.GetTypeId() == id))
                    {
                        ElementState st;
                        if (seen.Add(inst.Id) && TryRead(doc, inst, out st))
                            yield return new ManagedElement(inst.Id, st);
                    }
                }
            }
        }

        public Dictionary<string, object> ToFirestore(ElementState state)
        {
            var poly = new List<object>();
            foreach (XYZ p in state.Polyline ?? new List<XYZ>())
            {
                poly.Add(new Dictionary<string, object>
                {
                    { "x", p.X },
                    { "y", p.Y },
                    { "z", p.Z }
                });
            }

            return new Dictionary<string, object>
            {
                { "id", state.Id },
                { "type", "wall" },
                { "polyline", poly },
                { "thickness", state.Thickness },
                { "height", state.Height },
                { "material", state.Material ?? string.Empty },
            };
        }

        public void LogReadiness(Document doc) { }

        // ---- Read internals --------------------------------------------------

        private bool TryRead(Document doc, Wall wall, out ElementState state)
        {
            state = null;

            WallType wt = doc.GetElement(wall.GetTypeId()) as WallType;
            if (wt == null || !wt.Name.StartsWith(Naming.WallPrefix, StringComparison.Ordinal))
                return false;

            string id = IdTag.Get(wall);
            if (string.IsNullOrEmpty(id))
                return false;

            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            double height = heightParam != null ? heightParam.AsDouble() : 0.0;

            state = new ElementState(
                category: Category,
                id: id,
                polyline: ChainCenterline(new List<Wall> { wall }),
                line: null,
                thickness: wt.Width,
                height: height,
                b: 0,
                h: 0,
                material: RevitHelpers.MaterialNameOfLayer(doc, wt.GetCompoundStructure()));
            return true;
        }

        // ---- Type resolution ------------------------------------------------

        private static ElementId ResolveOrCreateType(Document doc, double thicknessFeet, string material)
        {
            string typeName = Naming.WallTypeName(thicknessFeet, material);

            WallType existing = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                .FirstOrDefault(t => t.Name == typeName);
            if (existing != null)
                return existing.Id;

            WallType baseType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                .FirstOrDefault(t => t.Kind == WallKind.Basic);
            if (baseType == null)
                throw new InvalidOperationException("No basic WallType exists to duplicate.");

            WallType newType = (WallType)baseType.Duplicate(typeName);
            CompoundStructure cs = CompoundStructure.CreateSingleLayerCompoundStructure(
                MaterialFunctionAssignment.Structure, thicknessFeet, RevitHelpers.GetOrCreateMaterial(doc, material));
            newType.SetCompoundStructure(cs);
            return newType.Id;
        }

        private static void DeleteTypeIfUnused(Document doc, ElementId typeId)
        {
            WallType wt = doc.GetElement(typeId) as WallType;
            if (wt == null || !wt.Name.StartsWith(Naming.WallPrefix, StringComparison.Ordinal))
                return;

            bool used = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Any(w => w.GetTypeId() == typeId);
            if (!used)
                doc.Delete(typeId);
        }

        // ---- Helpers --------------------------------------------------------

        private static string TypeNameOf(Document doc, ElementId typeId)
        {
            Element el = doc.GetElement(typeId);
            return el != null ? el.Name : "(type)";
        }

        private static List<Wall> FindAllByKey(Document doc, string id)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Where(w =>
                {
                    WallType wt = doc.GetElement(w.GetTypeId()) as WallType;
                    return IdTag.Get(w) == id
                        && wt != null
                        && wt.Name.StartsWith(Naming.WallPrefix, StringComparison.Ordinal);
                })
                .ToList();
        }

        private static bool SegmentsMatch(List<Wall> existing, IReadOnlyList<XYZ> polyline, ElementId typeId, double height)
        {
            var desired = new List<Tuple<XYZ, XYZ>>();
            for (int i = 0; i < polyline.Count - 1; i++)
                if (polyline[i].DistanceTo(polyline[i + 1]) >= Tol)
                    desired.Add(Tuple.Create(polyline[i], polyline[i + 1]));

            if (existing.Count != desired.Count)
                return false;

            foreach (Wall w in existing)
            {
                if (w.GetTypeId() != typeId)
                    return false;

                Parameter heightParam = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                double wallHeight = heightParam != null ? heightParam.AsDouble() : 0.0;
                if (Math.Abs(wallHeight - height) > Tol)
                    return false;

                LocationCurve lc = w.Location as LocationCurve;
                if (lc == null)
                    return false;

                XYZ s = lc.Curve.GetEndPoint(0);
                XYZ e = lc.Curve.GetEndPoint(1);
                bool found = desired.Any(d => SamePlanar(d.Item1, s) && SamePlanar(d.Item2, e));
                if (!found)
                    return false;
            }
            return true;
        }

        private static bool SamePlanar(XYZ a, XYZ b)
        {
            return Math.Abs(a.X - b.X) < Tol && Math.Abs(a.Y - b.Y) < Tol;
        }

        private static List<XYZ> ChainCenterline(List<Wall> walls)
        {
            var segs = new List<Tuple<XYZ, XYZ>>();
            foreach (Wall w in walls)
            {
                LocationCurve lc = w.Location as LocationCurve;
                if (lc == null)
                    continue;

                double z = 0.0;
                Level lv = w.Document.GetElement(w.LevelId) as Level;
                if (lv != null)
                    z = lv.Elevation;

                Parameter baseOffset = w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (baseOffset != null)
                    z += baseOffset.AsDouble();

                XYZ a = lc.Curve.GetEndPoint(0);
                XYZ b = lc.Curve.GetEndPoint(1);
                segs.Add(Tuple.Create(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)));
            }

            if (segs.Count == 0)
                return new List<XYZ>();

            var used = new bool[segs.Count];
            var pts = new List<XYZ> { segs[0].Item1, segs[0].Item2 };
            used[0] = true;
            bool extended = true;
            while (extended)
            {
                extended = false;
                for (int i = 0; i < segs.Count; i++)
                {
                    if (used[i]) continue;
                    XYZ last = pts[pts.Count - 1];
                    if (segs[i].Item1.DistanceTo(last) < Tol)
                    {
                        pts.Add(segs[i].Item2);
                        used[i] = true;
                        extended = true;
                    }
                    else if (segs[i].Item2.DistanceTo(last) < Tol)
                    {
                        pts.Add(segs[i].Item1);
                        used[i] = true;
                        extended = true;
                    }
                }
            }
            return pts;
        }

        private static Level LowestLevel(Document doc)
        {
            Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();
            if (level == null)
                throw new InvalidOperationException("The model has no Level to host the wall.");
            return level;
        }
    }
}
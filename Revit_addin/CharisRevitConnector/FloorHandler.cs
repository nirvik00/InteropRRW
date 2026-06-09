using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CharisRevitConnector
{
    internal sealed class FloorHandler : IFamilyHandler
    {
        private const double MinThicknessFeet = 1.0 / 32.0;
        private const double Tol = 1.0e-4;

        public ElementCategory Category => ElementCategory.Floor;
        public string ArrayKey => "floors";

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
                height: 0,
                b: 0,
                h: 0,
                material: FirestoreParse.Get(data, "material") as string);
        }

        // ---- Forward (Firestore -> Revit) -----------------------------------

        public void CreateOrUpdate(Document doc, ElementUpdate u)
        {
            if (u.Polyline == null || u.Polyline.Count < 3)
            {
                Log.Error(string.Format("Floor '{0}' skipped: polyline needs >= 3 points.", u.Id));
                return;
            }

            double thickness = u.Thickness < MinThicknessFeet ? MinThicknessFeet : u.Thickness;
            ElementId typeId = ResolveOrCreateType(doc, thickness, u.Material);
            Floor existing = FindById(doc, u.Id);

            if (existing != null)
            {
                if (!OutlineEquals(ReadOutline(doc, existing), u.Polyline))
                {
                    doc.Delete(existing.Id);
                    CreateFloor(doc, u, typeId);
                    Log.Info(string.Format("Re-shaped floor '{0}' ({1} pts) as '{2}'.", u.Id, u.Polyline.Count, TypeNameOf(doc, typeId)));
                }
                else if (existing.GetTypeId() != typeId)
                {
                    existing.ChangeTypeId(typeId);
                    Log.Info(string.Format("Re-typed floor '{0}' to '{1}'.", u.Id, TypeNameOf(doc, typeId)));
                }
                return;
            }

            CreateFloor(doc, u, typeId);
            Log.Info(string.Format("Created floor '{0}' ({1} pts) as '{2}'.", u.Id, u.Polyline.Count, TypeNameOf(doc, typeId)));
        }

        private static void CreateFloor(Document doc, ElementUpdate u, ElementId typeId)
        {
            Level level = LowestLevel(doc);
            double elevation = u.Polyline[0].Z;

            CurveLoop loop = PolylineLoop(u.Polyline, level.Elevation);
            Floor floor = Floor.Create(doc, new List<CurveLoop> { loop }, typeId, level.Id);

            IdTag.Set(floor, u.Id);
            Parameter param = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
            if (param != null)
                param.Set(elevation - level.Elevation);
        }

        public void Delete(Document doc, string id)
        {
            Floor floor = FindById(doc, id);
            if (floor == null)
                return;

            ElementId typeId = floor.GetTypeId();
            doc.Delete(floor.Id);
            DeleteTypeIfUnused(doc, typeId);
            Log.Info(string.Format("Deleted floor '{0}'.", id));
        }

        // ---- Reverse (Revit -> Firestore) -----------------------------------

        public IEnumerable<ManagedElement> ReadAll(Document doc)
        {
            foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                ElementState st;
                if (TryRead(doc, floor, out st))
                    yield return new ManagedElement(floor.Id, st);
            }
        }

        public IEnumerable<ManagedElement> ReadAffected(Document doc, IEnumerable<ElementId> changedIds)
        {
            var seen = new HashSet<ElementId>();

            foreach (ElementId id in changedIds)
            {
                Element el = doc.GetElement(id);
                if (el is Floor)
                {
                    Floor floor = (Floor)el;
                    ElementState st;
                    if (seen.Add(floor.Id) && TryRead(doc, floor, out st))
                        yield return new ManagedElement(floor.Id, st);
                }
                else if (el is FloorType)
                {
                    FloorType ft = (FloorType)el;
                    if (!ft.Name.StartsWith(Naming.FloorPrefix, StringComparison.Ordinal))
                        continue;

                    foreach (Floor inst in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
                        .Where(f => f.GetTypeId() == id))
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
                    ["x"] = p.X,
                    ["y"] = p.Y,
                    ["z"] = p.Z
                });
            }

            return new Dictionary<string, object>
            {
                ["id"] = state.Id,
                ["type"] = "floor",
                ["polyline"] = poly,
                ["thickness"] = state.Thickness,
                ["material"] = state.Material ?? string.Empty,
            };
        }

        public void LogReadiness(Document doc) { }

        private bool TryRead(Document doc, Floor floor, out ElementState state)
        {
            state = null;

            FloorType ft = doc.GetElement(floor.GetTypeId()) as FloorType;
            if (ft == null || !ft.Name.StartsWith(Naming.FloorPrefix, StringComparison.Ordinal))
                return false;

            string id = IdTag.Get(floor);
            if (string.IsNullOrEmpty(id))
                return false;

            CompoundStructure cs = ft.GetCompoundStructure();
            state = new ElementState(
                category: Category,
                id: id,
                polyline: ReadOutline(doc, floor),
                line: null,
                thickness: cs != null ? cs.GetWidth() : 0.0,
                height: 0,
                b: 0,
                h: 0,
                material: ReadMaterial(doc, ft));
            return true;
        }

        // ---- Type resolution ------------------------------------------------

        private static ElementId ResolveOrCreateType(Document doc, double thicknessFeet, string material)
        {
            string typeName = Naming.FloorTypeName(thicknessFeet, material);

            FloorType existing = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                .FirstOrDefault(ft => ft.Name == typeName);
            if (existing != null)
                return existing.Id;

            FloorType baseType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                .FirstOrDefault();
            if (baseType == null)
                throw new InvalidOperationException("No FloorType exists in the document to duplicate.");

            var newType = (FloorType)baseType.Duplicate(typeName);
            SetStructure(newType, thicknessFeet, RevitHelpers.GetOrCreateMaterial(doc, material));
            return newType.Id;
        }

        private static void SetStructure(FloorType floorType, double thicknessFeet, ElementId materialId)
        {
            CompoundStructure cs = CompoundStructure.CreateSingleLayerCompoundStructure(
                MaterialFunctionAssignment.Structure, thicknessFeet, materialId);

            CompoundStructure current = floorType.GetCompoundStructure();
            if (current != null)
                cs.EndCap = current.EndCap;

            floorType.SetCompoundStructure(cs);
        }

        private static void DeleteTypeIfUnused(Document doc, ElementId typeId)
        {
            FloorType ft = doc.GetElement(typeId) as FloorType;
            if (ft == null || !ft.Name.StartsWith(Naming.FloorPrefix, StringComparison.Ordinal))
                return;

            bool used = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
                .Any(f => f.GetTypeId() == typeId);
            if (!used)
                doc.Delete(typeId);
        }

        // ---- Helpers --------------------------------------------------------

        private static string TypeNameOf(Document doc, ElementId typeId)
        {
            Element el = doc.GetElement(typeId);
            return el != null ? el.Name : "(type)";
        }

        private static Floor FindById(Document doc, string id)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
                .FirstOrDefault(f => IdTag.Get(f) == id);
        }

        private static CurveLoop PolylineLoop(IReadOnlyList<XYZ> pts, double planeZ)
        {
            var p = pts.Select(q => new XYZ(q.X, q.Y, planeZ)).ToList();
            if (p.Count >= 2 && p[0].DistanceTo(p[p.Count - 1]) < Tol)
                p.RemoveAt(p.Count - 1);

            if (p.Count < 3)
                throw new InvalidOperationException("Floor polyline needs >= 3 distinct points.");

            var loop = new CurveLoop();
            for (int i = 0; i < p.Count; i++)
                loop.Append(Line.CreateBound(p[i], p[(i + 1) % p.Count]));
            return loop;
        }

        private static List<XYZ> ReadOutline(Document doc, Floor floor)
        {
            var pts = new List<XYZ>();

            var filter = new ElementClassFilter(typeof(Sketch));
            ElementId sketchId = floor.GetDependentElements(filter).FirstOrDefault()
                ?? ElementId.InvalidElementId;

            Sketch sketch = doc.GetElement(sketchId) as Sketch;
            if (sketch != null)
            {
                try
                {
                    foreach (CurveArray arr in sketch.Profile)
                    {
                        foreach (Curve c in arr)
                            pts.Add(c.GetEndPoint(0));
                        break;
                    }
                    if (pts.Count > 0)
                        pts.Add(pts[0]);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
            }

            return pts;
        }

        private static bool OutlineEquals(IReadOnlyList<XYZ> a, IReadOnlyList<XYZ> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].DistanceTo(b[i]) > Tol)
                    return false;
            return true;
        }

        private static string ReadMaterial(Document doc, FloorType ft)
        {
            CompoundStructure cs = ft.GetCompoundStructure();
            if (cs == null)
                return null;

            IList<CompoundStructureLayer> layers = cs.GetLayers();
            if (layers == null || layers.Count == 0)
                return null;

            if (layers[0].MaterialId == ElementId.InvalidElementId)
                return null;

            Material m = doc.GetElement(layers[0].MaterialId) as Material;
            return m != null ? m.Name : null;
        }

        private static Level LowestLevel(Document doc)
        {
            Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();
            if (level == null)
                throw new InvalidOperationException("The model has no Level to host the floor.");
            return level;
        }
    }
}
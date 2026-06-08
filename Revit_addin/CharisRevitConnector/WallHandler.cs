using Autodesk.Revit.DB;

namespace CharisRevitConnector;

/// <summary>
/// Wall family. Schema: { polyline: Vec3[] (centerline), thickness, height, material }.
/// Each polyline segment becomes one Wall, all tagged with the document key in
/// Comments. Types are shared by thickness (+ material): "Test - Wall - 0.5".
/// Updates rebuild the segment set; no-op when nothing changed.
/// </summary>
internal sealed class WallHandler : IFamilyHandler
{
    private const double MinThicknessFeet = 1.0 / 32.0;
    private const double Tol = 1.0e-4;

    public ElementCategory Category => ElementCategory.Wall;
    public string ArrayKey => "walls";

    public ElementUpdate ParseElement(string id, IReadOnlyDictionary<string, object> data) =>
        new()
        {
            Category = Category,
            Id = id,
            Polyline = FirestoreParse.ReadPolyline(data, "polyline"),
            Thickness = FirestoreParse.ToDouble(FirestoreParse.Get(data, "thickness")),
            Height = FirestoreParse.ToDouble(FirestoreParse.Get(data, "height")),
            Material = FirestoreParse.Get(data, "material") as string,
        };

    public void CreateOrUpdate(Document doc, ElementUpdate u)
    {
        if (u.Polyline is null || u.Polyline.Count < 2)
        {
            Log.Error($"Wall '{u.Id}' skipped: polyline needs >= 2 points.");
            return;
        }
        if (u.Height <= Tol)
        {
            Log.Error($"Wall '{u.Id}' skipped: height must be > 0.");
            return;
        }

        double thickness = Math.Max(u.Thickness, MinThicknessFeet);
        ElementId typeId = ResolveOrCreateType(doc, thickness, u.Material);
        List<Wall> existing = FindAllByKey(doc, u.Id);

        if (existing.Count > 0 && SegmentsMatch(existing, u.Polyline, typeId, u.Height))
            return; // nothing changed

        foreach (Wall w in existing)
            doc.Delete(w.Id);

        Level level = RevitHelpers.LowestLevel(doc);
        int segments = 0;
        for (int i = 0; i < u.Polyline.Count - 1; i++)
        {
            XYZ a = u.Polyline[i];
            XYZ b = u.Polyline[i + 1];
            if (a.DistanceTo(b) < Tol)
                continue;

            // Location line in the XY plane; base offset lifts it to the point's Z.
            Line baseLine = Line.CreateBound(new XYZ(a.X, a.Y, 0.0), new XYZ(b.X, b.Y, 0.0));
            double offset = a.Z - level.Elevation;
            Wall wall = Wall.Create(doc, baseLine, typeId, level.Id, u.Height, offset, false, false);
            RevitHelpers.SetComments(wall, u.Id);

            // Keep streamed walls free-standing: no end joins with adjacent walls.
            WallUtils.DisallowWallJoinAtEnd(wall, 0);
            WallUtils.DisallowWallJoinAtEnd(wall, 1);

            segments++;
        }

        Log.Info($"{(existing.Count > 0 ? "Rebuilt" : "Created")} wall '{u.Id}' "
                 + $"({segments} seg) as '{NameOf(doc, typeId)}', height {Naming.Num(u.Height)} ft.");
    }

    public void Delete(Document doc, string id)
    {
        List<Wall> walls = FindAllByKey(doc, id);
        if (walls.Count == 0)
            return;

        var typeIds = walls.Select(w => w.GetTypeId()).Distinct().ToList();
        foreach (Wall w in walls)
            doc.Delete(w.Id);

        foreach (ElementId typeId in typeIds)
            DeleteTypeIfUnused(doc, typeId);

        Log.Info($"Deleted wall '{id}'.");
    }

    public IEnumerable<ManagedElement> ReadAll(Document doc)
    {
        foreach (IGrouping<string, Wall> group in ManagedWalls(doc).GroupBy(w => RevitHelpers.Comments(w)!))
        {
            if (TryReadGroup(doc, group.ToList(), group.Key, out ManagedElement me))
                yield return me;
        }
    }

    public IEnumerable<ManagedElement> ReadAffected(Document doc, IEnumerable<ElementId> changedIds)
    {
        // Map any changed wall (or Test wall type) to its document key, then read
        // that key's full segment set once.
        var keys = new HashSet<string>();
        foreach (ElementId id in changedIds)
        {
            Element? el = doc.GetElement(id);
            if (el is Wall w && IsManaged(doc, w) && RevitHelpers.Comments(w) is { Length: > 0 } k1)
                keys.Add(k1);
            else if (el is WallType wt && wt.Name.StartsWith(Naming.WallPrefix, StringComparison.Ordinal))
            {
                foreach (Wall mw in ManagedWalls(doc).Where(x => x.GetTypeId() == id))
                    if (RevitHelpers.Comments(mw) is { Length: > 0 } k2)
                        keys.Add(k2);
            }
        }

        foreach (string key in keys)
        {
            List<Wall> walls = FindAllByKey(doc, key);
            if (TryReadGroup(doc, walls, key, out ManagedElement me))
                yield return me;
        }
    }

    public Dictionary<string, object> ToFirestore(ElementState state)
    {
        var poly = (state.Polyline ?? new List<XYZ>())
            .Select(p => (object)new Dictionary<string, object> { ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z })
            .ToList();

        return new Dictionary<string, object>
        {
            ["id"] = state.Id,
            ["type"] = "wall",
            ["polyline"] = poly,
            ["thickness"] = state.Thickness,
            ["height"] = state.Height,
            ["material"] = state.Material ?? string.Empty,
        };
    }

    // ---- internals ------------------------------------------------------

    private bool TryReadGroup(Document doc, List<Wall> walls, string key, out ManagedElement me)
    {
        me = default;
        if (walls.Count == 0)
            return false;

        Wall first = walls[0];
        var type = doc.GetElement(first.GetTypeId()) as WallType;

        var state = new ElementState
        {
            Category = Category,
            Id = key,
            Polyline = ChainCenterline(walls),
            Thickness = type?.Width ?? 0.0,
            Height = first.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0.0,
            Material = RevitHelpers.MaterialNameOfLayer(doc, type?.GetCompoundStructure()),
        };
        me = new ManagedElement(first.Id, state);
        return true;
    }

    private static List<Wall> ManagedWalls(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
            .Where(w => IsManaged(doc, w)).ToList();

    private static bool IsManaged(Document doc, Wall w) =>
        doc.GetElement(w.GetTypeId()) is WallType wt
        && wt.Name.StartsWith(Naming.WallPrefix, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(RevitHelpers.Comments(w));

    private static List<Wall> FindAllByKey(Document doc, string id) =>
        new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
            .Where(w => RevitHelpers.Comments(w) == id
                        && doc.GetElement(w.GetTypeId()) is WallType wt
                        && wt.Name.StartsWith(Naming.WallPrefix, StringComparison.Ordinal))
            .ToList();

    private static bool SegmentsMatch(List<Wall> existing, IReadOnlyList<XYZ> polyline, ElementId typeId, double height)
    {
        var desired = new List<(XYZ A, XYZ B)>();
        for (int i = 0; i < polyline.Count - 1; i++)
            if (polyline[i].DistanceTo(polyline[i + 1]) >= Tol)
                desired.Add((polyline[i], polyline[i + 1]));

        if (existing.Count != desired.Count)
            return false;

        foreach (Wall w in existing)
        {
            if (w.GetTypeId() != typeId)
                return false;
            if (Math.Abs((w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0.0) - height) > Tol)
                return false;
            if (w.Location is not LocationCurve lc)
                return false;

            XYZ s = lc.Curve.GetEndPoint(0);
            XYZ e = lc.Curve.GetEndPoint(1);
            bool found = desired.Any(d => SamePlanar(d.A, s) && SamePlanar(d.B, e));
            if (!found)
                return false;
        }
        return true;
    }

    private static bool SamePlanar(XYZ a, XYZ b) =>
        Math.Abs(a.X - b.X) < Tol && Math.Abs(a.Y - b.Y) < Tol;

    private static List<XYZ> ChainCenterline(List<Wall> walls)
    {
        // Lift each segment back to its base elevation (level + base offset).
        var segs = new List<(XYZ A, XYZ B)>();
        foreach (Wall w in walls)
        {
            if (w.Location is not LocationCurve lc)
                continue;
            double z = (w.Document.GetElement(w.LevelId) as Level)?.Elevation ?? 0.0;
            z += w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0.0;
            XYZ a = lc.Curve.GetEndPoint(0);
            XYZ b = lc.Curve.GetEndPoint(1);
            segs.Add((new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)));
        }
        if (segs.Count == 0)
            return new List<XYZ>();

        // Chain segments end-to-start into an ordered polyline.
        var used = new bool[segs.Count];
        var pts = new List<XYZ> { segs[0].A, segs[0].B };
        used[0] = true;
        bool extended = true;
        while (extended)
        {
            extended = false;
            for (int i = 0; i < segs.Count; i++)
            {
                if (used[i]) continue;
                if (segs[i].A.DistanceTo(pts[^1]) < Tol) { pts.Add(segs[i].B); used[i] = true; extended = true; }
                else if (segs[i].B.DistanceTo(pts[^1]) < Tol) { pts.Add(segs[i].A); used[i] = true; extended = true; }
            }
        }
        return pts;
    }

    private static ElementId ResolveOrCreateType(Document doc, double thicknessFeet, string? material)
    {
        string typeName = Naming.WallTypeName(thicknessFeet, material);

        WallType? existing = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
            .FirstOrDefault(t => t.Name == typeName);
        if (existing is not null)
            return existing.Id;

        WallType baseType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
            .FirstOrDefault(t => t.Kind == WallKind.Basic)
            ?? throw new InvalidOperationException("No basic WallType exists to duplicate.");

        var newType = (WallType)baseType.Duplicate(typeName);
        CompoundStructure cs = CompoundStructure.CreateSingleLayerCompoundStructure(
            MaterialFunctionAssignment.Structure, thicknessFeet, RevitHelpers.GetOrCreateMaterial(doc, material));
        newType.SetCompoundStructure(cs);
        return newType.Id;
    }

    private static void DeleteTypeIfUnused(Document doc, ElementId typeId)
    {
        if (doc.GetElement(typeId) is not WallType wt
            || !wt.Name.StartsWith(Naming.WallPrefix, StringComparison.Ordinal))
            return;

        bool used = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
            .Any(w => w.GetTypeId() == typeId);
        if (!used)
            doc.Delete(typeId);
    }

    private static string NameOf(Document doc, ElementId typeId) => doc.GetElement(typeId)?.Name ?? "(type)";
}

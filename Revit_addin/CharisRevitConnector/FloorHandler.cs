using Autodesk.Revit.DB;

namespace CharisRevitConnector;

/// <summary>
/// Floor family. Schema: { polyline: Vec3[] (closed outline), thickness, material }.
///
/// Types are shared and named by thickness (+ material) with the "Test" prefix,
/// e.g. "Test - Floor - 8" / "Test - Floor - 8 - concrete", created on demand.
/// Changing thickness/material switches the floor to the matching type (creating
/// it if needed) rather than mutating a shared type. The Firestore document key
/// is stored in the floor's Comments.
/// </summary>
internal sealed class FloorHandler : IFamilyHandler
{
    private const double MinThicknessFeet = 1.0 / 32.0;
    private const double Tol = 1.0e-4;

    public ElementCategory Category => ElementCategory.Floor;
    public string ArrayKey => "floors";

    // ---- Parse (listener thread) ----------------------------------------

    public ElementUpdate ParseElement(string id, IReadOnlyDictionary<string, object> data) =>
        new()
        {
            Category = Category,
            Id = id,
            Polyline = FirestoreParse.ReadPolyline(data, "polyline"),
            Thickness = FirestoreParse.ToDouble(FirestoreParse.Get(data, "thickness")),
            Material = FirestoreParse.Get(data, "material") as string,
        };

    // ---- Forward (Firestore -> Revit) -----------------------------------

    public void CreateOrUpdate(Document doc, ElementUpdate u)
    {
        if (u.Polyline is null || u.Polyline.Count < 3)
        {
            Log.Error($"Floor '{u.Id}' skipped: polyline needs >= 3 points.");
            return;
        }

        double thickness = u.Thickness < MinThicknessFeet ? MinThicknessFeet : u.Thickness;
        ElementId typeId = ResolveOrCreateType(doc, thickness, u.Material);
        Floor? existing = FindById(doc, u.Id);

        if (existing is not null)
        {
            // Changing the outline means re-sketching: recreate with the resolved type.
            if (!OutlineEquals(ReadOutline(doc, existing), u.Polyline))
            {
                doc.Delete(existing.Id);
                CreateFloor(doc, u, typeId);
                Log.Info($"Re-shaped floor '{u.Id}' ({u.Polyline.Count} pts) as '{TypeNameOf(doc, typeId)}'.");
            }
            else if (existing.GetTypeId() != typeId)
            {
                // Thickness/material changed: switch to the matching shared type.
                existing.ChangeTypeId(typeId);
                Log.Info($"Re-typed floor '{u.Id}' to '{TypeNameOf(doc, typeId)}'.");
            }
            return;
        }

        CreateFloor(doc, u, typeId);
        Log.Info($"Created floor '{u.Id}' ({u.Polyline.Count} pts) as '{TypeNameOf(doc, typeId)}'.");
    }

    /// <summary>
    /// Creates the floor with its sketch flattened to the level plane, then maps
    /// the polyline's z to the Height Offset From Level parameter (Floor.Create
    /// ignores the loop's z, so without this every floor lands on the level).
    /// </summary>
    private static void CreateFloor(Document doc, ElementUpdate u, ElementId typeId)
    {
        Level level = LowestLevel(doc);
        double elevation = u.Polyline![0].Z;

        CurveLoop loop = PolylineLoop(u.Polyline, level.Elevation);
        Floor floor = Floor.Create(doc, new List<CurveLoop> { loop }, typeId, level.Id);

        IdTag.Set(floor, u.Id);
        floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(elevation - level.Elevation);
    }

    public void Delete(Document doc, string id)
    {
        Floor? floor = FindById(doc, id);
        if (floor is null)
            return;

        ElementId typeId = floor.GetTypeId();
        doc.Delete(floor.Id);
        DeleteTypeIfUnused(doc, typeId);
        Log.Info($"Deleted floor '{id}'.");
    }

    // ---- Reverse (Revit -> Firestore) -----------------------------------

    public IEnumerable<ManagedElement> ReadAll(Document doc)
    {
        foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
        {
            if (TryRead(doc, floor, out ElementState st))
                yield return new ManagedElement(floor.Id, st);
        }
    }

    public IEnumerable<ManagedElement> ReadAffected(Document doc, IEnumerable<ElementId> changedIds)
    {
        var seen = new HashSet<ElementId>();

        foreach (ElementId id in changedIds)
        {
            Element? el = doc.GetElement(id);
            if (el is Floor floor)
            {
                if (seen.Add(floor.Id) && TryRead(doc, floor, out ElementState st))
                    yield return new ManagedElement(floor.Id, st);
            }
            else if (el is FloorType ft && ft.Name.StartsWith(Naming.FloorPrefix, StringComparison.Ordinal))
            {
                foreach (Floor inst in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
                             .Where(f => f.GetTypeId() == id))
                {
                    if (seen.Add(inst.Id) && TryRead(doc, inst, out ElementState st))
                        yield return new ManagedElement(inst.Id, st);
                }
            }
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
            ["type"] = "floor",
            ["polyline"] = poly,
            ["thickness"] = state.Thickness,
            ["material"] = state.Material ?? string.Empty,
        };
    }

    private bool TryRead(Document doc, Floor floor, out ElementState state)
    {
        state = null!;

        if (doc.GetElement(floor.GetTypeId()) is not FloorType ft
            || !ft.Name.StartsWith(Naming.FloorPrefix, StringComparison.Ordinal))
            return false;

        string? id = IdTag.Get(floor);
        if (string.IsNullOrEmpty(id))
            return false;

        CompoundStructure? cs = ft.GetCompoundStructure();
        state = new ElementState
        {
            Category = Category,
            Id = id,
            Polyline = ReadOutline(doc, floor),
            Thickness = cs?.GetWidth() ?? 0.0,
            Material = ReadMaterial(doc, ft),
        };
        return true;
    }

    // ---- Type resolution (shared, named by thickness[+material]) --------

    private static ElementId ResolveOrCreateType(Document doc, double thicknessFeet, string? material)
    {
        string typeName = Naming.FloorTypeName(thicknessFeet, material);

        FloorType? existing = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
            .FirstOrDefault(ft => ft.Name == typeName);
        if (existing is not null)
            return existing.Id;

        FloorType baseType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No FloorType exists in the document to duplicate.");

        var newType = (FloorType)baseType.Duplicate(typeName);
        SetStructure(newType, thicknessFeet, RevitHelpers.GetOrCreateMaterial(doc, material));
        return newType.Id;
    }

    private static void SetStructure(FloorType floorType, double thicknessFeet, ElementId materialId)
    {
        CompoundStructure cs = CompoundStructure.CreateSingleLayerCompoundStructure(
            MaterialFunctionAssignment.Structure, thicknessFeet, materialId);

        // Floors reject the default wall-style end cap; copy the type's valid one.
        CompoundStructure? current = floorType.GetCompoundStructure();
        if (current is not null)
            cs.EndCap = current.EndCap;

        floorType.SetCompoundStructure(cs);
    }

    private static void DeleteTypeIfUnused(Document doc, ElementId typeId)
    {
        if (doc.GetElement(typeId) is not FloorType ft
            || !ft.Name.StartsWith(Naming.FloorPrefix, StringComparison.Ordinal))
            return;

        bool used = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
            .Any(f => f.GetTypeId() == typeId);
        if (!used)
            doc.Delete(typeId);
    }

    // ---- Helpers --------------------------------------------------------

    private static string TypeNameOf(Document doc, ElementId typeId) =>
        doc.GetElement(typeId)?.Name ?? "(type)";

    private static Floor? FindById(Document doc, string id) =>
        new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
            .FirstOrDefault(f => IdTag.Get(f) == id);

    private static CurveLoop PolylineLoop(IReadOnlyList<XYZ> pts, double planeZ)
    {
        // Flatten to a single horizontal plane; elevation is applied via the
        // floor's Height Offset From Level parameter instead.
        var p = pts.Select(q => new XYZ(q.X, q.Y, planeZ)).ToList();
        if (p.Count >= 2 && p[0].DistanceTo(p[^1]) < Tol)
            p.RemoveAt(p.Count - 1); // drop duplicated closing point

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

        ElementId sketchId = floor.GetDependentElements(new ElementClassFilter(typeof(Sketch))).FirstOrDefault()
                             ?? ElementId.InvalidElementId;
        if (doc.GetElement(sketchId) is Sketch sketch)
        {
            try
            {
                foreach (CurveArray arr in sketch.Profile) // first loop = outer boundary
                {
                    foreach (Curve c in arr)
                        pts.Add(c.GetEndPoint(0));
                    break;
                }
                if (pts.Count > 0)
                    pts.Add(pts[0]); // closed
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Sketch not yet available (e.g. element created earlier in this
                // transaction, before regeneration). Treat as unknown outline.
            }
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

    private static string? ReadMaterial(Document doc, FloorType ft)
    {
        CompoundStructure? cs = ft.GetCompoundStructure();
        IList<CompoundStructureLayer>? layers = cs?.GetLayers();
        if (layers is { Count: > 0 }
            && layers[0].MaterialId != ElementId.InvalidElementId
            && doc.GetElement(layers[0].MaterialId) is Material m)
        {
            return m.Name;
        }
        return null;
    }

    private static Level LowestLevel(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation).FirstOrDefault()
            ?? throw new InvalidOperationException("The model has no Level to host the floor.");
}

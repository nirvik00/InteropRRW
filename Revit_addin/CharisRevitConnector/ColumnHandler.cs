using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;

namespace CharisRevitConnector;

/// <summary>
/// Column family (structural column) along a vertical line, section b×h.
/// Created point-based with explicit Base/Top offsets so the column height equals
/// the line's z-span exactly (the curve-based overload lets Revit re-bind the
/// top/base to levels, which drifts the height).
/// </summary>
internal sealed class ColumnHandler : LineMemberHandler
{
    public override ElementCategory Category => ElementCategory.Column;
    public override string ArrayKey => "columns";

    protected override BuiltInCategory SymbolCategory => BuiltInCategory.OST_StructuralColumns;
    protected override StructuralType StructuralType => StructuralType.Column;
    protected override string FirestoreType => "column";
    protected override string TypePrefix => Naming.ColumnPrefix;
    protected override string TypeName(double b, double h) => Naming.ColumnTypeName(b, h);

    protected override FamilyInstance CreateInstance(Document doc, XYZ start, XYZ end, FamilySymbol symbol, Level level)
    {
        var location = new XYZ(start.X, start.Y, level.Elevation);
        FamilyInstance column = doc.Create.NewFamilyInstance(location, symbol, level, StructuralType.Column);
        SetExtents(column, start, end, level);
        return column;
    }

    protected override void UpdateGeometry(FamilyInstance instance, XYZ start, XYZ end, Level level)
    {
        if (instance.Location is LocationPoint lp)
        {
            var target = new XYZ(start.X, start.Y, lp.Point.Z);
            if (lp.Point.DistanceTo(target) > 1.0e-4)
                lp.Point = target;
        }
        SetExtents(instance, start, end, level);
    }

    protected override void PreventJoins(FamilyInstance instance)
    {
        // Keep streamed columns free-standing: detach base/top from any target
        // (beam/floor/roof/ref-plane). RemoveColumnAttachment is a no-op if none.
        if (ColumnAttachment.IsValidColumn(instance))
        {
            ColumnAttachment.RemoveColumnAttachment(instance, 0); // base
            ColumnAttachment.RemoveColumnAttachment(instance, 1); // top
        }
    }

    protected override Tuple<XYZ, XYZ> ReadEndpoints(FamilyInstance instance, Level level)
    {
        LocationPoint lp = instance.Location as LocationPoint;
        if (lp == null)
            return null;

        Document doc = instance.Document;

        double baseElev =
            LevelElevation(doc, instance, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)
            + (instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0.0);

        double topElev =
            LevelElevation(doc, instance, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)
            + (instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0.0);

        XYZ xy = lp.Point;

        return Tuple.Create(
            new XYZ(xy.X, xy.Y, baseElev),
            new XYZ(xy.X, xy.Y, topElev));
    }

    private static void SetExtents(FamilyInstance column, XYZ start, XYZ end, Level level)
    {
        double baseZ = Math.Min(start.Z, end.Z);
        double topZ = Math.Max(start.Z, end.Z);

        // Base and top on the same level, positioned by offsets → height = topZ - baseZ.
        column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.Set(level.Id);
        column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.Set(level.Id);
        column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.Set(baseZ - level.Elevation);
        column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.Set(topZ - level.Elevation);
    }

    private static double LevelElevation(Document doc, FamilyInstance column, BuiltInParameter levelParam)
    {
        ElementId id = column.get_Parameter(levelParam)?.AsElementId() ?? ElementId.InvalidElementId;
        return doc.GetElement(id) is Level lvl ? lvl.Elevation : 0.0;
    }
}

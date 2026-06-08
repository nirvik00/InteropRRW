using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CharisRevitConnector;

/// <summary>Beam family (structural framing) along a line, section b×h.</summary>
internal sealed class BeamHandler : LineMemberHandler
{
    public override ElementCategory Category => ElementCategory.Beam;
    public override string ArrayKey => "beams";

    protected override BuiltInCategory SymbolCategory => BuiltInCategory.OST_StructuralFraming;
    protected override StructuralType StructuralType => StructuralType.Beam;
    protected override string FirestoreType => "beam";
    protected override string TypePrefix => Naming.BeamPrefix;
    protected override string TypeName(double b, double h) => Naming.BeamTypeName(b, h);

    protected override void PreventJoins(FamilyInstance instance)
    {
        // Disallow end joins so beams aren't coped/cut back to supports.
        if (StructuralFramingUtils.IsJoinAllowedAtEnd(instance, 0))
            StructuralFramingUtils.DisallowJoinAtEnd(instance, 0);
        if (StructuralFramingUtils.IsJoinAllowedAtEnd(instance, 1))
            StructuralFramingUtils.DisallowJoinAtEnd(instance, 1);
    }
}

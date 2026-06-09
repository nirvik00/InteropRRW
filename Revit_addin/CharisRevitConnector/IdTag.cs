using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;

namespace CharisRevitConnector;

/// <summary>
/// Durable 1-to-1 binding between a Revit element and its Firestore element id.
/// The id is stored in Extensible Storage (hidden, not user-editable, saved in
/// the .rvt) so the mapping survives closing/reopening sessions — no duplicates
/// are created on reconnect. The id is also mirrored into the Comments parameter
/// for human visibility, and reads fall back to Comments so elements tagged
/// before this feature still resolve (and migrate to storage on next write).
/// </summary>
internal static class IdTag
{
    private static readonly Guid SchemaGuid = new("c6b61c90-339c-4290-a044-41c4cedc64de");
    private const string SchemaName = "CharisFirestoreId";
    private const string FieldName = "FirestoreId";

    private static Schema GetSchema()
    {
        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is not null)
            return schema;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetVendorId("charis");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(FieldName, typeof(string));
        return builder.Finish();
    }

    /// <summary>Tag an element with its Firestore id (Extensible Storage + Comments mirror).</summary>
    public static void Set(Element element, string id)
    {
        var entity = new Entity(GetSchema());
        entity.Set(FieldName, id);
        element.SetEntity(entity);

        element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(id);
    }

    /// <summary>Read an element's Firestore id: storage first, then legacy Comments.</summary>
    public static string? Get(Element element)
    {
        Entity entity = element.GetEntity(GetSchema());
        if (entity.IsValid())
        {
            string value = entity.Get<string>(FieldName);
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        string? comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
        return string.IsNullOrEmpty(comments) ? null : comments;
    }
}

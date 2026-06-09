using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CharisRevitConnector;

/// <summary>Small shared helpers used across family handlers.</summary>
internal static class RevitHelpers
{
    public static Level LowestLevel(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation).FirstOrDefault()
            ?? throw new InvalidOperationException("The model has no Level.");

    // Element <-> Firestore id binding (durable via Extensible Storage; see IdTag).
    public static string? Comments(Element e) => IdTag.Get(e);

    public static void SetComments(Element e, string value) => IdTag.Set(e, value);

    /// <summary>Known material names → shading color (applied on create AND reuse).</summary>
    private static readonly Dictionary<string, (byte R, byte G, byte B)> KnownColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["concrete"] = (0xBE, 0xBE, 0xBE),
            ["wood"] = (0xFF, 0xFF, 0xE0),
        };

    public static ElementId GetOrCreateMaterial(Document doc, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ElementId.InvalidElementId;

        Material? material = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

        if (material is null)
            material = doc.GetElement(Material.Create(doc, name)) as Material;
        if (material is null)
            return ElementId.InvalidElementId;

        // Known names get a fixed shading color, refreshed even if pre-existing.
        if (KnownColors.TryGetValue(name.Trim(), out (byte R, byte G, byte B) c))
        {
            material.Color = new Color(c.R, c.G, c.B);
            material.UseRenderAppearanceForShading = false; // show the explicit color
        }

        return material.Id;
    }

    public static string? MaterialNameOfLayer(Document doc, CompoundStructure? cs)
    {
        IList<CompoundStructureLayer>? layers = cs?.GetLayers();
        if (layers is { Count: > 0 }
            && layers[0].MaterialId != ElementId.InvalidElementId
            && doc.GetElement(layers[0].MaterialId) is Material m)
        {
            return m.Name;
        }
        return null;
    }

    /// <summary>Set the first matching writable double parameter; true if one matched.</summary>
    public static bool SetDimension(Element e, double value, params string[] names)
    {
        foreach (string name in names)
        {
            Parameter? p = e.LookupParameter(name);
            if (p is { IsReadOnly: false } && p.StorageType == StorageType.Double)
            {
                p.Set(value);
                return true;
            }
        }
        return false;
    }

    public static double GetDimension(Element e, params string[] names)
    {
        foreach (string name in names)
        {
            Parameter? p = e.LookupParameter(name);
            if (p is not null && p.StorageType == StorageType.Double)
                return p.AsDouble();
        }
        return 0.0;
    }

    /// <summary>True if any of the names matches a writable double parameter.</summary>
    public static bool HasDimension(Element e, params string[] names) =>
        names.Any(n => e.LookupParameter(n) is { IsReadOnly: false, StorageType: StorageType.Double });

    /// <summary>Comma-separated names of writable double parameters (for diagnostics).</summary>
    public static string NumericParamNames(Element e) =>
        string.Join(", ", e.GetOrderedParameters()
            .Where(p => p.StorageType == StorageType.Double && !p.IsReadOnly)
            .Select(p => p.Definition?.Name)
            .Where(n => !string.IsNullOrEmpty(n)));
}

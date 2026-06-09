using Rhino;
using Rhino.DocObjects;

namespace SixCharis.RhinoReviewInterop.Extraction;

public sealed class LayerObjects
{
    public List<LayerObject> Floors { get; } = [];
    public List<LayerObject> Walls { get; } = [];
    public List<LayerObject> Beams { get; } = [];
    public List<LayerObject> Columns { get; } = [];
}

public sealed class LayerObject
{
    public required RhinoObject RhinoObject { get; init; }
    public required string LayerName { get; init; }
}

internal static class LayerObjectCollector
{
    public static LayerObjects Collect(RhinoDoc doc)
    {
        var result = new LayerObjects();
        var settings = new ObjectEnumeratorSettings
        {
            NormalObjects = true,
            LockedObjects = true,
            HiddenObjects = true,
            ActiveObjects = true,
            ReferenceObjects = true
        };

        foreach (var rhinoObject in doc.Objects.GetObjectList(settings))
        {
            var layer = doc.Layers[rhinoObject.Attributes.LayerIndex];
            if (layer is null || layer.IsDeleted)
            {
                continue;
            }

            var layerName = layer.FullPath;
            var layerObject = new LayerObject
            {
                RhinoObject = rhinoObject,
                LayerName = layerName
            };

            if (LayerNameMatches(layerName, "floor", "floors", "slab", "slabs"))
            {
                result.Floors.Add(layerObject);
            }
            else if (LayerNameMatches(layerName, "wall", "walls"))
            {
                result.Walls.Add(layerObject);
            }
            else if (LayerNameMatches(layerName, "beam", "beams"))
            {
                result.Beams.Add(layerObject);
            }
            else if (LayerNameMatches(layerName, "column", "columns", "col", "cols"))
            {
                result.Columns.Add(layerObject);
            }
        }

        return result;
    }

    private static bool LayerNameMatches(string layerName, params string[] tokens)
    {
        var normalized = layerName.ToLowerInvariant();
        return tokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}

using Rhino;
using Rhino.DocObjects;

using InteropRhino.Extraction;
using InteropRhino.Schema;

using System;
using System.Linq;
using System.Collections.Generic;

namespace InteropRhino.Extraction
{
    public static class RhinoInteropExtractor
    {
        public static InteropPayload Extract(RhinoDoc doc)
        {
            var context = new ExtractionContext(doc);
            var layerObjects = LayerObjectCollector.Collect(doc);

            var payload = new InteropPayload
            {
                DocumentName = doc.Name,
                Units = doc.ModelUnitSystem.ToString(),
                Floors = FloorExtractor.Extract(layerObjects.Floors, context),
                Walls = WallExtractor.Extract(layerObjects.Walls, context),
                Beams = BeamExtractor.Extract(layerObjects.Beams, context),
                Columns = ColumnExtractor.Extract(layerObjects.Columns, context),
                Issues = context.Issues
            };

            AddMissingLayerIssue(payload.Issues, "floor", layerObjects.Floors);
            AddMissingLayerIssue(payload.Issues, "wall", layerObjects.Walls);
            AddMissingLayerIssue(payload.Issues, "beam", layerObjects.Beams);
            AddMissingLayerIssue(payload.Issues, "column", layerObjects.Columns);

            return payload;
        }

        private static void AddMissingLayerIssue(
            ICollection<ExtractionIssue> issues,
            string type,
            IReadOnlyCollection<LayerObject> objects)
        {
            if (objects.Count > 0)
            {
                return;
            }

            issues.Add(new ExtractionIssue
            {
                Type = "missing_layer_objects",
                Message = $"No Rhino objects were found on layers matching '{type}'."
            });
        }
    }

    public sealed class ExtractionContext
    {
        private const string ElementIdKey = "rhinoReview.elementId";
        private readonly HashSet<string> _claimedElementIds = new(StringComparer.OrdinalIgnoreCase);

        public ExtractionContext(RhinoDoc doc)
        {
            Document = doc;
        }

        public RhinoDoc Document { get; }
        public double Tolerance => Document.ModelAbsoluteTolerance;
        public List<ExtractionIssue> Issues { get; } = [];

        public string StableElementId(RhinoObject rhinoObject, string layer)
        {
            var existing = rhinoObject.Attributes.GetUserString(ElementIdKey);
            if (TryClaim(existing, out var claimedExisting))
            {
                return claimedExisting;
            }

            if (!string.IsNullOrWhiteSpace(existing))
            {
                return RestampDuplicateElementId(rhinoObject, layer, existing);
            }

            existing = rhinoObject.Geometry.GetUserString(ElementIdKey);
            if (TryClaim(existing, out claimedExisting))
            {
                StampElementId(rhinoObject, layer, claimedExisting);
                return claimedExisting;
            }

            if (!string.IsNullOrWhiteSpace(existing))
            {
                return RestampDuplicateElementId(rhinoObject, layer, existing);
            }

            var stableId = NewClaimedElementId();
            return StampElementId(rhinoObject, layer, stableId)
                ? stableId
                : rhinoObject.Id.ToString("D");
        }

        public void AddIssue(string type, string message, RhinoObject rhinoObject, string layer)
        {
            Issues.Add(new ExtractionIssue
            {
                Type = type,
                Message = message,
                RhinoObjectId = rhinoObject.Id.ToString("D"),
                Layer = layer
            });
        }

        private bool StampElementId(RhinoObject rhinoObject, string layer, string stableId)
        {
            var attributes = rhinoObject.Attributes.Duplicate();
            attributes.SetUserString(ElementIdKey, stableId);

            if (Document.Objects.ModifyAttributes(rhinoObject.Id, attributes, quiet: true))
            {
                return true;
            }

            AddIssue(
                "stable_id_write_failed",
                "Rhino object could not be stamped with a stable rhinoReview.elementId; native Rhino id was used for this extraction.",
                rhinoObject,
                layer);
            return false;
        }

        private string RestampDuplicateElementId(RhinoObject rhinoObject, string layer, string duplicateId)
        {
            var stableId = NewClaimedElementId();
            if (StampElementId(rhinoObject, layer, stableId))
            {
                AddIssue(
                    "duplicate_stable_id_reassigned",
                    $"Rhino object had duplicate {ElementIdKey} '{duplicateId}' from a copied object; reassigned to '{stableId}'.",
                    rhinoObject,
                    layer);
                return stableId;
            }

            return rhinoObject.Id.ToString("D");
        }

        private bool TryClaim(string? candidate, out string claimed)
        {
            claimed = string.Empty;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            claimed = candidate.Trim();
            return _claimedElementIds.Add(claimed);
        }

        private string NewClaimedElementId()
        {
            string stableId;
            do
            {
                stableId = Guid.NewGuid().ToString("D");
            }
            while (!_claimedElementIds.Add(stableId));

            return stableId;
        }
    }
}
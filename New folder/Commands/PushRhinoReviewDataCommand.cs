using System.Text.Json;
using Rhino;
using Rhino.Commands;
using Rhino.UI;
using InteropRhino.Extraction;

namespace InteropRhino.Commands
{
    public sealed class PushRhinoReviewDataCommand : Command
    {
        public override string EnglishName => "PushRhinoReviewData";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var payload = RhinoInteropExtractor.Extract(doc);
            var json = JsonSerializer.Serialize(payload, JsonOptions.Pretty);

            Dialogs.ShowTextDialog(json, "Rhino Review Interop Payload");
            RhinoApp.WriteLine(
                $"Rhino Review payload prepared: {payload.Floors.Count} floors, {payload.Walls.Count} walls, {payload.Beams.Count} beams, {payload.Columns.Count} columns.");

            return Result.Success;
        }
    }

}


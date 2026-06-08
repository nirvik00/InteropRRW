using Rhino;
using Rhino.Commands;
using Rhino.UI;
using InteropRhino.Extraction;
using InteropRhino.Firebase;

namespace InteropRhino.Commands
{
    public sealed class PushRhinoReviewDataToFirebaseCommand : Command
    {
        public override string EnglishName => "PushRhinoReviewDataToFirebase";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            try
            {
                var payload = RhinoInteropExtractor.Extract(doc);
                var result = FirebaseSyncService.PushLatestAsync(payload).GetAwaiter().GetResult();

                var message =
                    $"Pushed Rhino Review payload to Firebase.\n\n" +
                    $"Database path: {result.DatabasePath}\n" +
                    $"Model ID: {result.ModelId}\n" +
                    $"Floors: {payload.Floors.Count}\n" +
                    $"Walls: {payload.Walls.Count}\n" +
                    $"Beams: {payload.Beams.Count}\n" +
                    $"Columns: {payload.Columns.Count}";

                RhinoApp.WriteLine(message.Replace("\n", " "));
                Dialogs.ShowTextDialog(message, "Firebase Push Complete");
                return Result.Success;
            }
            catch (Exception exception)
            {
                var message = $"Firebase push failed:\n\n{exception.Message}";
                RhinoApp.WriteLine(message.Replace("\n", " "));
                Dialogs.ShowTextDialog(message, "Firebase Push Failed");
                return Result.Failure;
            }
        }
    }

}


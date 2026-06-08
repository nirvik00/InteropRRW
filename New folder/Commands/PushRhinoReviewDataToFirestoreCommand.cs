using Rhino;
using Rhino.Commands;
using InteropRhino.Extraction;
using InteropRhino.Firebase;

namespace InteropRhino.Commands
{

    public sealed class PushRhinoReviewDataToFirestoreCommand : Command
    {
        public override string EnglishName => "PushRhinoReviewDataToFirestore";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            return PushInBackground(doc);
        }

        public static Result PushInBackground(RhinoDoc doc)
        {
            try
            {
                var payload = RhinoInteropExtractor.Extract(doc);
                RhinoApp.WriteLine(
                    $"Preparing Firestore push in background: {payload.Floors.Count} floors, {payload.Walls.Count} walls, {payload.Beams.Count} beams, {payload.Columns.Count} columns.");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await FirestoreSyncService.PushLatestAsync(payload);
                        var message =
                            $"Pushed Rhino Review payload to Firestore.\n\n" +
                            $"Document path: {result.DocumentPath}\n" +
                            $"Model ID: {result.ModelId}\n" +
                            $"Floors: {result.Floors}\n" +
                            $"Walls: {result.Walls}\n" +
                            $"Beams: {result.Beams}\n" +
                            $"Columns: {result.Columns}";

                        CommandUi.WriteLine(message);
                    }
                    catch (Exception exception)
                    {
                        CommandUi.ShowTextDialog($"Firestore push failed:\n\n{exception.Message}", "Firestore Push Failed");
                    }
                });

                return Result.Success;
            }
            catch (Exception exception)
            {
                var message = $"Firestore push failed:\n\n{exception.Message}";
                RhinoApp.WriteLine(message.Replace("\n", " "));
                return Result.Failure;
            }
        }
    }

}

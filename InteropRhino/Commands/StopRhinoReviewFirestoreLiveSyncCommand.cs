using Rhino;
using Rhino.Commands;
using InteropRhino.Firebase;
using System.Threading.Tasks;
using System;


namespace InteropRhino.Commands
{
    public sealed class StopRhinoReviewFirestoreLiveSyncCommand : Command
    {
        public override string EnglishName => "StopRhinoReviewFirestoreLiveSync";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Stopping Firestore live sync in background.");

            _ = Task.Run(async () =>
            {
                try
                {
                    var stopped = await FirestoreSyncService.StopLatestListenerAsync();
                    var message = stopped
                        ? "Stopped Firestore live sync."
                        : "Firestore live sync was not running.";

                    CommandUi.WriteLine(message);
                }
                catch (Exception exception)
                {
                    CommandUi.ShowTextDialog(
                        $"Firestore live sync failed to stop:\n\n{exception.Message}",
                        "Firestore Live Sync Failed");
                }
            });

            return Result.Success;
        }
    }

}


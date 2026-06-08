using Rhino;
using Rhino.Commands;
using InteropRhino.Firebase;
using System.Threading.Tasks;
using System;

namespace InteropRhino.Commands
{
    public sealed class RhinoReviewETestSyncCommand : Command
    {
        public override string EnglishName => "RhinoReviewETestSync";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var status = await FirestoreETestGeometrySyncService.ToggleAsync().ConfigureAwait(false);
                    var state = status.Running ? "started" : "stopped";
                    var already = status.AlreadyRunning ? " already" : string.Empty;
                    CommandUi.WriteLine($"Firestore e-test geometry sync{already} {state}. Path: {status.CollectionPath}");
                }
                catch (Exception exception)
                {
                    CommandUi.WriteLine($"Firestore e-test geometry sync failed: {exception.Message}");
                }
            });

            return Result.Success;
        }
    }

}


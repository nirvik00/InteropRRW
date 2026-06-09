using Rhino;
using Rhino.Commands;
using SixCharis.RhinoReviewInterop.Firebase;

namespace SixCharis.RhinoReviewInterop.Commands;

public sealed class StartRhinoReviewFirestoreLiveSyncCommand : Command
{
    public override string EnglishName => "StartRhinoReviewFirestoreLiveSync";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine("Starting Firestore live sync in background.");

        _ = Task.Run(() =>
        {
            try
            {
                var status = FirestoreSyncService.StartLatestListenerAsync().GetAwaiter().GetResult();
                var message = status.AlreadyRunning
                    ? $"Firestore live sync is already running.\n\nDocument path: {status.DocumentPath}"
                    : $"Started Firestore live sync.\n\nDocument path: {status.DocumentPath}";

                CommandUi.WriteLine(message);
            }
            catch (Exception exception)
            {
                CommandUi.ShowTextDialog(
                    $"Firestore live sync failed to start:\n\n{exception.Message}",
                    "Firestore Live Sync Failed");
            }
        });

        return Result.Success;
    }
}

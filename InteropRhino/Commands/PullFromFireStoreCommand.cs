// Commands/PullFromFirestoreCommand.cs
using Rhino;
using Rhino.Commands;
using InteropRhino.Firebase;
using System;
using System.Threading.Tasks;

namespace InteropRhino.Commands
{
    public sealed class PullFromFirestoreCommand : Command
    {
        public override string EnglishName => "PullFromFirestore";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Pulling from Firestore...");

            var serialNumber = doc.RuntimeSerialNumber;

            _ = Task.Run(async () =>
            {
                try
                {
                    var status = await FirestoreSyncService
                        .StartLatestListenerAsync(serialNumber, applyInitialSnapshot: true)
                        .ConfigureAwait(false);

                    CommandUi.WriteLine($"Pull started for {status.DocumentPath}. Waiting for snapshot...");

                    await Task.Delay(5000).ConfigureAwait(false);

                    await FirestoreSyncService.StopLatestListenerAsync().ConfigureAwait(false);
                    CommandUi.WriteLine("Pull complete.");
                }
                catch (Exception ex)
                {
                    CommandUi.ShowTextDialog($"Pull failed:\n\n{ex.Message}", "Pull Failed");
                }
            });

            return Result.Success;
        }
    }
}
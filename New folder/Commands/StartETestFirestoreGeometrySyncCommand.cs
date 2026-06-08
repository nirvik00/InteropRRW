using Rhino;
using Rhino.Commands;
using InteropRhino.Firebase;
using System;
using System.Threading.Tasks;


namespace InteropRhino.Commands
{
    public sealed class StartETestFirestoreGeometrySyncCommand : Command
    {
        public override string EnglishName => "StartETestFirestoreGeometrySync";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var status = await FirestoreETestGeometrySyncService.StartAsync().ConfigureAwait(false);
                    var already = status.AlreadyRunning ? " already" : string.Empty;
                    CommandUi.WriteLine($"Firestore e-test geometry sync{already} started. Path: {status.CollectionPath}");
                }
                catch (Exception exception)
                {
                    CommandUi.WriteLine($"Firestore e-test geometry sync failed to start: {exception.Message}");
                }
            });

            return Result.Success;
        }
    }

}

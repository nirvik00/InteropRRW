using System;
using System.Threading.Tasks;
using Rhino;
using Rhino.Commands;
using InteropRhino.Firebase;

namespace InteropRhino.Commands
{
    public sealed class StopETestFirestoreGeometrySyncCommand : Command
    {
        public override string EnglishName => "StopETestFirestoreGeometrySync";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var status = await FirestoreETestGeometrySyncService.StopAsync().ConfigureAwait(false);
                    CommandUi.WriteLine($"Firestore e-test geometry sync stopped. Path: {status.CollectionPath}");
                }
                catch (Exception exception)
                {
                    CommandUi.WriteLine($"Firestore e-test geometry sync failed to stop: {exception.Message}");
                }
            });

            return Result.Success;
        }
    }

}


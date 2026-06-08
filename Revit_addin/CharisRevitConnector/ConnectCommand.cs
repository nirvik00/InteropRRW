using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CharisRevitConnector;

/// <summary>
/// Toggles the two-way Firestore sync. First click connects, starts a realtime
/// listener per family collection, and enables Revit→Firestore write-back. A
/// second click stops everything and disconnects.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ConnectCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FirebaseConnection connection = App.Connection;

        try
        {
            if (connection.IsConnected)
            {
                StopSync();
                connection.Disconnect();
                Log.Info("Disconnected from Firestore; sync stopped.");
                TaskDialog.Show("Charis", "Disconnected from Firestore. Sync stopped.");
                return Result.Succeeded;
            }

            // Connect off the Revit UI thread (network I/O + avoids deadlock).
            ConnectionInfo info = Task.Run(connection.ConnectAsync).GetAwaiter().GetResult();

            // Forward: one listener on the single layered document.
            App.Listener = new DocumentListener(connection.Db!, App.Handlers, App.SyncHandler, App.SyncEvent!);
            App.Listener.Start();

            // Reverse: push Revit edits back. Seed the registry from the active doc.
            Document? activeDoc = commandData.Application.ActiveUIDocument?.Document;
            var writer = new DocumentWriter(connection.Db!, App.HandlerByCategory);
            App.Watcher.StartWritingTo(writer, activeDoc);

            // Log each family's readiness (loaded families / b-h params) to the log.
            if (activeDoc is not null)
            {
                foreach (IFamilyHandler handler in App.Handlers)
                    handler.LogReadiness(activeDoc);
            }

            string target = $"{Config.CollectionId}/{Config.DocumentId}";
            Log.Info($"Connected to '{info.ProjectId}'; syncing document: {target}.");
            TaskDialog.Show(
                "Charis — Connected",
                $"Connected to Cloud Firestore (two-way sync).\n\n"
                + $"Project: {info.ProjectId}\n"
                + $"Document: {target}\n\n"
                + "Firestore ⇄ Revit: floors/walls/beams/columns sync both ways.\n\n"
                + $"Log: {Log.LogPath}");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Exception cause = ex is AggregateException agg && agg.InnerException is not null
                ? agg.InnerException
                : ex;

            Log.Error("Connect failed", cause);
            message = cause.Message;
            TaskDialog.Show("Charis — Connection failed", cause.Message);
            return Result.Failed;
        }
    }

    private static void StopSync()
    {
        if (App.Listener is not null)
        {
            Task.Run(() => App.Listener.StopAsync()).GetAwaiter().GetResult();
            App.Listener = null;
        }

        App.Watcher.StopWriting();
    }
}

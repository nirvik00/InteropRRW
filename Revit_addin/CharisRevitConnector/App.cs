using System.Reflection;
using Autodesk.Revit.UI;

namespace CharisRevitConnector;

/// <summary>
/// Add-in entry point. Registers the ribbon UI and owns the app-lifetime sync
/// plumbing: the Firestore connection, the family handlers, the shared change
/// queue + ExternalEvent, the per-collection listeners, and the reverse-sync
/// watcher.
/// </summary>
public class App : IExternalApplication
{
    private const string TabName = "Charis";
    private const string PanelName = "Firebase Stream";

    /// <summary>App-lifetime owner of the Firestore connection (auth + client).</summary>
    internal static readonly FirebaseConnection Connection = new();

    /// <summary>One handler per element family — one Firestore collection each.</summary>
    internal static readonly IReadOnlyList<IFamilyHandler> Handlers = new IFamilyHandler[]
    {
        new FloorHandler(),
        new WallHandler(),
        new BeamHandler(),
        new ColumnHandler(),
    };

    internal static readonly IReadOnlyDictionary<ElementCategory, IFamilyHandler> HandlerByCategory =
        Handlers.ToDictionary(h => h.Category);

    /// <summary>Applies the latest document snapshot on the Revit thread.</summary>
    internal static readonly SyncEventHandler SyncHandler = new(HandlerByCategory);

    /// <summary>Created on the Revit thread in OnStartup (required for ExternalEvent).</summary>
    internal static ExternalEvent? SyncEvent;

    /// <summary>The single-document listener, or null when not connected.</summary>
    internal static DocumentListener? Listener;

    /// <summary>Reverse sync: watches Revit edits and pushes them to the document.</summary>
    internal static readonly RevitChangeWatcher Watcher = new(Handlers);

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Tab already exists — reuse it.
        }

        RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);

        string assemblyPath = Assembly.GetExecutingAssembly().Location;

        var connectButton = new PushButtonData(
            name: "CharisConnectButton",
            text: "Connect",
            assemblyName: assemblyPath,
            className: "CharisRevitConnector.ConnectCommand")
        {
            ToolTip = "Connect / disconnect the two-way Firestore sync "
                      + "(floors, walls, beams, columns)."
        };

        panel.AddItem(connectButton);

        // Must be created on the Revit thread, in a valid API context.
        SyncEvent = ExternalEvent.Create(SyncHandler);

        // Reverse sync: listen for Revit edits. No-ops until Connect sets a writer.
        application.ControlledApplication.DocumentChanged += Watcher.OnDocumentChanged;

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentChanged -= Watcher.OnDocumentChanged;
        Watcher.StopWriting();

        try
        {
            Listener?.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort on shutdown.
        }

        Listener = null;
        Connection.Disconnect();
        SyncEvent?.Dispose();
        return Result.Succeeded;
    }
}

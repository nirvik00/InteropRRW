using Rhino;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;

using System;
using System.Threading;
using System.Threading.Tasks;


using InteropRhino.Extraction;
using InteropRhino.Commands;
using InteropRhino.Firebase;

namespace InteropRhino.Firebase
{
    public static class FirestoreAutoSyncService
    {
        private static readonly object Gate = new();
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(100);
        private static Timer? _timer;
        private static uint _documentSerialNumber;
        private static bool _enabled;
        private static bool _pendingAfterPush;
        private static bool _pushInFlight;
        private static int _remoteApplyDepth;
        private static string _lastReason = "manual";

        public static bool IsEnabled
        {
            get
            {
                lock (Gate)
                {
                    return _enabled;
                }
            }
        }

        public static AutoSyncStatus Toggle(RhinoDoc doc)
        {
            return IsEnabled ? Stop() : Start(doc);
        }

        public static AutoSyncStatus Start(RhinoDoc doc)
        {
            lock (Gate)
            {
                if (_enabled)
                {
                    return new AutoSyncStatus(true, true, _documentSerialNumber);
                }

                _documentSerialNumber = doc.RuntimeSerialNumber;
                _enabled = true;
                _timer = new Timer(OnDebounceElapsed);
                Subscribe();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await FirestoreSyncService
                        .StartLatestListenerAsync(_documentSerialNumber, applyInitialSnapshot: false)
                        .ConfigureAwait(false);
                    CommandUi.WriteLine("Rhino Review sync listener is running.");
                }
                catch (Exception exception)
                {
                    CommandUi.WriteLine($"Rhino Review sync listener failed to start: {exception.Message}");
                }
            });

            SchedulePush("sync enabled");
            return new AutoSyncStatus(true, false, doc.RuntimeSerialNumber);
        }

        public static AutoSyncStatus Stop()
        {
            uint serialNumber;
            lock (Gate)
            {
                if (!_enabled)
                {
                    return new AutoSyncStatus(false, true, _documentSerialNumber);
                }

                serialNumber = _documentSerialNumber;
                _enabled = false;
                _pendingAfterPush = false;
                _timer?.Dispose();
                _timer = null;
                Unsubscribe();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await FirestoreSyncService.StopLatestListenerAsync().ConfigureAwait(false);
                    CommandUi.WriteLine("Rhino Review sync listener stopped.");
                }
                catch (Exception exception)
                {
                    CommandUi.WriteLine($"Rhino Review sync listener failed to stop: {exception.Message}");
                }
            });

            return new AutoSyncStatus(false, false, serialNumber);
        }

        public static void SchedulePush(string reason)
        {
            lock (Gate)
            {
                if (!_enabled || _timer is null || _remoteApplyDepth > 0)
                {
                    return;
                }

                _lastReason = reason;
                _timer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
            }

            CommandUi.WriteLine($"Rhino Review auto-sync scheduled after {reason}.");
        }

        public static T SuppressPushWhileApplyingRemoteChange<T>(Func<T> action)
        {
            lock (Gate)
            {
                _remoteApplyDepth++;
            }

            try
            {
                return action();
            }
            finally
            {
                lock (Gate)
                {
                    _remoteApplyDepth = Math.Max(0, _remoteApplyDepth - 1);
                }
            }
        }

        private static void Subscribe()
        {
            RhinoDoc.AddRhinoObject += OnObjectChanged;
            RhinoDoc.DeleteRhinoObject += OnObjectChanged;
            RhinoDoc.UndeleteRhinoObject += OnObjectChanged;
            RhinoDoc.ReplaceRhinoObject += OnObjectReplaced;
            RhinoDoc.ModifyObjectAttributes += OnObjectAttributesModified;
            RhinoDoc.AfterTransformObjects += OnObjectsTransformed;
            RhinoDoc.LayerTableEvent += OnLayerTableEvent;
            RhinoDoc.CloseDocument += OnDocumentClosed;
        }

        private static void Unsubscribe()
        {
            RhinoDoc.AddRhinoObject -= OnObjectChanged;
            RhinoDoc.DeleteRhinoObject -= OnObjectChanged;
            RhinoDoc.UndeleteRhinoObject -= OnObjectChanged;
            RhinoDoc.ReplaceRhinoObject -= OnObjectReplaced;
            RhinoDoc.ModifyObjectAttributes -= OnObjectAttributesModified;
            RhinoDoc.AfterTransformObjects -= OnObjectsTransformed;
            RhinoDoc.LayerTableEvent -= OnLayerTableEvent;
            RhinoDoc.CloseDocument -= OnDocumentClosed;
        }

        private static void OnObjectChanged(object? sender, RhinoObjectEventArgs args)
        {
            ScheduleIfCurrent(args.TheObject, "object changed");
        }

        private static void OnObjectReplaced(object? sender, RhinoReplaceObjectEventArgs args)
        {
            ScheduleIfCurrent(args.Document, "object replaced");
        }

        private static void OnObjectAttributesModified(object? sender, RhinoModifyObjectAttributesEventArgs args)
        {
            ScheduleIfCurrent(args.Document, "object attributes changed");
        }

        private static void OnObjectsTransformed(object? sender, RhinoAfterTransformObjectsEventArgs args)
        {
            ScheduleIfCurrent(sender, "objects transformed");
        }

        private static void OnLayerTableEvent(object? sender, LayerTableEventArgs args)
        {
            if (IsCurrentDocument(args.Document))
            {
                SchedulePush("layer changed");
            }
        }

        private static void OnDocumentClosed(object? sender, DocumentEventArgs args)
        {
            if (args.DocumentSerialNumber == _documentSerialNumber)
            {
                Stop();
            }
        }

        private static void ScheduleIfCurrent(object? sender, string reason)
        {
            if (sender is RhinoDoc doc && IsCurrentDocument(doc))
            {
                SchedulePush(reason);
            }
            else if (RhinoDoc.ActiveDoc is { } activeDoc && IsCurrentDocument(activeDoc))
            {
                SchedulePush(reason);
            }
        }

        private static void ScheduleIfCurrent(RhinoObject? rhinoObject, string reason)
        {
            if (rhinoObject?.Document is { } doc && IsCurrentDocument(doc))
            {
                SchedulePush(reason);
            }
            else if (RhinoDoc.ActiveDoc is { } activeDoc && IsCurrentDocument(activeDoc))
            {
                SchedulePush(reason);
            }
        }

        private static bool IsCurrentDocument(RhinoDoc? doc)
        {
            return doc is not null && doc.RuntimeSerialNumber == _documentSerialNumber;
        }

        private static void OnDebounceElapsed(object? state)
        {
            RhinoApp.InvokeOnUiThread((Action)ExtractAndPushOnUiThread);
        }

        private static void ExtractAndPushOnUiThread()
        {
            RhinoDoc? doc;
            string reason;

            lock (Gate)
            {
                if (!_enabled)
                {
                    return;
                }

                if (_pushInFlight)
                {
                    _pendingAfterPush = true;
                    return;
                }

                doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
                reason = _lastReason;
                _pushInFlight = true;
            }

            if (doc is null)
            {
                Stop();
                return;
            }

            var payload = RhinoInteropExtractor.Extract(doc);
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await FirestoreSyncService.PushLatestAsync(payload).ConfigureAwait(false);
                    CommandUi.WriteLine(
                        $"Rhino Review auto-sync pushed {payload.Floors.Count} floors, {payload.Walls.Count} walls, {payload.Beams.Count} beams, {payload.Columns.Count} columns after {reason}. Document: {result.DocumentPath}");
                }
                catch (Exception exception)
                {
                    CommandUi.WriteLine($"Rhino Review auto-sync push failed: {exception.Message}");
                }
                finally
                {
                    bool scheduleAgain;
                    lock (Gate)
                    {
                        _pushInFlight = false;
                        scheduleAgain = _enabled && _pendingAfterPush;
                        _pendingAfterPush = false;
                    }

                    if (scheduleAgain)
                    {
                        SchedulePush("queued changes");
                    }
                }
            });
        }
    }

    public sealed record AutoSyncStatus(
        bool Enabled,
        bool AlreadyInRequestedState,
        uint DocumentSerialNumber);


}
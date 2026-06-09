using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CharisRevitConnector
{
    internal sealed class DocumentListener
    {
        private readonly FirebaseConnection _connection;
        private readonly IEnumerable<IFamilyHandler> _handlers;
        private readonly SyncEventHandler _sink;
        private readonly ExternalEvent _externalEvent;
        private CancellationTokenSource _cts;
        private string _lastUpdateTime;

        public DocumentListener(FirebaseConnection connection, IEnumerable<IFamilyHandler> handlers,
                                SyncEventHandler sink, ExternalEvent externalEvent)
        {
            _connection = connection;
            _handlers = handlers;
            _sink = sink;
            _externalEvent = externalEvent;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => PollLoopAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
            }
            await Task.CompletedTask;
        }

        private async Task PollLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    JObject doc = await _connection.GetDocumentAsync(
                        Config.CollectionId, Config.DocumentId, cancellationToken);

                    string updateTime = doc["updateTime"]?.Value<string>();
                    if (updateTime != null && updateTime != _lastUpdateTime)
                    {
                        _lastUpdateTime = updateTime;
                        ProcessSnapshot(doc);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error("DocumentListener poll failed", ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)
                    .ContinueWith(_ => { }); // swallow cancellation
            }
        }

        private void ProcessSnapshot(JObject doc)
        {
            JObject fields = doc["fields"] as JObject;
            if (fields == null)
                return;

            var data = ParseFields(fields);

            if (WriteTracker.ConsumeIfOurs(FirestoreParse.Get(data, "writerOperationId") as string))
                return;

            var snapshot = new Dictionary<ElementCategory, List<ElementUpdate>>();
            foreach (IFamilyHandler handler in _handlers)
            {
                var list = new List<ElementUpdate>();
                if (FirestoreParse.Get(data, handler.ArrayKey) is IEnumerable<object> arr)
                {
                    foreach (object item in arr)
                    {
                        if (item is IReadOnlyDictionary<string, object> map
                            && FirestoreParse.Get(map, "id") is string id && id.Length > 0)
                        {
                            list.Add(handler.ParseElement(id, map));
                        }
                    }
                }
                snapshot[handler.Category] = list;
            }

            Log.Info("Snapshot from " + Config.CollectionId + "/" + Config.DocumentId + ": "
                + string.Join(", ", snapshot.Select(kv => kv.Key + "=" + kv.Value.Count)) + ".");

            _sink.SetPending(snapshot);
            _externalEvent.Raise();
        }

        private static Dictionary<string, object> ParseFields(JObject fields)
        {
            var result = new Dictionary<string, object>();
            foreach (var prop in fields.Properties())
                result[prop.Name] = ParseValue(prop.Value as JObject);
            return result;
        }

        private static object ParseValue(JObject valueObj)
        {
            if (valueObj == null) return null;
            if (valueObj["stringValue"] != null) return valueObj["stringValue"].Value<string>();
            if (valueObj["integerValue"] != null) return valueObj["integerValue"].Value<long>();
            if (valueObj["doubleValue"] != null) return valueObj["doubleValue"].Value<double>();
            if (valueObj["booleanValue"] != null) return valueObj["booleanValue"].Value<bool>();
            if (valueObj["nullValue"] != null) return null;
            if (valueObj["mapValue"] != null)
            {
                var mapFields = valueObj["mapValue"]["fields"] as JObject;
                return mapFields != null ? ParseFields(mapFields) : new Dictionary<string, object>();
            }
            if (valueObj["arrayValue"] != null)
            {
                var values = valueObj["arrayValue"]["values"] as JArray;
                var list = new List<object>();
                if (values != null)
                    foreach (var item in values)
                        list.Add(ParseValue(item as JObject));
                return list;
            }
            return null;
        }
    }
}
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CharisRevitConnector
{
    internal sealed class DocumentWriter
    {
        private readonly FirebaseConnection _connection;
        private readonly IReadOnlyDictionary<ElementCategory, IFamilyHandler> _handlers;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public DocumentWriter(FirebaseConnection connection, IReadOnlyDictionary<ElementCategory, IFamilyHandler> handlers)
        {
            _connection = connection;
            _handlers = handlers;
        }

        public void PushElement(ElementState state)
        {
            if (!_handlers.TryGetValue(state.Category, out IFamilyHandler handler))
                return;

            Dictionary<string, object> entry = handler.ToFirestore(state);
            string arrayKey = handler.ArrayKey;
            string id = state.Id;

            FireAndForget(async () =>
            {
                List<object> list = await ReadArrayAsync(arrayKey);
                int idx = list.FindIndex(m => IdOf(m) == id);
                if (idx >= 0) list[idx] = entry; else list.Add(entry);
                await WriteArrayAsync(arrayKey, list);
            }, "push " + state.Category + " " + id);
        }

        public void DeleteElement(ElementCategory category, string id)
        {
            if (!_handlers.TryGetValue(category, out IFamilyHandler handler))
                return;

            string arrayKey = handler.ArrayKey;

            FireAndForget(async () =>
            {
                List<object> list = await ReadArrayAsync(arrayKey);
                int removed = list.RemoveAll(m => IdOf(m) == id);
                if (removed > 0)
                    await WriteArrayAsync(arrayKey, list);
            }, "delete " + category + " " + id);
        }

        // ---- internals ------------------------------------------------------

        private async Task<List<object>> ReadArrayAsync(string arrayKey)
        {
            JObject doc = await _connection.GetDocumentAsync(
                Config.CollectionId, Config.DocumentId);

            JObject fields = doc["fields"] as JObject;
            if (fields == null)
                return new List<object>();

            JObject fieldValue = fields[arrayKey] as JObject;
            if (fieldValue == null)
                return new List<object>();

            JArray values = fieldValue["arrayValue"]?["values"] as JArray;
            if (values == null)
                return new List<object>();

            var list = new List<object>();
            foreach (JToken item in values)
                list.Add(ParseMapValue(item as JObject));
            return list;
        }

        private async Task WriteArrayAsync(string arrayKey, List<object> list)
        {
            string operationId = Guid.NewGuid().ToString();
            WriteTracker.Remember(operationId);

            var update = new Dictionary<string, object>
            {
                [arrayKey] = list,
                ["writerInstanceId"] = Config.WriterInstanceId,
                ["writerOperationId"] = operationId,
                ["updatedAtUtc"] = DateTime.UtcNow.ToString("o"),
                ["schemaVersion"] = Config.SchemaVersion,
            };

            await _connection.SetDocumentAsync(Config.CollectionId, Config.DocumentId, update);
        }

        private static string IdOf(object item)
        {
            if (item is IReadOnlyDictionary<string, object> m && m.TryGetValue("id", out object v))
                return v as string;
            return null;
        }

        private static object ParseMapValue(JObject valueObj)
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
                if (mapFields == null) return new Dictionary<string, object>();
                var result = new Dictionary<string, object>();
                foreach (var prop in mapFields.Properties())
                    result[prop.Name] = ParseMapValue(prop.Value as JObject);
                return result;
            }
            if (valueObj["arrayValue"] != null)
            {
                var values = valueObj["arrayValue"]["values"] as JArray;
                var list = new List<object>();
                if (values != null)
                    foreach (var item in values)
                        list.Add(ParseMapValue(item as JObject));
                return list;
            }
            return null;
        }

        private void FireAndForget(Func<Task> action, string what)
        {
            _ = Task.Run(async () =>
            {
                await _gate.WaitAsync();
                try
                {
                    await action();
                    Log.Info("Pushed to Firestore: " + what + ".");
                }
                catch (Exception ex)
                {
                    Log.Error("Firestore write failed (" + what + ")", ex);
                }
                finally
                {
                    _gate.Release();
                }
            });
        }
    }
}
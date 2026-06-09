using Google.Apis.Auth.OAuth2;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CharisRevitConnector
{
    public struct ConnectionInfo
    {
        public string ProjectId { get; }
        public ConnectionInfo(string projectId) { ProjectId = projectId; }
    }

    public sealed class FirebaseConnection
    {
        private static readonly HttpClient Http = new HttpClient();
        private readonly string _credentialJsonPath;
        private readonly string _projectId;
        private static readonly HttpMethod Patch = new HttpMethod("PATCH");
        private ServiceAccountCredential _credential;

        public string ProjectId => _projectId;
        public bool IsConnected => _credential != null;

        public FirebaseConnection(string credentialJsonPath)
        {
            _credentialJsonPath = credentialJsonPath;
            var json = JObject.Parse(File.ReadAllText(credentialJsonPath));
            _projectId = json["project_id"].Value<string>();
        }

        public async Task<ConnectionInfo> ConnectAsync()
        {
            var json = File.ReadAllText(_credentialJsonPath);
            var parameters = new ServiceAccountCredential.Initializer(
                JObject.Parse(json)["client_email"].Value<string>())
            {
                Scopes = new[] { "https://www.googleapis.com/auth/datastore" }
            }.FromPrivateKey(JObject.Parse(json)["private_key"].Value<string>());

            _credential = new ServiceAccountCredential(parameters);
            await _credential.GetAccessTokenForRequestAsync();
            return new ConnectionInfo(_projectId);
        }

        public void Disconnect() { _credential = null; }

        public async Task<string> GetAccessTokenAsync()
        {
            return await _credential.GetAccessTokenForRequestAsync();
        }

        /// <summary>Write a document via REST (SetAsync equivalent).</summary>
        public async Task SetDocumentAsync(
            string collection,
            string documentId,
            Dictionary<string, object> data,
            CancellationToken cancellationToken = default)
        {
            string token = await GetAccessTokenAsync();
            string url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/{collection}/{documentId}";

            var body = new JObject
            {
                ["fields"] = ToFirestoreFields(data)
            };
            var request = new HttpRequestMessage(Patch, new Uri(url))
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await Http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>Read a document via REST (GetSnapshotAsync equivalent).</summary>
        public async Task<JObject> GetDocumentAsync(
            string collection,
            string documentId,
            CancellationToken cancellationToken = default)
        {
            string token = await GetAccessTokenAsync();
            string url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/{collection}/{documentId}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await Http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        private static JObject ToFirestoreFields(Dictionary<string, object> data)
        {
            var fields = new JObject();
            foreach (var kvp in data)
                fields[kvp.Key] = ToFirestoreValue(kvp.Value);
            return fields;
        }

        private static JToken ToFirestoreValue(object value)
        {
            if (value == null) return new JObject { ["nullValue"] = JValue.CreateNull() };
            if (value is string s) return new JObject { ["stringValue"] = s };
            if (value is bool b) return new JObject { ["booleanValue"] = b };
            if (value is int i) return new JObject { ["integerValue"] = i.ToString() };
            if (value is long l) return new JObject { ["integerValue"] = l.ToString() };
            if (value is double d) return new JObject { ["doubleValue"] = d };
            if (value is float f) return new JObject { ["doubleValue"] = (double)f };
            if (value is Dictionary<string, object> dict)
                return new JObject { ["mapValue"] = new JObject { ["fields"] = ToFirestoreFields(dict) } };
            if (value is List<object> list)
            {
                var arr = new JArray();
                foreach (var item in list)
                    arr.Add(ToFirestoreValue(item));
                return new JObject { ["arrayValue"] = new JObject { ["values"] = arr } };
            }
            return new JObject { ["stringValue"] = value.ToString() };
        }
    }
}
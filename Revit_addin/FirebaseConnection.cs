using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System;

namespace CharisRevitConnector
{
    public struct ConnectionInfo
    {
        public string ProjectId { get; }
        public IReadOnlyList<string> RootCollections { get; }

        public ConnectionInfo(string projectId, IReadOnlyList<string> rootCollections)
        {
            ProjectId = projectId;
            RootCollections = rootCollections;
        }
    }
    public sealed class FirebaseConnection
    {
        private readonly string _credentialJsonPath;
        private readonly string _configJsonPath;
        private FirestoreDb _db;

        public bool IsConnected => _db != null;
        public string ProjectId { get; private set; }
        public FirestoreDb Db => _db;

        public FirebaseConnection(string credentialJsonPath, string configJsonPath)
        {
            _credentialJsonPath = credentialJsonPath;
            _configJsonPath = configJsonPath;
        }

        public async Task<ConnectionInfo> ConnectAsync()
        {
            if (_db != null)
                return new ConnectionInfo(ProjectId, new List<string>());

            // Tell gRPC where to find grpc_csharp_ext.x64.dll
            string pluginDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            Directory.SetCurrentDirectory(pluginDir);

            string credentialJson = File.ReadAllText(_credentialJsonPath);
            string projectId = ReadProjectId(credentialJson, _credentialJsonPath);

            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(_credentialJsonPath),
                    ProjectId = projectId,
                });
            }

            var db = new FirestoreDbBuilder
            {
                ProjectId = projectId,
                CredentialsPath = _credentialJsonPath,
                GrpcAdapter = Google.Api.Gax.Grpc.GrpcNetClientAdapter.Default,
            }.Build();

            var rootCollections = new List<string>();
            await foreach (CollectionReference col in db.ListRootCollectionsAsync())
                rootCollections.Add(col.Id);

            _db = db;
            ProjectId = projectId;
            return new ConnectionInfo(projectId, rootCollections);
        }

        //public async Task<ConnectionInfo> ConnectAsync()
        //{
        //    if (_db != null)
        //        return new ConnectionInfo(ProjectId, new List<string>());

        //    string credentialJson = File.ReadAllText(_credentialJsonPath);
        //    string projectId = ReadProjectId(credentialJson, _credentialJsonPath);

        //    #pragma warning disable CS0618
        //    GoogleCredential credential = GoogleCredential.FromJson(credentialJson);
        //    #pragma warning restore CS0618

        //    if (FirebaseApp.DefaultInstance == null)
        //    {
        //        FirebaseApp.Create(new AppOptions
        //        {
        //            Credential = credential,
        //            ProjectId = projectId,
        //        });
        //    }

        //    //var db = new FirestoreDbBuilder
        //    //{
        //    //    ProjectId = projectId,
        //    //    Credential = credential,
        //    //}.Build();

        //    var db = new FirestoreDbBuilder
        //    {
        //        ProjectId = projectId,
        //        CredentialsPath = _credentialJsonPath,
        //    }.Build();

        //    var rootCollections = new List<string>();
        //    await foreach (CollectionReference col in db.ListRootCollectionsAsync())
        //        rootCollections.Add(col.Id);

        //    _db = db;
        //    ProjectId = projectId;
        //    return new ConnectionInfo(projectId, rootCollections);
        //}

        public void Disconnect()
        {
            _db = null;
            ProjectId = null;
        }

        private static string ReadProjectId(string json, string sourcePath)
        {
            var root = JObject.Parse(json);
            var projectId = root["project_id"] != null ? root["project_id"].Value<string>() : null;
            if (string.IsNullOrEmpty(projectId))
                throw new InvalidOperationException(
                    "Service-account JSON at '" + sourcePath + "' has no 'project_id' field.");
            return projectId;
        }
    }
}
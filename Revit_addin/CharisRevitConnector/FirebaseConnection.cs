using System.Text.Json;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace CharisRevitConnector;

/// <summary>
/// Summary of a successful connection: the resolved project and the root
/// collections found by the authenticated test read.
/// </summary>
internal readonly record struct ConnectionInfo(string ProjectId, IReadOnlyList<string> RootCollections);

/// <summary>
/// Owns the Firestore connection. M1 responsibilities:
///   - load the service-account credential (the "via Admin SDK" entry point),
///   - initialize the Firebase Admin app and a FirestoreDb from that one credential,
///   - run an authenticated test read to prove connectivity.
/// M2 will add the realtime Listen() + ExternalEvent on top of <see cref="Db"/>.
/// </summary>
internal sealed class FirebaseConnection
{
    private FirestoreDb? _db;

    public bool IsConnected => _db is not null;
    public string? ProjectId { get; private set; }

    /// <summary>The live Firestore client, or null when disconnected. Used by M2.</summary>
    public FirestoreDb? Db => _db;

    public async Task<ConnectionInfo> ConnectAsync()
    {
        if (_db is not null)
            return new ConnectionInfo(ProjectId!, Array.Empty<string>());

        string credentialPath = CredentialLocator.Resolve()
            ?? throw new FileNotFoundException(CredentialLocator.NotFoundMessage());

        // Read the key once; reuse the text for both the project id and the
        // credential.
        string credentialJson = await File.ReadAllTextAsync(credentialPath);
        string projectId = ReadProjectId(credentialJson, credentialPath);

        // The whole GoogleCredential.From* family is marked obsolete in favour
        // of CredentialFactory, but that API's IGoogleCredential interface is
        // internal in the Google.Apis.Auth version pulled transitively by
        // Google.Cloud.Firestore 4.2.0 — so there is no public, non-deprecated
        // replacement here yet. The cited risk is path/stream injection from
        // untrusted input; our credential comes from an env var or a fixed
        // per-user file we provision, so the warning is safely suppressed.
        // Revisit when a newer Google.Apis.Auth exposes CredentialFactory.
#pragma warning disable CS0618 // Type or member is obsolete
        GoogleCredential credential = GoogleCredential.FromJson(credentialJson);
#pragma warning restore CS0618

        // Initialize the Admin SDK app from the same credential. This is the
        // "stream via Firebase Admin SDK" requirement: one service-account
        // credential shared between the Admin app and the Firestore client.
        if (FirebaseApp.DefaultInstance is null)
        {
            FirebaseApp.Create(new AppOptions
            {
                Credential = credential,
                ProjectId = projectId,
            });
        }

        var db = new FirestoreDbBuilder
        {
            ProjectId = projectId,
            Credential = credential,
        }.Build();

        // Authenticated test read: list root collections. Needs no specific
        // data to exist, so it works against a brand-new Firestore project.
        var rootCollections = new List<string>();
        await foreach (CollectionReference collection in db.ListRootCollectionsAsync())
            rootCollections.Add(collection.Id);

        _db = db;
        ProjectId = projectId;
        return new ConnectionInfo(projectId, rootCollections);
    }

    public void Disconnect()
    {
        // M1: drop the client reference. (M2 will also stop the listener here.)
        // FirebaseApp.DefaultInstance is intentionally left alive so a later
        // reconnect can reuse it without re-creating the Admin app.
        _db = null;
        ProjectId = null;
    }

    private static string ReadProjectId(string json, string sourcePath)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("project_id", out JsonElement pid)
            && pid.GetString() is { Length: > 0 } id)
        {
            return id;
        }

        throw new InvalidOperationException(
            $"Service-account JSON at '{sourcePath}' has no 'project_id' field — "
            + "is this a valid Firebase service-account key?");
    }
}

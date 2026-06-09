using System.Reflection;
using System.Text.Json;

namespace SixCharis.RhinoReviewInterop.Firebase;

public static class FirebaseOptionsLoader
{
    private const string ConfigFileName = "firebase.local.json";

    public static FirebaseOptions LoadForRealtimeDatabase()
    {
        var options = Load();
        options.ValidateRealtimeDatabase();
        return options;
    }

    public static FirestoreConnectionOptions LoadForFirestore()
    {
        var options = Load();
        options.ValidateFirestore();

        var serviceAccountPath = ResolveServiceAccountPath(options);
        var credentialsJson = File.ReadAllText(serviceAccountPath);
        var projectId = string.IsNullOrWhiteSpace(options.ProjectId)
            ? FirestoreCredentialReader.ReadProjectId(credentialsJson)
            : options.ProjectId;

        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException("Firebase config is missing projectId and the service account file does not contain project_id.");
        }

        return new FirestoreConnectionOptions(
            projectId,
            options.ModelId,
            options.FirestoreCollection,
            ResolveFirestoreDocumentPath(options),
            serviceAccountPath,
            credentialsJson);
    }

    private static string ResolveFirestoreDocumentPath(FirebaseOptions options)
    {
        var path = string.IsNullOrWhiteSpace(options.FirestoreDocumentPath)
            ? "rhinoReviewV2/{modelId}"
            : options.FirestoreDocumentPath;

        path = path.Replace("{modelId}", options.ModelId, StringComparison.OrdinalIgnoreCase)
            .Replace("\\", "/", StringComparison.Ordinal);

        var segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0 || segments.Length % 2 != 0)
        {
            throw new InvalidOperationException(
                $"Firestore document path must point to a document and contain an even number of path segments: {path}");
        }

        return string.Join("/", segments);
    }

    private static FirebaseOptions Load()
    {
        var configPath = FindConfigPath();
        if (configPath is null)
        {
            throw new FileNotFoundException(
                $"Firebase config file was not found. Create {ConfigFileName} beside the loaded .rhp or in the plugin project folder.");
        }

        var json = File.ReadAllText(configPath);
        var options = JsonSerializer.Deserialize<FirebaseOptions>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (options is null)
        {
            throw new InvalidOperationException($"Firebase config file is empty or invalid: {configPath}");
        }

        return options;
    }

    private static string ResolveServiceAccountPath(FirebaseOptions options)
    {
        var explicitPath = options.ServiceAccountKeyPath;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            foreach (var directory in CandidateDirectories())
            {
                var path = Path.IsPathRooted(explicitPath)
                    ? explicitPath
                    : Path.Combine(directory, explicitPath);

                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        foreach (var directory in CandidateDirectories())
        {
            var match = Directory
                .EnumerateFiles(directory, "*firebase-adminsdk*.json", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(directory, "serviceAccount*.json", SearchOption.TopDirectoryOnly))
                .Concat(Directory.EnumerateFiles(directory, "service-account*.json", SearchOption.TopDirectoryOnly))
                .FirstOrDefault();

            if (match is not null)
            {
                return match;
            }
        }

        throw new FileNotFoundException(
            "Firestore service account key was not found. Place the ignored *firebase-adminsdk*.json file beside firebase.local.json or beside the loaded .rhp.");
    }

    private static string? FindConfigPath()
    {
        foreach (var directory in CandidateDirectories())
        {
            var path = Path.Combine(directory, ConfigFileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            yield return assemblyDirectory;
        }

        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();
    }
}

public sealed record FirestoreConnectionOptions(
    string ProjectId,
    string ModelId,
    string FirestoreCollection,
    string FirestoreDocumentPath,
    string ServiceAccountKeyPath,
    string CredentialsJson);

internal static class FirestoreCredentialReader
{
    public static string ReadProjectId(string credentialsJson)
    {
        using var document = JsonDocument.Parse(credentialsJson);
        return document.RootElement.TryGetProperty("project_id", out var projectId)
            ? projectId.GetString() ?? string.Empty
            : string.Empty;
    }
}

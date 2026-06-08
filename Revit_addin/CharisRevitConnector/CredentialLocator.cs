namespace CharisRevitConnector;

/// <summary>
/// Finds the Firebase service-account JSON without ever baking it into the
/// assembly or the repo. Resolution order (first existing file wins):
///   1. CHARIS_FIREBASE_CREDENTIALS env var (explicit full path)
///   2. GOOGLE_APPLICATION_CREDENTIALS env var (Google's standard)
///   3. %AppData%\Charis\serviceAccount.json (per-user default)
/// </summary>
internal static class CredentialLocator
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Charis",
        "serviceAccount.json");

    public static string? Resolve()
    {
        string?[] candidates =
        {
            Environment.GetEnvironmentVariable("CHARIS_FIREBASE_CREDENTIALS"),
            Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS"),
            DefaultPath,
        };

        foreach (string? path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return path;
        }

        return null;
    }

    public static string NotFoundMessage() =>
        "No Firebase service-account JSON found.\n\n"
        + "Provide it via one of these (checked in order):\n"
        + "  1. CHARIS_FIREBASE_CREDENTIALS  (env var: full path to the .json)\n"
        + "  2. GOOGLE_APPLICATION_CREDENTIALS  (env var)\n"
        + $"  3. {DefaultPath}\n\n"
        + "Get the key from the Firebase console:\n"
        + "Project settings -> Service accounts -> Generate new private key.\n\n"
        + "Treat it as a secret — never commit it to git.";
}

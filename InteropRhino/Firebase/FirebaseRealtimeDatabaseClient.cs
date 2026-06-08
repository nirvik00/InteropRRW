using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixCharis.RhinoReviewInterop.Extraction;
using SixCharis.RhinoReviewInterop.Schema;

namespace SixCharis.RhinoReviewInterop.Firebase;

public sealed class FirebaseRealtimeDatabaseClient
{
    private static readonly HttpClient HttpClient = new();
    private readonly FirebaseOptions _options;

    public FirebaseRealtimeDatabaseClient(FirebaseOptions options)
    {
        _options = options;
    }

    public async Task<FirebasePushResult> PushLatestAsync(InteropPayload payload, CancellationToken cancellationToken = default)
    {
        var idToken = await SignInAnonymouslyAsync(cancellationToken);
        var databasePath = $"rhinoReview/{_options.ModelId}/latest";
        var url = BuildRealtimeDatabaseUrl(databasePath, idToken);
        var json = JsonSerializer.Serialize(payload, JsonOptions.Pretty);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await HttpClient.PutAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, "Realtime Database write", cancellationToken);

        return new FirebasePushResult(_options.ModelId, databasePath);
    }

    private async Task<string> SignInAnonymouslyAsync(CancellationToken cancellationToken)
    {
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={Uri.EscapeDataString(_options.ApiKey)}";
        var requestBody = JsonSerializer.Serialize(new
        {
            returnSecureToken = true
        });

        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync(url, content, cancellationToken);
        var responseBody = await EnsureSuccessAsync(response, "Firebase anonymous sign-in", cancellationToken);
        var authResponse = JsonSerializer.Deserialize<FirebaseAuthResponse>(responseBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (authResponse is null || string.IsNullOrWhiteSpace(authResponse.IdToken))
        {
            throw new InvalidOperationException("Firebase anonymous sign-in did not return an ID token.");
        }

        return authResponse.IdToken;
    }

    private Uri BuildRealtimeDatabaseUrl(string databasePath, string idToken)
    {
        var baseUrl = _options.DatabaseUrl.TrimEnd('/');
        var path = string.Join("/", databasePath.Split('/').Select(Uri.EscapeDataString));
        var url = $"{baseUrl}/{path}.json?auth={Uri.EscapeDataString(idToken)}";
        return new Uri(url, UriKind.Absolute);
    }

    private static async Task<string> EnsureSuccessAsync(
        HttpResponseMessage response,
        string operationName,
        CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return responseBody;
        }

        var cleanBody = string.IsNullOrWhiteSpace(responseBody) ? response.ReasonPhrase : responseBody;
        throw new InvalidOperationException(
            $"{operationName} failed with HTTP {(int)response.StatusCode}: {cleanBody}");
    }

    private sealed class FirebaseAuthResponse
    {
        [JsonPropertyName("idToken")]
        public string? IdToken { get; init; }
    }
}

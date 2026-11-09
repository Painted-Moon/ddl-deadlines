using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DDL.Services;

/// A problem talking to GitHub, already phrased for the user.
public sealed class GistException(string message) : Exception(message);

/// Keeps the calendar in a secret GitHub gist (unlisted; readable by anyone who has the address).
public static class GistPublisher
{
    public const string FileName = "ddl-deadlines.ics";
    private const string Description = "DDL deadlines (calendar for Outlook)";

    /// GitHub page that creates a classic token limited to gists.
    public const string NewTokenUrl = "https://github.com/settings/tokens/new?scopes=gist&description=DDL%20calendar";

    private static readonly HttpClient Http = CreateClient();

    /// Always serves the latest version of the file; this is what Outlook subscribes to.
    public static string RawUrl(string owner, string gistId) =>
        $"https://gist.githubusercontent.com/{owner}/{gistId}/raw/{FileName}";

    /// Checks the token, creates a new secret gist with the calendar, and returns (owner, gist id).
    public static async Task<(string Owner, string GistId)> CreateAsync(string token, string content, CancellationToken cancel)
    {
        using var create = Request(HttpMethod.Post, "gists", token, new
        {
            description = Description,
            @public = false,
            files = new Dictionary<string, object> { [FileName] = new { content } },
        });
        using var response = await Send(create, "create the gist", cancel);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var root = json.RootElement;
        return (root.GetProperty("owner").GetProperty("login").GetString()!, root.GetProperty("id").GetString()!);
    }

    public static async Task UpdateAsync(string token, string gistId, string content, CancellationToken cancel)
    {
        using var update = Request(HttpMethod.Patch, $"gists/{gistId}", token, new
        {
            files = new Dictionary<string, object> { [FileName] = new { content } },
        });
        using var _ = await Send(update, "update the gist", cancel);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object body) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(body),
        };

    private static async Task<HttpResponseMessage> Send(HttpRequestMessage request, string action, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancel);
        }
        catch (HttpRequestException ex)
        {
            throw new GistException($"Couldn't reach GitHub to {action} ({ex.Message}). Check your internet connection.");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new GistException($"GitHub took too long to {action}. Check your internet connection.");
        }

        if (response.IsSuccessStatusCode) return response;
        var code = response.StatusCode;
        response.Dispose();
        throw new GistException(code switch
        {
            HttpStatusCode.Unauthorized => "GitHub didn't accept the token. It may be mistyped or expired; create a new one.",
            HttpStatusCode.Forbidden => "This token isn't allowed to manage gists. Create one with the “gist” box ticked.",
            HttpStatusCode.NotFound => "The gist no longer exists on GitHub. Disconnect and connect again.",
            _ => $"GitHub couldn't {action} (error {(int)code}).",
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DDL-deadlines-app/0.3");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}

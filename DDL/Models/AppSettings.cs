using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DDL.Models;

/// User preferences, saved as settings.json next to tasks.json.
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly byte[] TokenEntropy = Encoding.UTF8.GetBytes("DDL-github-token");
    private readonly object _saveLock = new();
    private string _file = "";

    public bool CalendarFeedEnabled { get; set; }
    public bool CalendarFeedIncludeCompleted { get; set; }
    public string? CalendarFeedPath { get; set; }          // local copy; null = default location
    public bool TrayHintShown { get; set; }                // "still running in the tray" shown once

    // Secret GitHub gist that Outlook subscribes to.
    public string? GistOwner { get; set; }
    public string? GistId { get; set; }
    public string? GistContentHash { get; set; }           // what was last uploaded
    public string? GitHubTokenProtected { get; set; }      // encrypted for this Windows user only

    public static AppSettings Load(string folder)
    {
        var file = Path.Combine(folder, "settings.json");
        AppSettings settings;
        try
        {
            settings = File.Exists(file)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOptions) ?? new()
                : new();
        }
        catch (JsonException)
        {
            settings = new();   // unreadable settings: fall back to defaults
        }
        settings._file = file;
        return settings;
    }

    /// Safe to call from any thread.
    public void Save()
    {
        lock (_saveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, _file, overwrite: true);
        }
    }

    public string? GetGitHubToken()
    {
        if (GitHubTokenProtected is null) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(GitHubTokenProtected), TokenEntropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;   // made by another Windows user or damaged: ask to reconnect
        }
    }

    public void SetGitHubToken(string? token) =>
        GitHubTokenProtected = token is null
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), TokenEntropy,
                DataProtectionScope.CurrentUser));
}

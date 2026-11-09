using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DDL.Models;

namespace DDL.Services;

/// Publishes the deadlines as an iCalendar (.ics) calendar: a local copy, plus a secret GitHub gist
/// that Outlook subscribes to. Outlook's servers fetch the gist every few hours.
public sealed class CalendarFeed(AppSettings settings, string defaultPath)
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly object _uploadLock = new();
    private string? _pendingUpload;
    private bool _uploading;

    public string FilePath => settings.CalendarFeedPath ?? defaultPath;
    public bool IsConnected => settings.GistId is not null && settings.GistOwner is not null && settings.GitHubTokenProtected is not null;
    public string? SubscribeUrl => IsConnected ? GistPublisher.RawUrl(settings.GistOwner!, settings.GistId!) : null;
    public string? Owner => settings.GistOwner;

    public int LastCount { get; private set; }
    public string? LastError { get; private set; }          // local file
    public string? LastUploadError { get; private set; }    // GitHub
    public DateTime? LastUploaded { get; private set; }
    public bool IsUploading { get { lock (_uploadLock) return _uploading; } }

    /// Raised (on a background thread) when an upload finishes, successfully or not.
    public event Action? UploadFinished;

    /// Default location of the local copy: Documents\DDL.
    public static string DefaultPathFor(bool testMode, string dataFolder) =>
        testMode
            ? Path.Combine(dataFolder, "DDL deadlines.ics")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DDL", "DDL deadlines.ics");

    /// Writes the local copy and, when the content changed, uploads it. Never throws.
    public void Publish(IEnumerable<DdlTask> tasks)
    {
        if (!settings.CalendarFeedEnabled) return;
        var included = Included(tasks).ToList();
        var ics = BuildIcs(included);
        LastCount = included.Count;

        try
        {
            var path = FilePath;
            if (!File.Exists(path) || File.ReadAllText(path, Utf8NoBom) != ics)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllText(temp, ics, Utf8NoBom);
                File.Move(temp, path, overwrite: true);
            }
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        if (IsConnected && Hash(ics) != settings.GistContentHash) QueueUpload(ics);
    }

    /// One-time setup: creates the secret gist with the current calendar and remembers the token.
    public async Task ConnectAsync(string token, IEnumerable<DdlTask> tasks, CancellationToken cancel)
    {
        var included = Included(tasks).ToList();
        var ics = BuildIcs(included);
        var (owner, gistId) = await GistPublisher.CreateAsync(token, ics, cancel);

        settings.SetGitHubToken(token);
        settings.GistOwner = owner;
        settings.GistId = gistId;
        settings.GistContentHash = Hash(ics);
        settings.CalendarFeedEnabled = true;
        settings.Save();
        LastCount = included.Count;
        LastUploaded = DateTime.Now;
        LastUploadError = null;
        Publish(tasks);   // local copy
    }

    /// Forgets the token and gist (the gist itself stays on GitHub until the user deletes it).
    public void Disconnect()
    {
        settings.SetGitHubToken(null);
        settings.GistOwner = null;
        settings.GistId = null;
        settings.GistContentHash = null;
        settings.Save();
        LastUploadError = null;
    }

    /// Gives a pending upload a moment to finish (used when the app closes).
    public void WaitForUploads(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (IsUploading && DateTime.UtcNow < until) Thread.Sleep(50);
    }

    // Only the newest content matters: uploads run one at a time and skip superseded versions.
    private void QueueUpload(string ics)
    {
        lock (_uploadLock)
        {
            _pendingUpload = ics;
            if (_uploading) return;
            _uploading = true;
        }
        _ = Task.Run(UploadLoopAsync);
    }

    private async Task UploadLoopAsync()
    {
        while (true)
        {
            string content;
            lock (_uploadLock)
            {
                if (_pendingUpload is null)
                {
                    _uploading = false;
                    return;
                }
                content = _pendingUpload;
                _pendingUpload = null;
            }

            try
            {
                var token = settings.GetGitHubToken()
                    ?? throw new GistException("The saved GitHub token can't be read. Disconnect and connect again.");
                await GistPublisher.UpdateAsync(token, settings.GistId!, content, CancellationToken.None);
                settings.GistContentHash = Hash(content);
                settings.Save();
                LastUploaded = DateTime.Now;
                LastUploadError = null;
            }
            catch (Exception ex)
            {
                LastUploadError = ex is GistException ? ex.Message : $"Upload failed: {ex.Message}";
            }
            UploadFinished?.Invoke();
        }
    }

    private IEnumerable<DdlTask> Included(IEnumerable<DdlTask> tasks) =>
        tasks.Where(t => settings.CalendarFeedIncludeCompleted || !t.IsCompleted).OrderBy(t => t.DueDate);

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// RFC 5545 calendar: one short event per deadline, starting at the due time. Titles only (no notes).
    public static string BuildIcs(IEnumerable<DdlTask> tasks)
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(Fold(text)).Append("\r\n");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//DDL//Deadlines//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("X-WR-CALNAME:DDL deadlines");
        Line("X-WR-CALDESC:Deadlines from the DDL app");
        Line("REFRESH-INTERVAL;VALUE=DURATION:PT1H");
        Line("X-PUBLISHED-TTL:PT1H");

        foreach (var task in tasks)
        {
            var start = task.DueDate;
            var nextMidnight = start.Date.AddDays(1);
            var end = start.AddMinutes(15) < nextMidnight ? start.AddMinutes(15) : nextMidnight;   // stay on one day

            Line("BEGIN:VEVENT");
            Line($"UID:{task.Id:N}@ddl");
            Line($"DTSTAMP:{Utc(task.CreatedAt)}");
            Line($"DTSTART:{Utc(start)}");
            Line($"DTEND:{Utc(end)}");
            Line($"SUMMARY:{Escape(task.IsCompleted ? $"Done: {task.Title}" : task.Title)}");
            Line("DESCRIPTION:Deadline from DDL");
            Line("CATEGORIES:Deadline");
            Line("TRANSP:TRANSPARENT");   // don't show you as busy
            Line("END:VEVENT");
        }

        Line("END:VCALENDAR");
        return sb.ToString();
    }

    private static string Utc(DateTime local) =>
        local.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
        .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "");

    /// Lines longer than 75 bytes continue on the next line after a space (never splitting a character).
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        var count = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (count + rune.Utf8SequenceLength > 75)
            {
                sb.Append("\r\n ");
                count = 1;
            }
            sb.Append(rune.ToString());
            count += rune.Utf8SequenceLength;
        }
        return sb.ToString();
    }
}

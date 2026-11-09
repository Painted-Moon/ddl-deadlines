using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDL.Models;

namespace DDL.Services;

/// Keeps all tasks in one JSON file (default: %LOCALAPPDATA%\DDL\tasks.json).
public sealed class TaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly List<DdlTask> _tasks = [];

    public TaskStore(string path)
    {
        _path = path;
        Load();
    }

    /// Set DDL_DATA_DIR to use another folder (handy for testing).
    public static bool IsCustomLocation => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DDL_DATA_DIR"));

    public static string DefaultPath
    {
        get
        {
            var dir = IsCustomLocation
                ? Environment.GetEnvironmentVariable("DDL_DATA_DIR")!
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DDL");
            return Path.Combine(dir, "tasks.json");
        }
    }

    public IReadOnlyList<DdlTask> Tasks => _tasks;
    public string FolderPath => Path.GetDirectoryName(_path)!;

    /// Raised after every successful save (used to refresh the calendar file).
    public event Action? Saved;

    public DdlTask? Find(Guid id) => _tasks.FirstOrDefault(t => t.Id == id);

    public void Add(DdlTask task)
    {
        _tasks.Add(task);
        Save();
    }

    public void Delete(Guid id)
    {
        if (_tasks.RemoveAll(t => t.Id == id) > 0) Save();
    }

    /// Call after changing a task in place.
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_tasks, JsonOptions));
        File.Move(temp, _path, overwrite: true);   // never leaves a half-written file
        Saved?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<DdlTask>>(File.ReadAllText(_path), JsonOptions);
            if (loaded is not null) _tasks.AddRange(loaded);
        }
        catch (JsonException)
        {
            // Keep the unreadable file for recovery and start empty.
            File.Move(_path, $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
    }
}

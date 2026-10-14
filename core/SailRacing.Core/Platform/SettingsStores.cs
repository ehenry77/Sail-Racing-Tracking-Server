using System.Text.Json;

namespace SailRacing.Services;

/// <summary>Keeps settings in memory only — the default until an app installs its own store, and what tests use.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = new();

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public void Set(string key, string? value)
    {
        if (value is null) _values.Remove(key);
        else _values[key] = value;
    }
}

/// <summary>Settings in a small JSON file (the desktop app's equivalent of MAUI Preferences).</summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string> _values;

    public JsonFileSettingsStore(string path)
    {
        _path = path;
        _values = Load();
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch
        {
            return new(); // a damaged file means defaults, not a failed start
        }
    }

    public string? Get(string key)
    {
        lock (_gate) return _values.TryGetValue(key, out var v) ? v : null;
    }

    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            if (value is null) _values.Remove(key);
            else _values[key] = value;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Write to a temp file and move it over, so a crash mid-write can't leave half a settings file.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);
        }
    }
}

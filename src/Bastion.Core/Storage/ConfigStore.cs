using System.Text.Json;
using System.Text.Json.Serialization;
using Bastion.Core.Models;

namespace Bastion.Core.Storage;

/// <summary>
/// Loads and atomically persists <see cref="BastionConfig"/> to config.json.
/// The SYSTEM service is the authoritative writer (the file is ACL'd so only
/// SYSTEM/Administrators can write); user-context components load it read-only
/// and request mutations over the service pipe. All access is serialized.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _backupPath;

    public ConfigStore(string? path = null)
    {
        _path = path ?? BastionPaths.ConfigFile;
        _backupPath = BastionPaths.ConfigBackupFile;
    }

    public string Path => _path;

    public bool Exists => File.Exists(_path);

    /// <summary>
    /// Reads the configuration. On a corrupt primary file, falls back to the
    /// last-known-good backup. If neither is readable, returns a fresh,
    /// unconfigured document (fail-safe: no apps become unprotected silently —
    /// the service treats an unreadable config as "protect nothing new" but
    /// keeps existing IFEO hooks until it can reconcile).
    /// </summary>
    public BastionConfig Load()
    {
        lock (_gate)
        {
            var cfg = TryReadFile(_path) ?? TryReadFile(_backupPath);
            return cfg ?? new BastionConfig();
        }
    }

    /// <summary>True when the primary file exists but cannot be parsed.</summary>
    public bool IsPrimaryCorrupt()
    {
        lock (_gate)
        {
            return File.Exists(_path) && TryReadFile(_path) is null;
        }
    }

    public void Save(BastionConfig config)
    {
        lock (_gate)
        {
            BastionPaths.EnsureDirectories();
            var json = JsonSerializer.Serialize(config, JsonOptions);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);

            // Keep a rolling backup of the previous good file before replacing.
            if (File.Exists(_path))
            {
                try { File.Copy(_path, _backupPath, overwrite: true); } catch { /* best effort */ }
            }

            // Atomic replace where possible.
            if (File.Exists(_path))
                File.Replace(tmp, _path, null);
            else
                File.Move(tmp, _path);
        }
    }

    /// <summary>Load, mutate under the lock, and persist in one operation.</summary>
    public BastionConfig Update(Action<BastionConfig> mutate)
    {
        lock (_gate)
        {
            var cfg = TryReadFile(_path) ?? TryReadFile(_backupPath) ?? new BastionConfig();
            mutate(cfg);
            SaveNoLock(cfg);
            return cfg;
        }
    }

    private void SaveNoLock(BastionConfig config)
    {
        BastionPaths.EnsureDirectories();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_path))
        {
            try { File.Copy(_path, _backupPath, overwrite: true); } catch { }
            File.Replace(tmp, _path, null);
        }
        else
        {
            File.Move(tmp, _path);
        }
    }

    private static BastionConfig? TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<BastionConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

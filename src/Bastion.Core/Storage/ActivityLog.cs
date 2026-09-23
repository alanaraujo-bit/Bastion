using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bastion.Core.Models;

namespace Bastion.Core.Storage;

/// <summary>
/// Append-only local activity history stored as JSON lines. Terse by design;
/// never contains secrets. Writing is best-effort and never throws into
/// callers on the security path.
/// </summary>
public sealed class ActivityLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly string _path;

    public ActivityLog(string? path = null) => _path = path ?? BastionPaths.ActivityLogFile;

    public void Append(ActivityEvent ev)
    {
        try
        {
            lock (_gate)
            {
                BastionPaths.EnsureDirectories();
                var line = JsonSerializer.Serialize(ev, JsonOptions);
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* history is non-critical; never break the security path */ }
    }

    public IReadOnlyList<ActivityEvent> ReadRecent(int max = 500)
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return Array.Empty<ActivityEvent>();
                var lines = File.ReadAllLines(_path);
                var result = new List<ActivityEvent>(Math.Min(max, lines.Length));
                for (int i = lines.Length - 1; i >= 0 && result.Count < max; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    try
                    {
                        var ev = JsonSerializer.Deserialize<ActivityEvent>(lines[i], JsonOptions);
                        if (ev is not null) result.Add(ev);
                    }
                    catch { /* skip malformed line */ }
                }
                return result;
            }
        }
        catch
        {
            return Array.Empty<ActivityEvent>();
        }
    }

    public void Clear()
    {
        try
        {
            lock (_gate)
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
        }
        catch { }
    }

    /// <summary>Trim the log to the newest <paramref name="limit"/> entries.</summary>
    public void Trim(int limit)
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return;
                var lines = File.ReadAllLines(_path);
                if (lines.Length <= limit) return;
                var keep = lines.Skip(lines.Length - limit);
                File.WriteAllLines(_path, keep);
            }
        }
        catch { }
    }
}

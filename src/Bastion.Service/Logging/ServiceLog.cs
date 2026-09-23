using Bastion.Core;

namespace Bastion.Service.Logging;

/// <summary>Minimal, resilient file logger for the service. Never throws.</summary>
public static class ServiceLog
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                BastionPaths.EnsureDirectories();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(BastionPaths.ServiceLogFile, line);

                // keep the log from growing unbounded
                var fi = new FileInfo(BastionPaths.ServiceLogFile);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var tail = File.ReadAllLines(BastionPaths.ServiceLogFile);
                    File.WriteAllLines(BastionPaths.ServiceLogFile, tail.Skip(Math.Max(0, tail.Length - 2000)));
                }
            }
        }
        catch { /* logging must never break the service */ }
    }
}

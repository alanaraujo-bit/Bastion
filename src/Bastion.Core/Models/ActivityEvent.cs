namespace Bastion.Core.Models;

public enum ActivityKind
{
    LaunchAuthorized,
    LaunchDenied,
    LaunchCancelled,
    GrantReused,
    Relocked,
    ProtectionEnabled,
    ProtectionDisabled,
    AppAdded,
    AppRemoved,
    ConfigChanged,
    MasterPasswordChanged,
    ServiceStarted,
    ServiceStopped,
    Error,
}

/// <summary>A single, terse local history entry. Never records secrets.</summary>
public sealed class ActivityEvent
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public ActivityKind Kind { get; set; }
    public string? ImageName { get; set; }
    public string? DisplayName { get; set; }
    public string? Detail { get; set; }

    public static ActivityEvent For(ActivityKind kind, ProtectedApp? app = null, string? detail = null) => new()
    {
        Kind = kind,
        ImageName = app?.ImageName,
        DisplayName = app?.DisplayName,
        Detail = detail,
    };
}

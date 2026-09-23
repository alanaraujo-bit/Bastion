namespace Bastion.Core.Models;

/// <summary>How long an authenticated unlock stays valid for a protected app.</summary>
public enum UnlockMode
{
    /// <summary>Require authentication on every launch (most secure).</summary>
    Always = 0,

    /// <summary>Stay unlocked until every instance of the app has exited.</summary>
    UntilAppCloses = 1,

    /// <summary>Stay unlocked for a fixed number of minutes after a successful unlock.</summary>
    ForMinutes = 2,

    /// <summary>Stay unlocked for the remainder of the interactive Windows session.</summary>
    WholeSession = 3,
}

/// <summary>
/// The unlock behaviour for a protected application. A per-app policy may
/// inherit the global default or override it.
/// </summary>
public sealed class UnlockPolicy
{
    public UnlockMode Mode { get; set; } = UnlockMode.Always;

    /// <summary>Grant lifetime in minutes when <see cref="Mode"/> is <see cref="UnlockMode.ForMinutes"/>.</summary>
    public int Minutes { get; set; } = 5;

    /// <summary>Re-lock every protected app when the Windows session locks (Win+L / lock screen).</summary>
    public bool RelockOnWorkstationLock { get; set; } = true;

    /// <summary>Re-lock after the machine resumes from sleep/hibernate.</summary>
    public bool RelockOnResume { get; set; } = true;

    public UnlockPolicy Clone() => new()
    {
        Mode = Mode,
        Minutes = Minutes,
        RelockOnWorkstationLock = RelockOnWorkstationLock,
        RelockOnResume = RelockOnResume,
    };

    public static UnlockPolicy SecureDefault() => new()
    {
        Mode = UnlockMode.UntilAppCloses,
        Minutes = 5,
        RelockOnWorkstationLock = true,
        RelockOnResume = true,
    };
}

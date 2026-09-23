namespace Bastion.Core.Models;

public enum AppTheme { System = 0, Light = 1, Dark = 2 }

public enum AppLanguage { System = 0, PtBr = 1, En = 2 }

/// <summary>
/// The master credential verifier. We never store the password — only the
/// parameters and salt needed to recompute an Argon2id hash and compare it in
/// constant time. <see cref="RecoveryProtectedKey"/> optionally holds the same
/// verifying material sealed behind a recovery answer, enabling reset without
/// weakening the primary path.
/// </summary>
public sealed class MasterCredential
{
    public string Algorithm { get; set; } = "argon2id";
    public int MemoryKiB { get; set; } = 65536;   // 64 MiB
    public int Iterations { get; set; } = 3;
    public int Parallelism { get; set; } = 2;
    public string Salt { get; set; } = "";          // base64
    public string Hash { get; set; } = "";          // base64
    public DateTimeOffset SetUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsConfigured => !string.IsNullOrEmpty(Hash) && !string.IsNullOrEmpty(Salt);
}

/// <summary>
/// Optional recovery: a security question whose answer unseals a recovery blob.
/// The blob is DPAPI-machine protected and contains a recovery token that lets
/// the user set a new master password. Purely local; nothing leaves the machine.
/// </summary>
public sealed class RecoverySettings
{
    public bool Enabled { get; set; }
    public string? Question { get; set; }
    public string AnswerAlgorithm { get; set; } = "argon2id";
    public string? AnswerSalt { get; set; }   // base64
    public string? AnswerHash { get; set; }   // base64
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Keep a local activity history (authorized/denied/config events).</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>Cap on retained activity entries; older ones are trimmed.</summary>
    public int HistoryLimit { get; set; } = 2000;
}

public sealed class SecuritySettings
{
    /// <summary>Require re-authentication before weakening protection (remove app, disable, change settings).</summary>
    public bool ProtectConfigurationChanges { get; set; } = true;

    /// <summary>Allow Windows Hello globally (individual apps may still opt out).</summary>
    public bool WindowsHelloEnabled { get; set; } = true;

    /// <summary>Consecutive failed attempts before a cooldown is imposed on the prompt.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>Cooldown, in seconds, applied after <see cref="MaxFailedAttempts"/> failures.</summary>
    public int LockoutSeconds { get; set; } = 30;
}

/// <summary>Root persisted configuration document. Serialized to config.json.</summary>
public sealed class BastionConfig
{
    public int SchemaVersion { get; set; } = 1;

    public MasterCredential Master { get; set; } = new();
    public RecoverySettings Recovery { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();

    /// <summary>Global default unlock policy applied to apps that do not override it.</summary>
    public UnlockPolicy DefaultPolicy { get; set; } = UnlockPolicy.SecureDefault();

    public List<ProtectedApp> Apps { get; set; } = new();

    /// <summary>Master arm switch. When false, all IFEO hooks are removed and nothing is intercepted.</summary>
    public bool GlobalProtectionEnabled { get; set; } = true;

    /// <summary>Set once onboarding has completed successfully.</summary>
    public bool OnboardingComplete { get; set; }

    public UnlockPolicy EffectivePolicyFor(ProtectedApp app) =>
        app.OverridePolicy ? app.Policy : DefaultPolicy;

    public ProtectedApp? FindByImage(string imageName) =>
        Apps.FirstOrDefault(a => a.Matches(imageName));
}

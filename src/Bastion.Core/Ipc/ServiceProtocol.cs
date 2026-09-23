using System.Text.Json.Serialization;
using Bastion.Core.Models;

namespace Bastion.Core.Ipc;

public static class PipeInfo
{
    /// <summary>Local named pipe the SYSTEM service listens on.</summary>
    public const string PipeName = "Bastion.Service.v1";
}

public enum RequestType
{
    Ping,
    GetStatus,
    /// <summary>Read-only snapshot of config for the UI (no secret material).</summary>
    GetSnapshot,
    /// <summary>Does an active unlock grant already cover this image in this session?</summary>
    CheckGrant,
    /// <summary>Authenticate (password/Hello-derived) and launch a protected target.</summary>
    RequestLaunch,
    /// <summary>Verify the master password and mint a short-lived admin token.</summary>
    AuthenticateAdmin,
    SetMasterPassword,      // onboarding only (when none configured)
    ChangeMasterPassword,   // requires current password
    SetupRecovery,
    ResetViaRecovery,
    AddApp,
    RemoveApp,
    SetAppEnabled,
    UpdateAppPolicy,
    SetGlobalProtection,
    UpdateGeneralSettings,
    UpdateSecuritySettings,
    UpdateDefaultPolicy,
    RelockAll,
    RelockApp,
    GetHistory,
    ClearHistory,
    CompleteOnboarding,
}

public enum ResponseStatus
{
    Ok,
    NeedAuthentication,
    Denied,
    Cooldown,
    NotConfigured,
    AlreadyConfigured,
    BadRequest,
    Error,
}

/// <summary>
/// Single request envelope. Fields are optional and interpreted per
/// <see cref="Type"/>. Secrets (Password, NewPassword, RecoveryAnswer) are only
/// ever transmitted over the local, ACL-restricted pipe and are cleared by the
/// service after use.
/// </summary>
public sealed class ServiceRequest
{
    public RequestType Type { get; set; }

    // Launch / grant
    public string? ImageName { get; set; }
    public string? TargetPath { get; set; }
    public string? Arguments { get; set; }
    public int SessionId { get; set; } = -1;

    // Auth material
    public string? Password { get; set; }
    public string? NewPassword { get; set; }
    /// <summary>Short-lived token from AuthenticateAdmin, authorizing config changes.</summary>
    public string? AdminToken { get; set; }

    // Recovery
    public string? RecoveryQuestion { get; set; }
    public string? RecoveryAnswer { get; set; }

    // App payloads
    public ProtectedApp? App { get; set; }
    public string? AppId { get; set; }
    public bool BoolValue { get; set; }
    public UnlockPolicy? Policy { get; set; }
    public GeneralSettings? General { get; set; }
    public SecuritySettings? Security { get; set; }

    public int HistoryMax { get; set; } = 500;
}

public sealed class ServiceResponse
{
    public ResponseStatus Status { get; set; }
    public string? Message { get; set; }

    /// <summary>Present for AuthenticateAdmin.</summary>
    public string? AdminToken { get; set; }

    /// <summary>Seconds remaining, for Cooldown responses.</summary>
    public int CooldownSeconds { get; set; }

    /// <summary>Present for GetSnapshot.</summary>
    public StatusSnapshot? Snapshot { get; set; }

    /// <summary>Present for GetHistory.</summary>
    public List<ActivityEvent>? History { get; set; }

    public static ServiceResponse Ok(string? msg = null) => new() { Status = ResponseStatus.Ok, Message = msg };
    public static ServiceResponse Fail(ResponseStatus s, string? msg = null) => new() { Status = s, Message = msg };
}

/// <summary>Secret-free projection of the configuration for the UI.</summary>
public sealed class StatusSnapshot
{
    public bool ServiceHealthy { get; set; } = true;
    public bool OnboardingComplete { get; set; }
    public bool MasterConfigured { get; set; }
    public bool RecoveryEnabled { get; set; }
    public bool GlobalProtectionEnabled { get; set; } = true;
    public bool ConfigCorrupt { get; set; }
    public string ServiceVersion { get; set; } = "";

    public GeneralSettings General { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public UnlockPolicy DefaultPolicy { get; set; } = new();
    public List<ProtectedApp> Apps { get; set; } = new();

    /// <summary>Image names that currently have an active unlock grant.</summary>
    public List<string> ActiveGrants { get; set; } = new();
}

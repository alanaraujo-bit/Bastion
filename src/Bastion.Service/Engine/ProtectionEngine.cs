using System.Reflection;
using Bastion.Core;
using Bastion.Core.Ipc;
using Bastion.Core.Models;
using Bastion.Core.Security;
using Bastion.Core.Storage;
using Bastion.Service.Grants;
using Bastion.Service.Ifeo;
using Bastion.Service.Launch;
using Bastion.Service.Logging;
using Bastion.Service.Security;

namespace Bastion.Service.Engine;

/// <summary>
/// The authoritative brain of the SYSTEM service. Every launch decision and
/// every configuration mutation flows through here. Because standard users
/// cannot write the IFEO keys or config.json, and because launches require the
/// privileged pass-through this class performs, protection cannot be disabled
/// or bypassed without authenticating.
/// </summary>
public sealed class ProtectionEngine : IDisposable
{
    private readonly ConfigStore _store;
    private readonly ActivityLog _log;
    private readonly IfeoManager _ifeo = new();
    private readonly SessionLauncher _launcher = new();
    private readonly AuthGate _auth = new();
    private readonly GrantManager _grants;
    private readonly string _gatekeeperPath;
    private readonly object _mutateGate = new();

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public ProtectionEngine(ConfigStore? store = null, ActivityLog? log = null)
    {
        _store = store ?? new ConfigStore();
        _log = log ?? new ActivityLog();
        _grants = new GrantManager(RestoreHook, (img, sess) => _log.Append(
            ActivityEvent.For(ActivityKind.Relocked, new ProtectedApp { ImageName = img }, "re-locked")));
        _gatekeeperPath = Path.Combine(BastionPaths.InstallDir, "Bastion.Gatekeeper.exe");
    }

    private void RestoreHook(string imageName)
    {
        var cfg = _store.Load();
        var app = cfg.FindByImage(imageName);
        if (cfg.GlobalProtectionEnabled && app is { Enabled: true } && IfeoManager.IsProtectable(imageName))
            _ifeo.Register(imageName, _gatekeeperPath);
    }

    /// <summary>
    /// Reconcile IFEO state with configuration. Called at startup and after
    /// mutations so protection survives reboots and is self-healing.
    /// </summary>
    public void Reconcile()
    {
        try
        {
            var cfg = _store.Load();
            foreach (var app in cfg.Apps)
            {
                bool shouldHook = cfg.GlobalProtectionEnabled && app.Enabled &&
                                  IfeoManager.IsProtectable(app.ImageName) &&
                                  !_grants.HasActiveGrant(app.ImageName, -1); // don't re-hook active grants
                bool isHooked = _ifeo.GetDebugger(app.ImageName)?.Contains("Bastion.Gatekeeper", StringComparison.OrdinalIgnoreCase) == true;
                if (shouldHook && !isHooked) _ifeo.Register(app.ImageName, _gatekeeperPath);
                else if (!shouldHook && isHooked) _ifeo.Unregister(app.ImageName);
            }
            ServiceLog.Info($"Reconciled {cfg.Apps.Count} app(s). Global protection: {cfg.GlobalProtectionEnabled}.");
        }
        catch (Exception ex) { ServiceLog.Error("Reconcile failed", ex); }
    }

    public ServiceResponse Handle(ServiceRequest req)
    {
        try
        {
            return req.Type switch
            {
                RequestType.Ping => ServiceResponse.Ok(Version),
                RequestType.GetStatus or RequestType.GetSnapshot => Snapshot(),
                RequestType.CheckGrant => CheckGrant(req),
                RequestType.RequestLaunch => RequestLaunch(req),
                RequestType.AuthenticateAdmin => AuthenticateAdmin(req),
                RequestType.SetMasterPassword => SetMasterPassword(req),
                RequestType.ChangeMasterPassword => ChangeMasterPassword(req),
                RequestType.SetupRecovery => SetupRecovery(req),
                RequestType.ResetViaRecovery => ResetViaRecovery(req),
                RequestType.AddApp => AddApp(req),
                RequestType.RemoveApp => RemoveApp(req),
                RequestType.SetAppEnabled => SetAppEnabled(req),
                RequestType.UpdateAppPolicy => UpdateAppPolicy(req),
                RequestType.SetGlobalProtection => SetGlobalProtection(req),
                RequestType.UpdateGeneralSettings => UpdateGeneralSettings(req),
                RequestType.UpdateSecuritySettings => UpdateSecuritySettings(req),
                RequestType.UpdateDefaultPolicy => UpdateDefaultPolicy(req),
                RequestType.RelockAll => RelockAll(),
                RequestType.RelockApp => RelockApp(req),
                RequestType.GetHistory => GetHistory(req),
                RequestType.ClearHistory => ClearHistory(req),
                RequestType.CompleteOnboarding => CompleteOnboarding(req),
                _ => ServiceResponse.Fail(ResponseStatus.BadRequest, "Unknown request."),
            };
        }
        catch (Exception ex)
        {
            ServiceLog.Error($"Handling {req.Type} failed", ex);
            _log.Append(ActivityEvent.For(ActivityKind.Error, detail: $"{req.Type}: {ex.Message}"));
            return ServiceResponse.Fail(ResponseStatus.Error, "An internal error occurred in the protection service.");
        }
    }

    // ---- launches -------------------------------------------------------

    private ServiceResponse CheckGrant(ServiceRequest req)
    {
        if (string.IsNullOrEmpty(req.ImageName)) return ServiceResponse.Fail(ResponseStatus.BadRequest);
        return _grants.HasActiveGrant(req.ImageName, req.SessionId)
            ? ServiceResponse.Ok("granted")
            : ServiceResponse.Fail(ResponseStatus.NeedAuthentication);
    }

    private ServiceResponse RequestLaunch(ServiceRequest req)
    {
        if (string.IsNullOrEmpty(req.ImageName) || string.IsNullOrEmpty(req.TargetPath))
            return ServiceResponse.Fail(ResponseStatus.BadRequest, "Missing launch target.");

        var cfg = _store.Load();
        var app = cfg.FindByImage(req.ImageName);

        // Not (or no longer) protected: pass the launch straight through.
        if (app is null || !app.Enabled || !cfg.GlobalProtectionEnabled)
            return PassThrough(cfg, app, req, createGrant: false);

        // Already unlocked (race window): just launch.
        if (_grants.HasActiveGrant(app.ImageName, req.SessionId))
            return PassThrough(cfg, app, req, createGrant: true);

        // Locked. Require authentication.
        if (string.IsNullOrEmpty(req.Password))
            return ServiceResponse.Fail(ResponseStatus.NeedAuthentication);

        int cd = _auth.CooldownRemaining("launch:" + app.ImageName);
        if (cd > 0)
            return new ServiceResponse { Status = ResponseStatus.Cooldown, CooldownSeconds = cd, Message = "Too many attempts. Try again shortly." };

        if (!PasswordService.VerifyMaster(cfg.Master, req.Password))
        {
            _auth.RegisterFailure("launch:" + app.ImageName, cfg.Security.MaxFailedAttempts, cfg.Security.LockoutSeconds);
            _log.Append(ActivityEvent.For(ActivityKind.LaunchDenied, app, "incorrect password"));
            int newCd = _auth.CooldownRemaining("launch:" + app.ImageName);
            return newCd > 0
                ? new ServiceResponse { Status = ResponseStatus.Cooldown, CooldownSeconds = newCd, Message = "Too many attempts." }
                : ServiceResponse.Fail(ResponseStatus.Denied, "Incorrect password.");
        }

        _auth.RegisterSuccess("launch:" + app.ImageName);
        return PassThrough(cfg, app, req, createGrant: true);
    }

    private ServiceResponse PassThrough(BastionConfig cfg, ProtectedApp? app, ServiceRequest req, bool createGrant)
    {
        uint session = req.SessionId >= 0 ? (uint)req.SessionId : Interop.NativeMethods.WTSGetActiveConsoleSessionId();
        try
        {
            LaunchResult result = default!;
            if (app is not null && IfeoManager.IsProtectable(app.ImageName))
            {
                // Remove the hook up front so the app and its child processes run
                // freely for the duration of the grant, then launch.
                _ifeo.Unregister(app.ImageName);
            }
            result = _launcher.LaunchAsUser(session, req.TargetPath!, req.Arguments, null);

            if (createGrant && app is not null)
            {
                var policy = cfg.EffectivePolicyFor(app);
                _grants.RegisterLaunch(app, policy, req.SessionId, result.ProcessId, result.ProcessHandle);
                _log.Append(ActivityEvent.For(ActivityKind.LaunchAuthorized, app));
            }
            return ServiceResponse.Ok("launched");
        }
        catch (FileNotFoundException)
        {
            // App moved/uninstalled: restore hook (nothing to protect if gone, but keep intent) and report cleanly.
            if (app is not null) RestoreHook(app.ImageName);
            return ServiceResponse.Fail(ResponseStatus.Error, "The application could not be found. It may have been moved or uninstalled.");
        }
        catch (Exception ex)
        {
            if (app is not null) RestoreHook(app.ImageName);
            ServiceLog.Error("Pass-through launch failed", ex);
            return ServiceResponse.Fail(ResponseStatus.Error, "The application could not be started.");
        }
    }

    // ---- authentication / master password -------------------------------

    private ServiceResponse AuthenticateAdmin(ServiceRequest req)
    {
        var cfg = _store.Load();
        if (!cfg.Master.IsConfigured) return ServiceResponse.Fail(ResponseStatus.NotConfigured);
        int cd = _auth.CooldownRemaining("admin");
        if (cd > 0) return new ServiceResponse { Status = ResponseStatus.Cooldown, CooldownSeconds = cd };

        if (!PasswordService.VerifyMaster(cfg.Master, req.Password ?? ""))
        {
            _auth.RegisterFailure("admin", cfg.Security.MaxFailedAttempts, cfg.Security.LockoutSeconds);
            return ServiceResponse.Fail(ResponseStatus.Denied, "Incorrect password.");
        }
        _auth.RegisterSuccess("admin");
        return new ServiceResponse { Status = ResponseStatus.Ok, AdminToken = _auth.MintToken() };
    }

    private ServiceResponse SetMasterPassword(ServiceRequest req)
    {
        lock (_mutateGate)
        {
            var cfg = _store.Load();
            if (cfg.Master.IsConfigured) return ServiceResponse.Fail(ResponseStatus.AlreadyConfigured, "A master password is already set.");
            if (string.IsNullOrEmpty(req.NewPassword)) return ServiceResponse.Fail(ResponseStatus.BadRequest, "Password required.");
            _store.Update(c => c.Master = PasswordService.CreateMaster(req.NewPassword!));
            _log.Append(ActivityEvent.For(ActivityKind.MasterPasswordChanged, detail: "initial setup"));
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse ChangeMasterPassword(ServiceRequest req)
    {
        lock (_mutateGate)
        {
            var cfg = _store.Load();
            if (!cfg.Master.IsConfigured) return ServiceResponse.Fail(ResponseStatus.NotConfigured);
            if (!PasswordService.VerifyMaster(cfg.Master, req.Password ?? ""))
                return ServiceResponse.Fail(ResponseStatus.Denied, "Current password is incorrect.");
            if (string.IsNullOrEmpty(req.NewPassword)) return ServiceResponse.Fail(ResponseStatus.BadRequest, "New password required.");
            _store.Update(c => c.Master = PasswordService.CreateMaster(req.NewPassword!));
            _log.Append(ActivityEvent.For(ActivityKind.MasterPasswordChanged));
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse SetupRecovery(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        lock (_mutateGate)
        {
            if (string.IsNullOrEmpty(req.RecoveryQuestion) || string.IsNullOrEmpty(req.RecoveryAnswer))
            {
                _store.Update(c => c.Recovery = new RecoverySettings { Enabled = false });
                return ServiceResponse.Ok("recovery disabled");
            }
            var (salt, hash) = PasswordService.CreateRecoveryAnswer(req.RecoveryAnswer!);
            _store.Update(c => c.Recovery = new RecoverySettings
            {
                Enabled = true,
                Question = req.RecoveryQuestion,
                AnswerSalt = salt,
                AnswerHash = hash,
            });
            _log.Append(ActivityEvent.For(ActivityKind.ConfigChanged, detail: "recovery configured"));
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse ResetViaRecovery(ServiceRequest req)
    {
        lock (_mutateGate)
        {
            var cfg = _store.Load();
            if (!cfg.Recovery.Enabled) return ServiceResponse.Fail(ResponseStatus.Denied, "Recovery is not configured.");
            int cd = _auth.CooldownRemaining("recovery");
            if (cd > 0) return new ServiceResponse { Status = ResponseStatus.Cooldown, CooldownSeconds = cd };
            if (!PasswordService.VerifyRecoveryAnswer(cfg.Recovery, req.RecoveryAnswer ?? ""))
            {
                _auth.RegisterFailure("recovery", cfg.Security.MaxFailedAttempts, cfg.Security.LockoutSeconds);
                return ServiceResponse.Fail(ResponseStatus.Denied, "Recovery answer is incorrect.");
            }
            if (string.IsNullOrEmpty(req.NewPassword)) return ServiceResponse.Fail(ResponseStatus.BadRequest, "New password required.");
            _auth.RegisterSuccess("recovery");
            _store.Update(c => c.Master = PasswordService.CreateMaster(req.NewPassword!));
            _log.Append(ActivityEvent.For(ActivityKind.MasterPasswordChanged, detail: "reset via recovery"));
            return ServiceResponse.Ok();
        }
    }

    // ---- app management -------------------------------------------------

    private ServiceResponse AddApp(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        if (req.App is null || string.IsNullOrWhiteSpace(req.App.ImageName))
            return ServiceResponse.Fail(ResponseStatus.BadRequest, "No application specified.");
        var reason = IfeoManager.DenylistReason(req.App.ImageName);
        if (reason is not null) return ServiceResponse.Fail(ResponseStatus.BadRequest, reason);

        lock (_mutateGate)
        {
            var cfg = _store.Update(c =>
            {
                var existing = c.FindByImage(req.App!.ImageName);
                if (existing is null) c.Apps.Add(req.App!);
                else { existing.TargetPath = req.App!.TargetPath; existing.DisplayName = req.App.DisplayName; existing.Enabled = true; existing.IconPath = req.App.IconPath; }
            });
            var app = cfg.FindByImage(req.App.ImageName)!;
            if (cfg.GlobalProtectionEnabled && app.Enabled) _ifeo.Register(app.ImageName, _gatekeeperPath);
            _log.Append(ActivityEvent.For(ActivityKind.AppAdded, app));
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse RemoveApp(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        lock (_mutateGate)
        {
            var cfg = _store.Load();
            var app = cfg.Apps.FirstOrDefault(a => a.Id == req.AppId) ?? (req.ImageName is null ? null : cfg.FindByImage(req.ImageName));
            if (app is null) return ServiceResponse.Fail(ResponseStatus.BadRequest, "Application not found.");
            _ifeo.Unregister(app.ImageName);
            _grants.RelockImage(app.ImageName);
            _store.Update(c => c.Apps.RemoveAll(a => a.Id == app.Id));
            _log.Append(ActivityEvent.For(ActivityKind.AppRemoved, app));
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse SetAppEnabled(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        lock (_mutateGate)
        {
            var cfg = _store.Load();
            var app = cfg.Apps.FirstOrDefault(a => a.Id == req.AppId) ?? (req.ImageName is null ? null : cfg.FindByImage(req.ImageName));
            if (app is null) return ServiceResponse.Fail(ResponseStatus.BadRequest, "Application not found.");
            _store.Update(c => { var a = c.Apps.First(x => x.Id == app.Id); a.Enabled = req.BoolValue; });
            if (req.BoolValue && cfg.GlobalProtectionEnabled) _ifeo.Register(app.ImageName, _gatekeeperPath);
            else { _ifeo.Unregister(app.ImageName); _grants.RelockImage(app.ImageName); }
            _log.Append(ActivityEvent.For(req.BoolValue ? ActivityKind.ProtectionEnabled : ActivityKind.ProtectionDisabled, app));
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse UpdateAppPolicy(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        if (req.Policy is null) return ServiceResponse.Fail(ResponseStatus.BadRequest);
        lock (_mutateGate)
        {
            _store.Update(c =>
            {
                var app = c.Apps.FirstOrDefault(a => a.Id == req.AppId);
                if (app is not null) { app.OverridePolicy = true; app.Policy = req.Policy!; app.AllowWindowsHello = req.BoolValue; }
            });
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse SetGlobalProtection(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        lock (_mutateGate)
        {
            var cfg = _store.Update(c => c.GlobalProtectionEnabled = req.BoolValue);
            if (req.BoolValue) Reconcile();
            else
            {
                foreach (var a in cfg.Apps) _ifeo.Unregister(a.ImageName);
                _grants.RelockAll();
            }
            _log.Append(ActivityEvent.For(req.BoolValue ? ActivityKind.ProtectionEnabled : ActivityKind.ProtectionDisabled, detail: "global"));
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse UpdateGeneralSettings(ServiceRequest req)
    {
        if (req.General is null) return ServiceResponse.Fail(ResponseStatus.BadRequest);
        lock (_mutateGate)
        {
            _store.Update(c => c.General = req.General!);
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse UpdateSecuritySettings(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        if (req.Security is null) return ServiceResponse.Fail(ResponseStatus.BadRequest);
        lock (_mutateGate)
        {
            _store.Update(c => c.Security = req.Security!);
            _log.Append(ActivityEvent.For(ActivityKind.ConfigChanged, detail: "security settings"));
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse UpdateDefaultPolicy(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        if (req.Policy is null) return ServiceResponse.Fail(ResponseStatus.BadRequest);
        lock (_mutateGate)
        {
            _store.Update(c => c.DefaultPolicy = req.Policy!);
            return ServiceResponse.Ok();
        }
    }

    private ServiceResponse RelockAll()
    {
        _grants.RelockAll();
        Reconcile();
        _log.Append(ActivityEvent.For(ActivityKind.Relocked, detail: "all"));
        return ServiceResponse.Ok();
    }

    private ServiceResponse RelockApp(ServiceRequest req)
    {
        if (string.IsNullOrEmpty(req.ImageName)) return ServiceResponse.Fail(ResponseStatus.BadRequest);
        _grants.RelockImage(req.ImageName);
        RestoreHook(req.ImageName);
        return ServiceResponse.Ok();
    }

    private ServiceResponse GetHistory(ServiceRequest req)
    {
        var cfg = _store.Load();
        if (!cfg.General.KeepHistory) return new ServiceResponse { Status = ResponseStatus.Ok, History = new() };
        return new ServiceResponse { Status = ResponseStatus.Ok, History = _log.ReadRecent(req.HistoryMax).ToList() };
    }

    private ServiceResponse ClearHistory(ServiceRequest req)
    {
        var gate = RequireAuthorization(req);
        if (gate is not null) return gate;
        _log.Clear();
        return ServiceResponse.Ok();
    }

    private ServiceResponse CompleteOnboarding(ServiceRequest req)
    {
        lock (_mutateGate)
        {
            _store.Update(c => c.OnboardingComplete = true);
            _log.Append(ActivityEvent.For(ActivityKind.ServiceStarted, detail: "onboarding complete"));
            return ServiceResponse.Ok();
        }
    }

    // ---- helpers --------------------------------------------------------

    /// <summary>
    /// Enforces re-authentication for security-weakening operations. Returns null
    /// when authorized, or a NeedAuthentication/Denied response otherwise.
    /// </summary>
    private ServiceResponse? RequireAuthorization(ServiceRequest req)
    {
        var cfg = _store.Load();
        if (!cfg.Master.IsConfigured) return null;           // bootstrapping: nothing to protect yet
        if (!cfg.Security.ProtectConfigurationChanges) return null;
        if (_auth.ValidateToken(req.AdminToken)) return null;
        if (!string.IsNullOrEmpty(req.Password) && PasswordService.VerifyMaster(cfg.Master, req.Password))
            return null;
        return ServiceResponse.Fail(ResponseStatus.NeedAuthentication, "Authentication is required to change protection settings.");
    }

    private ServiceResponse Snapshot()
    {
        var cfg = _store.Load();
        var snap = new StatusSnapshot
        {
            ServiceHealthy = true,
            ServiceVersion = Version,
            OnboardingComplete = cfg.OnboardingComplete,
            MasterConfigured = cfg.Master.IsConfigured,
            RecoveryEnabled = cfg.Recovery.Enabled,
            GlobalProtectionEnabled = cfg.GlobalProtectionEnabled,
            ConfigCorrupt = _store.IsPrimaryCorrupt(),
            General = cfg.General,
            Security = cfg.Security,
            DefaultPolicy = cfg.DefaultPolicy,
            Apps = cfg.Apps,
            ActiveGrants = _grants.ActiveImageNames().ToList(),
        };
        return new ServiceResponse { Status = ResponseStatus.Ok, Snapshot = snap };
    }

    // ---- lifecycle hooks from the service host --------------------------

    public void OnWorkstationLock()
    {
        var cfg = _store.Load();
        if (cfg.DefaultPolicy.RelockOnWorkstationLock || cfg.Apps.Any(a => a.OverridePolicy && a.Policy.RelockOnWorkstationLock))
            _grants.OnWorkstationLock();
    }

    public void OnResume()
    {
        var cfg = _store.Load();
        if (cfg.DefaultPolicy.RelockOnResume || cfg.Apps.Any(a => a.OverridePolicy && a.Policy.RelockOnResume))
            _grants.OnResume();
    }

    public void OnSessionLogoff(int sessionId) => _grants.EndSession(sessionId);

    public void Dispose() => _grants.Dispose();
}

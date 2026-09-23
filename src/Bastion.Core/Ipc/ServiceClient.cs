using Bastion.Core.Models;

namespace Bastion.Core.Ipc;

/// <summary>
/// Typed, task-based facade over <see cref="PipeClient"/> for the UI and
/// gatekeeper. Every call fails safe: if the service is unreachable the caller
/// receives an Error response rather than an exception.
/// </summary>
public sealed class ServiceClient
{
    public Task<bool> IsReachableAsync() => PipeClient.IsServiceReachableAsync();

    public Task<ServiceResponse> GetSnapshotAsync() =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.GetSnapshot });

    public Task<ServiceResponse> RequestLaunchAsync(string imageName, string targetPath, string? arguments, int sessionId, string? password = null) =>
        PipeClient.SendAsync(new ServiceRequest
        {
            Type = RequestType.RequestLaunch,
            ImageName = imageName,
            TargetPath = targetPath,
            Arguments = arguments,
            SessionId = sessionId,
            Password = password,
        }, timeoutMs: 8000);

    public Task<ServiceResponse> AuthenticateAdminAsync(string password) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.AuthenticateAdmin, Password = password });

    public Task<ServiceResponse> SetMasterPasswordAsync(string newPassword) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.SetMasterPassword, NewPassword = newPassword });

    public Task<ServiceResponse> ChangeMasterPasswordAsync(string current, string newPassword) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.ChangeMasterPassword, Password = current, NewPassword = newPassword });

    public Task<ServiceResponse> SetupRecoveryAsync(string? question, string? answer, string? adminToken) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.SetupRecovery, RecoveryQuestion = question, RecoveryAnswer = answer, AdminToken = adminToken });

    public Task<ServiceResponse> ResetViaRecoveryAsync(string answer, string newPassword) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.ResetViaRecovery, RecoveryAnswer = answer, NewPassword = newPassword });

    public Task<ServiceResponse> AddAppAsync(ProtectedApp app, string? adminToken, string? password = null) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.AddApp, App = app, AdminToken = adminToken, Password = password });

    public Task<ServiceResponse> RemoveAppAsync(string appId, string? adminToken, string? password = null) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.RemoveApp, AppId = appId, AdminToken = adminToken, Password = password });

    public Task<ServiceResponse> SetAppEnabledAsync(string appId, bool enabled, string? adminToken, string? password = null) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.SetAppEnabled, AppId = appId, BoolValue = enabled, AdminToken = adminToken, Password = password });

    public Task<ServiceResponse> UpdateAppPolicyAsync(string appId, UnlockPolicy policy, bool allowHello, string? adminToken) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.UpdateAppPolicy, AppId = appId, Policy = policy, BoolValue = allowHello, AdminToken = adminToken });

    public Task<ServiceResponse> SetGlobalProtectionAsync(bool enabled, string? adminToken, string? password = null) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.SetGlobalProtection, BoolValue = enabled, AdminToken = adminToken, Password = password });

    public Task<ServiceResponse> UpdateGeneralSettingsAsync(GeneralSettings general) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.UpdateGeneralSettings, General = general });

    public Task<ServiceResponse> UpdateSecuritySettingsAsync(SecuritySettings security, string? adminToken) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.UpdateSecuritySettings, Security = security, AdminToken = adminToken });

    public Task<ServiceResponse> UpdateDefaultPolicyAsync(UnlockPolicy policy, string? adminToken) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.UpdateDefaultPolicy, Policy = policy, AdminToken = adminToken });

    public Task<ServiceResponse> RelockAllAsync() =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.RelockAll });

    public Task<ServiceResponse> GetHistoryAsync(int max = 500) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.GetHistory, HistoryMax = max });

    public Task<ServiceResponse> ClearHistoryAsync(string? adminToken) =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.ClearHistory, AdminToken = adminToken });

    public Task<ServiceResponse> CompleteOnboardingAsync() =>
        PipeClient.SendAsync(new ServiceRequest { Type = RequestType.CompleteOnboarding });
}

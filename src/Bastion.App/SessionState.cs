using System.Windows;
using Bastion.Core.Ipc;

namespace Bastion.App;

/// <summary>
/// Process-wide shared state for the UI: the latest snapshot from the service,
/// a short-lived admin token minted after re-authentication, and a helper that
/// transparently re-authenticates when the service demands it.
/// </summary>
public sealed class SessionState
{
    public ServiceClient Client { get; } = new();
    public StatusSnapshot? Snapshot { get; private set; }
    public bool ServiceReachable { get; private set; }
    public string? AdminToken { get; set; }

    public event Action? SnapshotChanged;

    public static SessionState Current { get; } = new();

    public async Task RefreshAsync()
    {
        var resp = await Client.GetSnapshotAsync();
        ServiceReachable = resp.Status == ResponseStatus.Ok && resp.Snapshot is not null;
        if (ServiceReachable) Snapshot = resp.Snapshot;
        SnapshotChanged?.Invoke();
    }

    /// <summary>
    /// Runs a service action that may require an admin token. If the service
    /// responds NeedAuthentication, prompts once for the master password, mints
    /// a token, and retries. Returns the final response (or a Denied/cancelled).
    /// </summary>
    public async Task<ServiceResponse> RunPrivilegedAsync(Window owner, Func<string?, Task<ServiceResponse>> action)
    {
        var resp = await action(AdminToken);
        if (resp.Status != ResponseStatus.NeedAuthentication) return resp;

        var pw = ReAuthDialog.Ask(owner);
        if (pw is null) return ServiceResponse.Fail(ResponseStatus.Denied, "Cancelled.");

        var auth = await Client.AuthenticateAdminAsync(pw);
        if (auth.Status != ResponseStatus.Ok || string.IsNullOrEmpty(auth.AdminToken))
            return auth.Status == ResponseStatus.Cooldown ? auth
                 : ServiceResponse.Fail(ResponseStatus.Denied, auth.Message ?? "Authentication failed.");

        AdminToken = auth.AdminToken;
        return await action(AdminToken);
    }
}

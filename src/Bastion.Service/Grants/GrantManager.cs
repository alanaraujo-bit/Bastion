using System.Collections.Concurrent;
using Microsoft.Win32.SafeHandles;
using Bastion.Core.Models;
using Bastion.Service.Interop;

namespace Bastion.Service.Grants;

/// <summary>
/// Tracks active unlock grants. The governing rule: while a grant is active the
/// IFEO hook for that image is <b>removed</b>, so the running application and
/// its (identically named) child processes execute freely with no per-launch
/// interception. When the grant ends — the process exits, a timer elapses, the
/// session ends, or the workstation locks per policy — the hook is restored and
/// the app is "locked" again.
///
/// Exit detection is event-driven: we wait on the launched process handle rather
/// than polling.
/// </summary>
public sealed class GrantManager : IDisposable
{
    private sealed class TrackedProcess
    {
        public required SafeWaitHandle Handle;
        public required ManualResetEvent Event;
        public required RegisteredWaitHandle Wait;
    }

    private sealed class Grant
    {
        public required string ImageName;
        public required int SessionId;
        public UnlockMode Mode;
        public DateTimeOffset? ExpiryUtc;      // for ForMinutes
        public bool ExtensionRevoked;          // set on workstation lock: fall back to until-close
        public readonly List<TrackedProcess> Processes = new();
        public DateTimeOffset CreatedUtc = DateTimeOffset.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Grant> _grants = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Action<string> _restoreHook;     // imageName -> re-arm IFEO
    private readonly Action<string, int> _onRelock;   // imageName, sessionId
    private readonly Timer _sweep;

    public GrantManager(Action<string> restoreHook, Action<string, int> onRelock)
    {
        _restoreHook = restoreHook;
        _onRelock = onRelock;
        _sweep = new Timer(_ => SweepExpired(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private static string Key(string image, int session) => $"{image.ToLowerInvariant()}|{session}";

    public bool HasActiveGrant(string imageName, int sessionId)
        => _grants.TryGetValue(Key(imageName, sessionId), out var g) && IsActive(g);

    public IReadOnlyList<string> ActiveImageNames()
    {
        lock (_gate)
            return _grants.Values.Where(IsActive).Select(g => g.ImageName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool IsActive(Grant g)
    {
        bool alive = g.Processes.Any(p => !p.Event.WaitOne(0));
        if (alive) return true;
        if (g.ExtensionRevoked) return false;
        return g.Mode switch
        {
            UnlockMode.WholeSession => true,
            UnlockMode.ForMinutes => g.ExpiryUtc is { } e && DateTimeOffset.UtcNow < e,
            _ => false, // Always / UntilAppCloses end once no process is alive
        };
    }

    /// <summary>Registers a launched process under a grant, creating/refreshing it per policy.</summary>
    public void RegisterLaunch(ProtectedApp app, UnlockPolicy policy, int sessionId, int pid, IntPtr processHandle)
    {
        lock (_gate)
        {
            var key = Key(app.ImageName, sessionId);
            if (!_grants.TryGetValue(key, out var g))
            {
                g = new Grant { ImageName = app.ImageName, SessionId = sessionId, Mode = policy.Mode };
                _grants[key] = g;
            }
            g.Mode = policy.Mode;
            g.ExtensionRevoked = false;
            g.CreatedUtc = DateTimeOffset.UtcNow;
            if (policy.Mode == UnlockMode.ForMinutes)
                g.ExpiryUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, policy.Minutes));

            if (processHandle != IntPtr.Zero)
            {
                var safe = new SafeWaitHandle(processHandle, ownsHandle: true);
                var evt = new ManualResetEvent(false) { SafeWaitHandle = safe };
                RegisteredWaitHandle? rwh = null;
                rwh = ThreadPool.RegisterWaitForSingleObject(evt, (_, _) => OnProcessExit(key), null, Timeout.Infinite, executeOnlyOnce: true);
                g.Processes.Add(new TrackedProcess { Handle = safe, Event = evt, Wait = rwh! });
            }
        }
    }

    private void OnProcessExit(string key)
    {
        // A tracked process ended. If the grant is no longer active, end it.
        if (_grants.TryGetValue(key, out var g))
        {
            lock (_gate)
            {
                if (!IsActive(g)) EndGrant(key, g, "application closed");
            }
        }
    }

    private void SweepExpired()
    {
        lock (_gate)
        {
            foreach (var kv in _grants.ToArray())
            {
                if (!IsActive(kv.Value))
                    EndGrant(kv.Key, kv.Value, "unlock expired");
            }
        }
    }

    private void EndGrant(string key, Grant g, string reason)
    {
        if (!_grants.TryRemove(key, out _)) return;
        foreach (var p in g.Processes)
        {
            try { p.Wait.Unregister(null); } catch { }
            try { p.Event.Dispose(); } catch { }
        }
        try { _restoreHook(g.ImageName); } catch { }
        try { _onRelock(g.ImageName, g.SessionId); } catch { }
    }

    /// <summary>Explicit "lock everything now": end every grant and restore all hooks.</summary>
    public void RelockAll()
    {
        lock (_gate)
        {
            foreach (var kv in _grants.ToArray())
                EndGrant(kv.Key, kv.Value, "manual re-lock");
        }
    }

    public void RelockImage(string imageName)
    {
        lock (_gate)
        {
            foreach (var kv in _grants.Where(k => string.Equals(k.Value.ImageName, imageName, StringComparison.OrdinalIgnoreCase)).ToArray())
                EndGrant(kv.Key, kv.Value, "manual re-lock");
        }
    }

    /// <summary>
    /// Workstation locked. Non-destructive: we do not kill running apps (that
    /// would lose the user's work). Instead we revoke any time/session extension
    /// so the app re-locks as soon as it closes; apps not currently running are
    /// locked immediately.
    /// </summary>
    public void OnWorkstationLock()
    {
        lock (_gate)
        {
            foreach (var kv in _grants.ToArray())
            {
                var g = kv.Value;
                if (g.Mode is UnlockMode.WholeSession or UnlockMode.ForMinutes)
                {
                    g.ExtensionRevoked = true;
                    if (!IsActive(g)) EndGrant(kv.Key, g, "workstation locked");
                }
            }
        }
    }

    /// <summary>Resume from sleep/hibernate: same non-destructive re-lock semantics.</summary>
    public void OnResume() => OnWorkstationLock();

    public void EndSession(int sessionId)
    {
        lock (_gate)
        {
            foreach (var kv in _grants.Where(k => k.Value.SessionId == sessionId).ToArray())
                EndGrant(kv.Key, kv.Value, "session ended");
        }
    }

    public void Dispose()
    {
        _sweep.Dispose();
        RelockAll();
    }
}

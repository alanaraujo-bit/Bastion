using System.ServiceProcess;
using Bastion.Service.Engine;
using Bastion.Service.Logging;
using Bastion.Service.Server;

namespace Bastion.Service;

/// <summary>
/// The Windows Service host. Runs as LocalSystem so it can own the HKLM IFEO
/// keys and launch authorized apps into user sessions. Handles session-lock and
/// power-resume notifications to enforce the re-lock policies.
/// </summary>
public sealed class BastionService : ServiceBase
{
    public const string ServiceName_ = "BastionProtection";

    private ProtectionEngine? _engine;
    private PipeServer? _server;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public BastionService()
    {
        ServiceName = ServiceName_;
        CanHandleSessionChangeEvent = true;
        CanHandlePowerEvent = true;
        CanShutdown = true;
        CanStop = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args) => StartCore();

    public void StartCore()
    {
        try
        {
            Core.BastionPaths.EnsureDirectories();
            _engine = new ProtectionEngine();
            _engine.Reconcile();
            _cts = new CancellationTokenSource();
            _server = new PipeServer(_engine);
            _serverTask = _server.RunAsync(_cts.Token);
            ServiceLog.Info("Bastion protection service started.");
        }
        catch (Exception ex)
        {
            ServiceLog.Error("Service failed to start", ex);
            throw;
        }
    }

    protected override void OnStop() => StopCore();
    protected override void OnShutdown() => StopCore();

    public void StopCore()
    {
        try
        {
            _cts?.Cancel();
            try { _serverTask?.Wait(TimeSpan.FromSeconds(3)); } catch { }
            _engine?.Dispose();
            ServiceLog.Info("Bastion protection service stopped.");
        }
        catch (Exception ex) { ServiceLog.Error("Error during stop", ex); }
    }

    protected override void OnSessionChange(SessionChangeDescription change)
    {
        try
        {
            switch (change.Reason)
            {
                case SessionChangeReason.SessionLock:
                    _engine?.OnWorkstationLock();
                    break;
                case SessionChangeReason.SessionLogoff:
                    _engine?.OnSessionLogoff(change.SessionId);
                    break;
            }
        }
        catch (Exception ex) { ServiceLog.Error("Session change handling failed", ex); }
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        try
        {
            if (powerStatus is PowerBroadcastStatus.ResumeSuspend or PowerBroadcastStatus.ResumeAutomatic)
                _engine?.OnResume();
        }
        catch (Exception ex) { ServiceLog.Error("Power event handling failed", ex); }
        return true;
    }
}

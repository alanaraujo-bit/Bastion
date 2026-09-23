using System.ServiceProcess;
using Bastion.Service;
using Bastion.Service.Engine;
using Bastion.Service.Ifeo;
using Bastion.Service.Logging;
using Bastion.Service.Server;

// Uninstall cleanup: strip every IFEO hook we own so protected apps launch
// normally once Bastion is removed. Invoked by the installer before file removal.
if (args.Contains("--cleanup-hooks", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        int n = new IfeoManager().RemoveAllBastionHooks();
        ServiceLog.Info($"Uninstall cleanup removed {n} IFEO hook(s).");
        Console.WriteLine($"Removed {n} protection hook(s).");
    }
    catch (Exception ex) { ServiceLog.Error("Cleanup failed", ex); }
    return;
}

// Entry point. Runs as a Windows Service normally, or in the foreground with
// --console for local diagnostics (must be elevated to touch HKLM / the pipe ACL).
if (args.Contains("--console", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Bastion protection service — console mode. Press Ctrl+C to stop.");
    Bastion.Core.BastionPaths.EnsureDirectories();
    using var engine = new ProtectionEngine();
    engine.Reconcile();
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    var server = new PipeServer(engine);
    ServiceLog.Info("Started in console mode.");
    try { await server.RunAsync(cts.Token); }
    catch (OperationCanceledException) { }
    engine.Dispose();
    return;
}

ServiceBase.Run(new BastionService());

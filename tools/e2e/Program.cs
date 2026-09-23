// Dev-only E2E helper: drives the installed SYSTEM service over its pipe, exactly
// as the real app does, to add a protected app or verify a launch.
using Bastion.Core.Ipc;
using Bastion.Core.Models;

string cmd = args.Length > 0 ? args[0] : "add";
string password = args.Length > 1 ? args[1] : "test1234";

if (cmd == "add")
{
    var app = new ProtectedApp
    {
        ImageName = "charmap.exe",
        DisplayName = "Character Map",
        TargetPath = @"C:\Windows\System32\charmap.exe",
        Enabled = true,
        OverridePolicy = true,
        Policy = new UnlockPolicy { Mode = UnlockMode.Always },
    };
    var resp = await new ServiceClient().AddAppAsync(app, adminToken: null, password: password);
    Console.WriteLine($"AddApp -> {resp.Status} {resp.Message}");
}
else if (cmd == "snapshot")
{
    var resp = await new ServiceClient().GetSnapshotAsync();
    Console.WriteLine($"Snapshot -> {resp.Status}; apps: {string.Join(", ", resp.Snapshot?.Apps.Select(a => a.ImageName) ?? Array.Empty<string>())}");
    Console.WriteLine($"Active grants: {string.Join(", ", resp.Snapshot?.ActiveGrants ?? new())}");
}
else if (cmd == "ping")
{
    Console.WriteLine($"Reachable: {await new ServiceClient().IsReachableAsync()}");
}
else if (cmd == "launch")
{
    // Exactly what the gatekeeper does when you enter the password and click Unlock.
    int session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
    var resp = await new ServiceClient().RequestLaunchAsync(
        "charmap.exe", @"C:\Windows\System32\charmap.exe", null, session, password);
    Console.WriteLine($"RequestLaunch -> {resp.Status} {resp.Message}");
}

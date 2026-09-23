// Dev-only utility (NOT part of the shipped product): seeds a demo configuration
// so the UI can be rendered/reviewed without an elevated service install.
using Bastion.Core.Models;
using Bastion.Core.Security;
using Bastion.Core.Storage;

var store = new ConfigStore();
var lang = args.Contains("--pt") ? AppLanguage.PtBr : args.Contains("--en") ? AppLanguage.En : AppLanguage.System;
var cfg = new BastionConfig
{
    OnboardingComplete = true,
    GlobalProtectionEnabled = true,
    Master = PasswordService.CreateMaster("test1234"),
    General = { Language = lang },
};

void Add(string name, string path, UnlockMode mode, bool enabled = true)
{
    if (!File.Exists(path)) return;
    cfg.Apps.Add(new ProtectedApp
    {
        ImageName = Path.GetFileName(path).ToLowerInvariant(),
        DisplayName = name,
        TargetPath = path,
        Enabled = enabled,
        OverridePolicy = true,
        Policy = new UnlockPolicy { Mode = mode, Minutes = 5 },
    });
}

if (args.Contains("--minimal"))
{
    // Contained end-to-end test: protect only Notepad.
    Add("Notepad", @"C:\Windows\System32\notepad.exe", UnlockMode.Always);
}
else
{
    Add("Microsoft Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", UnlockMode.UntilAppCloses);
    Add("Notepad", @"C:\Windows\System32\notepad.exe", UnlockMode.Always);
    Add("Paint", @"C:\Windows\System32\mspaint.exe", UnlockMode.ForMinutes);
    Add("Calculator", @"C:\Windows\System32\calc.exe", UnlockMode.WholeSession, enabled: false);
}

store.Save(cfg);

var log = new ActivityLog();
log.Append(ActivityEvent.For(ActivityKind.ServiceStarted, detail: "demo seed"));
log.Append(ActivityEvent.For(ActivityKind.AppAdded, cfg.Apps[0]));
log.Append(ActivityEvent.For(ActivityKind.LaunchAuthorized, cfg.Apps[0]));
log.Append(ActivityEvent.For(ActivityKind.LaunchDenied, cfg.Apps.Count > 1 ? cfg.Apps[1] : cfg.Apps[0], "incorrect password"));

Console.WriteLine($"Seeded {cfg.Apps.Count} apps at {store.Path}");

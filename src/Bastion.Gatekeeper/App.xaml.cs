using System.Diagnostics;
using System.IO;
using System.Windows;
using Bastion.Core.Ipc;
using Bastion.Core.Models;
using Bastion.Ui.Localization;
using Bastion.Ui.Services;

namespace Bastion.Gatekeeper;

public partial class App : Application
{
    private readonly ServiceClient _client = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            // Fail closed and quietly: never leave a broken prompt on screen.
            args.Handled = true;
            Shutdown();
        };

#if DEBUG
        if (e.Args.Contains("--preview") || e.Args.Contains("--preview-dark"))
        {
            ThemeManager.Apply(e.Args.Contains("--preview-dark") ? AppTheme.Dark : AppTheme.Light);
            Loc.Current.SetLanguage(e.Args.Contains("--pt") ? AppLanguage.PtBr : AppLanguage.En);
            ShowPrompt("notepad.exe", @"C:\Windows\System32\notepad.exe", "", 1,
                       "Notepad", helloAllowed: true, unavailable: false);
            return;
        }
#endif

        ThemeManager.Apply(AppTheme.System);

        var (target, arguments) = CommandLine.Parse(Environment.CommandLine);
        if (string.IsNullOrWhiteSpace(target))
        {
            Shutdown();
            return;
        }

        _ = RunAsync(target, arguments);
    }

    private async Task RunAsync(string target, string arguments)
    {
        var image = Path.GetFileName(target);
        int session = Process.GetCurrentProcess().SessionId;

        var snapResp = await _client.GetSnapshotAsync();
        if (snapResp.Status != ResponseStatus.Ok || snapResp.Snapshot is null)
        {
            // Protection backend unreachable -> fail closed with a clear message.
            ShowPrompt(image, target, arguments, session, displayName: FriendlyName(target),
                       helloAllowed: false, unavailable: true);
            return;
        }

        var snap = snapResp.Snapshot;
        Loc.Current.SetLanguage(snap.General.Language);
        var app = snap.Apps.FirstOrDefault(a => a.Matches(image));
        bool isProtected = app is { Enabled: true } && snap.GlobalProtectionEnabled;

        if (!isProtected)
        {
            // Not under protection (stale hook, disabled, or globally off): pass straight through.
            await _client.RequestLaunchAsync(image, target, arguments, session);
            Shutdown();
            return;
        }

        if (snap.ActiveGrants.Contains(image, StringComparer.OrdinalIgnoreCase))
        {
            await _client.RequestLaunchAsync(image, target, arguments, session);
            Shutdown();
            return;
        }

        bool helloAllowed = snap.Security.WindowsHelloEnabled && app!.AllowWindowsHello
                            && HelloService.IsEnrolled && await HelloService.IsAvailableAsync();

        ShowPrompt(image, target, arguments, session,
                   displayName: string.IsNullOrWhiteSpace(app!.DisplayName) ? FriendlyName(target) : app.DisplayName,
                   helloAllowed: helloAllowed, unavailable: false);
    }

    private void ShowPrompt(string image, string target, string arguments, int session,
                            string displayName, bool helloAllowed, bool unavailable)
    {
        var window = new PromptWindow(_client, image, target, arguments, session, displayName, helloAllowed, unavailable);
        window.Closed += (_, _) => Shutdown();
        window.Show();
        window.Activate();
    }

    private static string FriendlyName(string path)
    {
        try
        {
            var fvi = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(fvi.FileDescription)) return fvi.FileDescription!.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(path);
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Bastion.Core;
using Bastion.Core.Ipc;
using Bastion.Core.Models;
using Bastion.Ui.Localization;
using Bastion.Ui.Services;

namespace Bastion.App.Pages;

public partial class SettingsPage : UserControl, IPage
{
    private bool _loading;

    public SettingsPage() => InitializeComponent();

    public async void OnShown()
    {
        await SessionState.Current.RefreshAsync();
        LoadValues();
        await RefreshHelloAvailabilityAsync();
    }

    private void LoadValues()
    {
        var snap = SessionState.Current.Snapshot;
        if (snap is null) return;
        _loading = true;

        var g = snap.General;
        StartupToggle.IsChecked = g.StartWithWindows;
        StartMinToggle.IsChecked = g.StartMinimized;
        TrayToggle.IsChecked = g.MinimizeToTray;
        HistoryToggle.IsChecked = g.KeepHistory;
        ThemeCombo.SelectedIndex = (int)g.Theme;
        LangCombo.SelectedIndex = (int)g.Language;

        DefaultModeCombo.SelectedIndex = snap.DefaultPolicy.Mode switch
        {
            UnlockMode.Always => 0, UnlockMode.ForMinutes => 2, UnlockMode.WholeSession => 3, _ => 1,
        };

        ProtectChangesToggle.IsChecked = snap.Security.ProtectConfigurationChanges;
        HelloToggle.IsChecked = snap.Security.WindowsHelloEnabled && HelloService.IsEnrolled;

        SetComboText(AttemptsCombo, "Set_Tries");
        SetComboText(LockoutCombo, "Set_Seconds");
        SelectByTag(AttemptsCombo, snap.Security.MaxFailedAttempts, 1);
        SelectByTag(LockoutCombo, snap.Security.LockoutSeconds, 1);

        RecoveryBtn.Content = snap.RecoveryEnabled ? Loc.S("Common_Manage") : Loc.S("Common_SetUp");
        RecoveryDesc.Text = snap.RecoveryEnabled ? Loc.S("Set_RecoveryOnSub") : Loc.S("Set_RecoverySub");

        VersionText.Text = Loc.F("Set_AboutVersion", snap.ServiceVersion);
        _loading = false;
    }

    private static void SetComboText(ComboBox combo, string key)
    {
        foreach (ComboBoxItem it in combo.Items)
            it.Content = Loc.F(key, it.Tag?.ToString() ?? "");
    }

    private static void SelectByTag(ComboBox combo, int value, int fallbackIndex)
    {
        foreach (ComboBoxItem it in combo.Items)
            if (it.Tag?.ToString() == value.ToString()) { it.IsSelected = true; return; }
        combo.SelectedIndex = fallbackIndex;
    }

    private async Task RefreshHelloAvailabilityAsync()
    {
        bool available = await HelloService.IsAvailableAsync();
        if (!available)
        {
            HelloToggle.IsEnabled = false;
            HelloDesc.Text = Loc.S("Set_HelloUnavailable");
        }
    }

    private GeneralSettings BuildGeneral()
    {
        var g = SessionState.Current.Snapshot?.General ?? new GeneralSettings();
        return new GeneralSettings
        {
            StartWithWindows = StartupToggle.IsChecked == true,
            StartMinimized = StartMinToggle.IsChecked == true,
            MinimizeToTray = TrayToggle.IsChecked == true,
            KeepHistory = HistoryToggle.IsChecked == true,
            Theme = (AppTheme)Math.Max(0, ThemeCombo.SelectedIndex),
            Language = (AppLanguage)Math.Max(0, LangCombo.SelectedIndex),
            HistoryLimit = g.HistoryLimit,
        };
    }

    private async void General_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var g = BuildGeneral();
        StartupManager.SetRunAtLogin(g.StartWithWindows, g.StartMinimized);
        await SessionState.Current.Client.UpdateGeneralSettingsAsync(g);
        await SessionState.Current.RefreshAsync();
    }

    private async void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var g = BuildGeneral();
        ThemeManager.Apply(g.Theme);
        await SessionState.Current.Client.UpdateGeneralSettingsAsync(g);
        await SessionState.Current.RefreshAsync();
    }

    private async void Lang_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var g = BuildGeneral();
        Loc.Current.SetLanguage(g.Language);           // live switch across the app
        await SessionState.Current.Client.UpdateGeneralSettingsAsync(g);
        await SessionState.Current.RefreshAsync();
        LoadValues();                                   // repopulate code-set combo texts
    }

    private async void DefaultMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var snap = SessionState.Current.Snapshot;
        var p = snap?.DefaultPolicy.Clone() ?? UnlockPolicy.SecureDefault();
        p.Mode = DefaultModeCombo.SelectedIndex switch
        {
            0 => UnlockMode.Always, 2 => UnlockMode.ForMinutes, 3 => UnlockMode.WholeSession, _ => UnlockMode.UntilAppCloses,
        };
        var owner = Window.GetWindow(this);
        var resp = await SessionState.Current.RunPrivilegedAsync(owner,
            token => SessionState.Current.Client.UpdateDefaultPolicyAsync(p, token));
        if (resp.Status == ResponseStatus.Denied) LoadValues();
        await SessionState.Current.RefreshAsync();
    }

    private async void ProtectChanges_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        await UpdateSecurityAsync(s => s.ProtectConfigurationChanges = ProtectChangesToggle.IsChecked == true);
    }

    private async void Lockout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        int attempts = TagInt(AttemptsCombo, 5);
        int seconds = TagInt(LockoutCombo, 30);
        await UpdateSecurityAsync(s => { s.MaxFailedAttempts = attempts; s.LockoutSeconds = seconds; });
    }

    private static int TagInt(ComboBox combo, int fallback)
        => combo.SelectedItem is ComboBoxItem it && int.TryParse(it.Tag?.ToString(), out var v) ? v : fallback;

    private async Task UpdateSecurityAsync(Action<SecuritySettings> mutate)
    {
        var snap = SessionState.Current.Snapshot;
        var sec = snap?.Security is null ? new SecuritySettings() : Clone(snap.Security);
        mutate(sec);
        var owner = Window.GetWindow(this);
        var resp = await SessionState.Current.RunPrivilegedAsync(owner,
            token => SessionState.Current.Client.UpdateSecuritySettingsAsync(sec, token));
        if (resp.Status == ResponseStatus.Denied) LoadValues();
        else if (resp.Status != ResponseStatus.Ok)
            MessageBoxCard.ShowError(owner, Loc.S("Err_SaveTitle"), Loc.S("Err_Generic"));
        await SessionState.Current.RefreshAsync();
    }

    private static SecuritySettings Clone(SecuritySettings s) => new()
    {
        ProtectConfigurationChanges = s.ProtectConfigurationChanges,
        WindowsHelloEnabled = s.WindowsHelloEnabled,
        MaxFailedAttempts = s.MaxFailedAttempts,
        LockoutSeconds = s.LockoutSeconds,
    };

    private async void Hello_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var owner = Window.GetWindow(this);
        bool desired = HelloToggle.IsChecked == true;

        if (desired)
        {
            if (!await HelloService.IsAvailableAsync())
            {
                HelloToggle.IsChecked = false;
                MessageBoxCard.ShowInfo(owner, Loc.S("Set_HelloUnavailTitle"), Loc.S("Set_HelloUnavailMsg"));
                return;
            }
            var pw = ReAuthDialog.Ask(owner, Loc.S("Set_HelloLinkPrompt"));
            if (pw is null) { HelloToggle.IsChecked = false; return; }
            var auth = await SessionState.Current.Client.AuthenticateAdminAsync(pw);
            if (auth.Status != ResponseStatus.Ok)
            {
                HelloToggle.IsChecked = false;
                MessageBoxCard.ShowError(owner, Loc.S("Set_HelloWrongTitle"), Loc.S("Set_HelloWrongMsg"));
                return;
            }
            SessionState.Current.AdminToken = auth.AdminToken;
            HelloService.Enroll(pw);
            await UpdateSecurityAsync(s => s.WindowsHelloEnabled = true);
        }
        else
        {
            HelloService.Unenroll();
            await UpdateSecurityAsync(s => s.WindowsHelloEnabled = false);
        }
    }

    private async void ChangePwd_Click(object sender, RoutedEventArgs e)
    {
        if (ChangePasswordDialog.Run(Window.GetWindow(this)))
        {
            HelloService.Unenroll();
            await SessionState.Current.RefreshAsync();
            LoadValues();
        }
    }

    private async void Recovery_Click(object sender, RoutedEventArgs e)
    {
        if (RecoveryDialog.Run(Window.GetWindow(this)))
        {
            await SessionState.Current.RefreshAsync();
            LoadValues();
        }
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (!ConfirmDialog.Show(owner, Loc.S("Act_ClearTitle"),
            Loc.S("Act_ClearMsg"), Loc.S("Common_Clear"), danger: true, glyph: ""))
            return;
        await SessionState.Current.RunPrivilegedAsync(owner, token => SessionState.Current.Client.ClearHistoryAsync(token));
    }

    private void Licence_Click(object sender, RoutedEventArgs e) => OpenDoc("LICENSE.txt", Loc.S("Set_Licence"));
    private void Docs_Click(object sender, RoutedEventArgs e) => OpenDoc("How-it-works.txt", Loc.S("Set_HowItWorks"));

    private void OpenDoc(string fileName, string title)
    {
        try
        {
            var path = Path.Combine(BastionPaths.InstallDir, fileName);
            if (File.Exists(path)) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return; }
        }
        catch { }
        MessageBoxCard.ShowInfo(Window.GetWindow(this), title, Loc.S("Set_DocInstalled"));
    }
}

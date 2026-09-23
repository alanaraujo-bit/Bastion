using System.IO;
using System.Windows;
using System.Windows.Controls;
using Bastion.Core;
using Bastion.Core.Ipc;
using Bastion.Core.Models;
using Bastion.Ui.Localization;
using Bastion.Ui.Services;
using Microsoft.Win32;

namespace Bastion.App;

public partial class ProtectDialog : Window
{
    private IReadOnlyList<AppCandidate> _all = Array.Empty<AppCandidate>();
    private AppCandidate? _selected;

    private ProtectDialog(Window? owner)
    {
        InitializeComponent();
        Owner = owner?.IsLoaded == true ? owner : null;
        LocHelpers.FillMinutes(MinutesCombo);
        Loaded += async (_, _) => await ScanAsync();
        ModeCombo.SelectedIndex = 1;
    }

    public static bool Run(Window? owner) => new ProtectDialog(owner).ShowDialog() == true;

    private async Task ScanAsync()
    {
        Scanning.Visibility = Visibility.Visible;
        _all = await InstalledAppScanner.ScanAsync();
        Scanning.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_all.Count == 0) Scanning.Text = Loc.S("Protect_NoApps");
        ApplyFilter();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = Search.Text?.Trim() ?? "";
        var view = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(c => c.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
                           || c.ImageName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Candidates.ItemsSource = view;
    }

    private void Candidates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Candidates.SelectedItem is AppCandidate c) GoToConfig(c);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", CheckFileExists = true, Title = Loc.S("Protect_ChooseTitle") };
        if (dlg.ShowDialog(this) == true)
        {
            var path = dlg.FileName;
            GoToConfig(new AppCandidate(Path.GetFileNameWithoutExtension(path), path, Path.GetFileName(path)));
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var exe = files.FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (exe is not null)
            GoToConfig(new AppCandidate(Path.GetFileNameWithoutExtension(exe), exe, Path.GetFileName(exe)));
    }

    private void GoToConfig(AppCandidate c)
    {
        _selected = c;
        PickPanel.Visibility = Visibility.Collapsed;
        ConfigPanel.Visibility = Visibility.Visible;
        BackBtn.Visibility = Visibility.Visible;
        DialogTitle.Text = Loc.S("Protect_SetupTitle");
        DialogSubtitle.Text = Loc.S("Protect_SetupSub");

        SelIcon.Source = IconExtractor.FromExecutable(c.TargetPath, 64);
        SelName.Text = c.DisplayName;
        SelPath.Text = c.TargetPath;

        var reason = ProtectableRules.DenylistReason(c.ImageName);
        if (reason is not null)
        {
            DenyWarn.Visibility = Visibility.Visible;
            DenyText.Text = reason;
            StoreWarn.Visibility = Visibility.Collapsed;
            ProtectBtn.IsEnabled = false;
        }
        else
        {
            DenyWarn.Visibility = Visibility.Collapsed;
            // Soft caution for Store/MSIX apps — allowed, but flagged as unreliable.
            StoreWarn.Visibility = ProtectableRules.IsLikelyStoreRedirected(c.ImageName, c.TargetPath)
                ? Visibility.Visible : Visibility.Collapsed;
            ProtectBtn.IsEnabled = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _selected = null;
        Candidates.SelectedItem = null;
        ConfigPanel.Visibility = Visibility.Collapsed;
        PickPanel.Visibility = Visibility.Visible;
        BackBtn.Visibility = Visibility.Collapsed;
        ProtectBtn.IsEnabled = false;
        DialogTitle.Text = Loc.S("Protect_Title");
        DialogSubtitle.Text = Loc.S("Protect_Sub");
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MinutesRow is null) return;
        MinutesRow.Visibility = ModeCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Protect_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var policy = new UnlockPolicy
        {
            Mode = ModeCombo.SelectedIndex switch
            {
                0 => UnlockMode.Always,
                2 => UnlockMode.ForMinutes,
                3 => UnlockMode.WholeSession,
                _ => UnlockMode.UntilAppCloses,
            },
            Minutes = MinutesCombo.SelectedItem is ComboBoxItem it && int.TryParse(it.Tag?.ToString(), out var m) ? m : 5,
            RelockOnWorkstationLock = true,
            RelockOnResume = true,
        };

        var app = new ProtectedApp
        {
            ImageName = _selected.ImageName.ToLowerInvariant(),
            DisplayName = _selected.DisplayName,
            TargetPath = _selected.TargetPath,
            Enabled = true,
            OverridePolicy = true,
            Policy = policy,
            AllowWindowsHello = HelloToggle.IsChecked == true,
        };

        ProtectBtn.IsEnabled = false;
        ProtectBtn.Content = Loc.S("Protect_Protecting");
        var resp = await SessionState.Current.RunPrivilegedAsync(this,
            token => SessionState.Current.Client.AddAppAsync(app, token));
        ProtectBtn.Content = Loc.S("Protect_Btn");

        if (resp.Status == ResponseStatus.Ok)
        {
            DialogResult = true;
        }
        else
        {
            ProtectBtn.IsEnabled = true;
            if (resp.Status != ResponseStatus.Denied)
                MessageBoxCard.ShowError(this, Loc.S("Protect_ErrTitle"), Loc.S("Err_Generic"));
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

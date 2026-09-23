using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Bastion.Core.Ipc;
using Bastion.App.ViewModels;
using Bastion.Ui.Localization;

namespace Bastion.App.Pages;

public partial class AppsPage : UserControl, IPage
{
    private IReadOnlyList<AppRowVm> _rows = Array.Empty<AppRowVm>();

    public AppsPage() => InitializeComponent();

    public async void OnShown()
    {
        await SessionState.Current.RefreshAsync();
        Rebuild();
    }

    private void Rebuild()
    {
        var snap = SessionState.Current.Snapshot;
        _rows = snap is null ? Array.Empty<AppRowVm>() : AppRowVm.Build(snap);
        ApplyFilter();
        EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = Search.Text?.Trim() ?? "";
        List.ItemsSource = string.IsNullOrEmpty(q)
            ? _rows
            : _rows.Where(r => r.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static AppRowVm? Vm(object sender) => (sender as FrameworkElement)?.Tag as AppRowVm;

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (ProtectDialog.Run(Window.GetWindow(this))) { await SessionState.Current.RefreshAsync(); Rebuild(); }
    }

    private async void Enabled_Click(object sender, RoutedEventArgs e)
    {
        if (Vm(sender) is not { } vm) return;
        bool desired = (sender as ToggleButton)?.IsChecked == true;
        var owner = Window.GetWindow(this);

        if (!desired)
        {
            bool ok = ConfirmDialog.Show(owner, Loc.F("Confirm_PauseAppTitle", vm.DisplayName),
                Loc.S("Confirm_PauseAppMsg"), Loc.S("Common_Pause"), danger: true, glyph: "");
            if (!ok) { (sender as ToggleButton)!.IsChecked = true; return; }
        }

        var resp = await SessionState.Current.RunPrivilegedAsync(owner,
            token => SessionState.Current.Client.SetAppEnabledAsync(vm.App.Id, desired, token));
        if (resp.Status != ResponseStatus.Ok && resp.Status != ResponseStatus.Denied)
            MessageBoxCard.ShowError(owner, Loc.S("Err_UpdateTitle"), Loc.S("Err_Generic"));
        await SessionState.Current.RefreshAsync();
        Rebuild();
    }

    private async void Configure_Click(object sender, RoutedEventArgs e)
    {
        if (Vm(sender) is not { } vm) return;
        if (PolicyDialog.Run(Window.GetWindow(this), vm.App))
        {
            await SessionState.Current.RefreshAsync();
            Rebuild();
        }
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (Vm(sender) is not { } vm) return;
        try
        {
            if (File.Exists(vm.Path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{vm.Path}\"") { UseShellExecute = true });
            else
                MessageBoxCard.ShowInfo(Window.GetWindow(this), Loc.S("Info_FileNotFoundTitle"),
                    Loc.S("Info_FileNotFoundMsg"));
        }
        catch { }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Vm(sender) is not { } vm) return;
        var owner = Window.GetWindow(this);
        bool ok = ConfirmDialog.Show(owner, Loc.F("Confirm_RemoveTitle", vm.DisplayName),
            Loc.S("Confirm_RemoveMsg"), Loc.S("Common_Remove"), danger: true, glyph: "");
        if (!ok) return;

        var resp = await SessionState.Current.RunPrivilegedAsync(owner,
            token => SessionState.Current.Client.RemoveAppAsync(vm.App.Id, token));
        if (resp.Status != ResponseStatus.Ok && resp.Status != ResponseStatus.Denied)
            MessageBoxCard.ShowError(owner, Loc.S("Err_RemoveTitle"), Loc.S("Err_Generic"));
        await SessionState.Current.RefreshAsync();
        Rebuild();
    }
}

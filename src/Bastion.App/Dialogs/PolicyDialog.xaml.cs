using System.Windows;
using System.Windows.Controls;
using Bastion.Core.Ipc;
using Bastion.Core.Models;
using Bastion.Ui.Localization;

namespace Bastion.App;

public partial class PolicyDialog : Window
{
    private readonly ProtectedApp _app;

    private PolicyDialog(Window? owner, ProtectedApp app)
    {
        InitializeComponent();
        Owner = owner?.IsLoaded == true ? owner : null;
        _app = app;
        AppText.Text = string.IsNullOrWhiteSpace(app.DisplayName) ? app.ImageName : app.DisplayName;
        LocHelpers.FillMinutes(MinutesCombo);

        var p = app.Policy;
        ModeCombo.SelectedIndex = p.Mode switch
        {
            UnlockMode.Always => 0,
            UnlockMode.ForMinutes => 2,
            UnlockMode.WholeSession => 3,
            _ => 1,
        };
        SelectMinutes(p.Minutes);
        HelloToggle.IsChecked = app.AllowWindowsHello;
        LockToggle.IsChecked = p.RelockOnWorkstationLock;
        ResumeToggle.IsChecked = p.RelockOnResume;
        UpdateMinutesVisibility();
    }

    public static bool Run(Window? owner, ProtectedApp app) => new PolicyDialog(owner, app).ShowDialog() == true;

    private void SelectMinutes(int minutes)
    {
        foreach (ComboBoxItem it in MinutesCombo.Items)
            if (it.Tag?.ToString() == minutes.ToString()) { it.IsSelected = true; return; }
        MinutesCombo.SelectedIndex = 1; // default 5
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e) => UpdateMinutesVisibility();

    private void UpdateMinutesVisibility()
    {
        if (MinutesRow is null) return;
        MinutesRow.Visibility = ModeCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
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
            RelockOnWorkstationLock = LockToggle.IsChecked == true,
            RelockOnResume = ResumeToggle.IsChecked == true,
        };

        SaveBtn.IsEnabled = false;
        var resp = await SessionState.Current.RunPrivilegedAsync(this,
            token => SessionState.Current.Client.UpdateAppPolicyAsync(_app.Id, policy, HelloToggle.IsChecked == true, token));
        SaveBtn.IsEnabled = true;

        if (resp.Status == ResponseStatus.Ok) DialogResult = true;
        else if (resp.Status != ResponseStatus.Denied)
            MessageBoxCard.ShowError(this, Loc.S("Err_SaveTitle"), Loc.S("Err_Generic"));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

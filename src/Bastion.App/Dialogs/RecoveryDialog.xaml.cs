using System.Windows;
using Bastion.Core.Ipc;
using Bastion.Ui.Localization;

namespace Bastion.App;

public partial class RecoveryDialog : Window
{
    private RecoveryDialog(Window? owner)
    {
        InitializeComponent();
        Owner = owner?.IsLoaded == true ? owner : null;
        var snap = SessionState.Current.Snapshot;
        if (snap?.RecoveryEnabled == true)
            DisableBtn.Visibility = Visibility.Visible;
    }

    public static bool Run(Window? owner) => new RecoveryDialog(owner).ShowDialog() == true;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Error.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(Question.Text)) { Fail(Loc.S("Rec_NeedQuestion")); return; }
        if (Answer.Password.Trim().Length < 2) { Fail(Loc.S("Rec_NeedAnswer")); return; }

        SaveBtn.IsEnabled = false;
        var resp = await SessionState.Current.RunPrivilegedAsync(this,
            token => SessionState.Current.Client.SetupRecoveryAsync(Question.Text.Trim(), Answer.Password, token));
        SaveBtn.IsEnabled = true;

        if (resp.Status == ResponseStatus.Ok) DialogResult = true;
        else if (resp.Status != ResponseStatus.Denied) Fail(Loc.S("Rec_Failed"));
    }

    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        var resp = await SessionState.Current.RunPrivilegedAsync(this,
            token => SessionState.Current.Client.SetupRecoveryAsync(null, null, token));
        if (resp.Status == ResponseStatus.Ok) DialogResult = true;
        else if (resp.Status != ResponseStatus.Denied) Fail(Loc.S("Rec_OffFailed"));
    }

    private void Fail(string msg) { Error.Text = msg; Error.Visibility = Visibility.Visible; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

using System.Windows;
using System.Windows.Media;
using Bastion.Core.Ipc;
using Bastion.Core.Security;
using Bastion.Ui.Localization;

namespace Bastion.App;

public partial class ChangePasswordDialog : Window
{
    private ChangePasswordDialog(Window? owner)
    {
        InitializeComponent();
        Owner = owner?.IsLoaded == true ? owner : null;
        Loaded += (_, _) => Current.Focus();
    }

    public static bool Run(Window? owner) => new ChangePasswordDialog(owner).ShowDialog() == true;

    private void New_Changed(object sender, RoutedEventArgs e)
    {
        int score = PasswordService.EstimateStrength(New1.Password); // 0..4
        StrengthFill.Width = StrengthTrack.ActualWidth * (score / 4.0);
        StrengthFill.Background = (Brush)FindResource(score >= 3 ? "Brush.Success" : score >= 2 ? "Brush.Warning" : "Brush.Danger");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Error.Visibility = Visibility.Collapsed;
        if (New1.Password.Length < 6) { Fail(Loc.S("ChPwd_TooShort")); return; }
        if (New1.Password != New2.Password) { Fail(Loc.S("ChPwd_Mismatch")); return; }

        SaveBtn.IsEnabled = false;
        var resp = await SessionState.Current.Client.ChangeMasterPasswordAsync(Current.Password, New1.Password);
        SaveBtn.IsEnabled = true;

        if (resp.Status == ResponseStatus.Ok) { DialogResult = true; return; }
        Fail(resp.Status == ResponseStatus.Denied ? Loc.S("ChPwd_WrongCurrent") : Loc.S("ChPwd_Failed"));
    }

    private void Fail(string msg) { Error.Text = msg; Error.Visibility = Visibility.Visible; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

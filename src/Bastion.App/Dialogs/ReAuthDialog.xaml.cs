using System.Windows;
using System.Windows.Input;

namespace Bastion.App;

public partial class ReAuthDialog : Window
{
    private ReAuthDialog(Window? owner, string? subtitle)
    {
        InitializeComponent();
        Owner = owner;
        if (!string.IsNullOrEmpty(subtitle)) Sub.Text = subtitle;
        Loaded += (_, _) => Password.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Confirm_Click(this, new RoutedEventArgs());
            else if (e.Key == Key.Escape) Cancel_Click(this, new RoutedEventArgs());
        };
    }

    public string? Result { get; private set; }

    /// <summary>Shows the dialog and returns the entered password, or null if cancelled.</summary>
    public static string? Ask(Window? owner, string? subtitle = null)
    {
        var dlg = new ReAuthDialog(owner, subtitle);
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Password.Password))
        {
            Error.Text = Bastion.Ui.Localization.Loc.S("ReAuth_EnterPwd");
            Error.Visibility = Visibility.Visible;
            return;
        }
        Result = Password.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

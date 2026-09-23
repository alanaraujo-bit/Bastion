using System.Windows;
using System.Windows.Media;

namespace Bastion.App;

public partial class ConfirmDialog : Window
{
    private ConfirmDialog() => InitializeComponent();

    public static bool Show(Window? owner, string title, string message,
                            string confirmText = "Confirm", bool danger = false, string glyph = "",
                            bool singleButton = false)
    {
        var dlg = new ConfirmDialog { Owner = owner?.IsLoaded == true ? owner : null };
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ConfirmBtn.Content = confirmText;
        dlg.IconGlyph.Text = glyph;
        if (singleButton) dlg.CancelBtn.Visibility = Visibility.Collapsed;
        if (danger)
        {
            dlg.ConfirmBtn.Style = (Style)dlg.FindResource("Btn.Danger");
            dlg.IconHost.Background = (Brush)dlg.FindResource("Brush.DangerSoft");
            dlg.IconGlyph.Foreground = (Brush)dlg.FindResource("Brush.Danger");
        }
        return dlg.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

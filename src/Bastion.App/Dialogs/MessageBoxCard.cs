using System.Windows;

namespace Bastion.App;

/// <summary>Themed, single-button information/error dialogs.</summary>
public static class MessageBoxCard
{
    public static void ShowInfo(Window? owner, string title, string message)
        => ConfirmDialog.Show(owner, title, message, "OK", danger: false, glyph: "", singleButton: true);

    public static void ShowError(Window? owner, string title, string message)
        => ConfirmDialog.Show(owner, title, message, "OK", danger: true, glyph: "", singleButton: true);

    public static void ShowSuccess(Window? owner, string title, string message)
        => ConfirmDialog.Show(owner, title, message, "Done", danger: false, glyph: "", singleButton: true);
}

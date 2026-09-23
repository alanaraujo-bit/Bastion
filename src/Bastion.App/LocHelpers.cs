using System.Windows.Controls;
using Bastion.Ui.Localization;

namespace Bastion.App;

/// <summary>Small helpers for localizing combo items that carry numeric tags.</summary>
public static class LocHelpers
{
    public static void FillMinutes(ComboBox combo)
    {
        foreach (ComboBoxItem it in combo.Items)
        {
            var tag = it.Tag?.ToString() ?? "";
            it.Content = tag == "1" ? Loc.S("Minutes_1") : Loc.F("Minutes_N", tag);
        }
    }
}

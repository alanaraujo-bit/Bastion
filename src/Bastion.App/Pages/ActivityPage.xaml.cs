using System.Windows;
using System.Windows.Controls;
using Bastion.Core.Ipc;
using Bastion.App.ViewModels;
using Bastion.Ui.Localization;

namespace Bastion.App.Pages;

public partial class ActivityPage : UserControl, IPage
{
    public ActivityPage() => InitializeComponent();

    public async void OnShown() => await LoadAsync();

    private async Task LoadAsync()
    {
        var resp = await SessionState.Current.Client.GetHistoryAsync(500);
        var items = (resp.History ?? new()).Select(ActivityRowVm.From).ToList();
        List.ItemsSource = items;
        bool empty = items.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListCard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (!ConfirmDialog.Show(owner, Loc.S("Act_ClearTitle"),
            Loc.S("Act_ClearMsg"), Loc.S("Common_Clear"), danger: true, glyph: ""))
            return;

        var resp = await SessionState.Current.RunPrivilegedAsync(owner,
            token => SessionState.Current.Client.ClearHistoryAsync(token));
        if (resp.Status != ResponseStatus.Ok && resp.Status != ResponseStatus.Denied)
            MessageBoxCard.ShowError(owner, Loc.S("Act_ClearTitle"), Loc.S("Err_Generic"));
        await LoadAsync();
    }
}

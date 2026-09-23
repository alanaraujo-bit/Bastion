using System.Windows;
using System.Windows.Controls;
using Bastion.Core.Ipc;
using Bastion.App.ViewModels;
using Bastion.Ui.Localization;

namespace Bastion.App.Pages;

public partial class HomePage : UserControl, IPage
{
    public HomePage() => InitializeComponent();

    public async void OnShown()
    {
        await SessionState.Current.RefreshAsync();
        Render();
        await LoadActivityAsync();
    }

    private void Render()
    {
        var snap = SessionState.Current.Snapshot;
        if (snap is null) return;

        var rows = AppRowVm.Build(snap);
        int protectedCount = snap.Apps.Count(a => a.Enabled);

        HeroCount.Text = protectedCount switch
        {
            0 => Loc.S("Home_CountNone"),
            1 => Loc.S("Home_CountOne"),
            _ => Loc.F("Home_CountN", protectedCount),
        };
        HeroSubtitle.Text = protectedCount == 0 ? Loc.S("Home_SubNone") : Loc.S("Home_SubSome");

        AppsList.ItemsSource = rows;
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppsList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadActivityAsync()
    {
        var resp = await SessionState.Current.Client.GetHistoryAsync(6);
        var items = (resp.History ?? new()).Take(6).Select(ActivityRowVm.From).ToList();
        ActivityList.ItemsSource = items;
        NoActivity.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Protect_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var added = ProtectDialog.Run(owner);
        if (added)
        {
            await SessionState.Current.RefreshAsync();
            Render();
            await LoadActivityAsync();
        }
    }

    private void Manage_Click(object sender, RoutedEventArgs e)
        => (Window.GetWindow(this) as MainWindow)?.NavigateTo(NavSection.Applications);
}

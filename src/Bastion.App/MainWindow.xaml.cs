using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bastion.Core.Ipc;
using Bastion.App.Pages;
using Bastion.Ui.Localization;

namespace Bastion.App;

public enum NavSection { Home, Applications, Activity, Settings }

public sealed record NavItem(string Glyph, string Label, NavSection Section);

/// <summary>Contract every content page implements so the shell can notify it.</summary>
public interface IPage
{
    void OnShown();
}

public partial class MainWindow : Window
{
    private readonly Dictionary<NavSection, UserControl> _pages = new();
    private bool _updatingToggle;

    public MainWindow()
    {
        InitializeComponent();

        BuildNav();

        SessionState.Current.SnapshotChanged += OnSnapshotChanged;
        Loc.Current.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            SessionState.Current.SnapshotChanged -= OnSnapshotChanged;
            Loc.Current.LanguageChanged -= OnLanguageChanged;
        };

        Nav.SelectedIndex = 0;
        OnSnapshotChanged();
    }

    private void BuildNav()
    {
        Nav.ItemsSource = new List<NavItem>
        {
            new("", Loc.S("Nav_Home"), NavSection.Home),
            new("", Loc.S("Nav_Apps"), NavSection.Applications),
            new("", Loc.S("Nav_Activity"), NavSection.Activity),
            new("", Loc.S("Nav_Settings"), NavSection.Settings),
        };
    }

    private void OnLanguageChanged()
    {
        int idx = Nav.SelectedIndex;
        BuildNav();
        Nav.SelectedIndex = Math.Max(0, idx);
        OnSnapshotChanged();
    }

    public void NavigateTo(NavSection section)
    {
        var idx = ((List<NavItem>)Nav.ItemsSource).FindIndex(i => i.Section == section);
        if (idx >= 0) Nav.SelectedIndex = idx;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        SectionTitle.Text = item.Label;
        var page = GetPage(item.Section);
        PageHost.Content = page;
        (page as IPage)?.OnShown();
    }

    private UserControl GetPage(NavSection section)
    {
        if (_pages.TryGetValue(section, out var existing)) return existing;
        UserControl page = section switch
        {
            NavSection.Home => new HomePage(),
            NavSection.Applications => new AppsPage(),
            NavSection.Activity => new ActivityPage(),
            NavSection.Settings => new SettingsPage(),
            _ => new HomePage(),
        };
        _pages[section] = page;
        return page;
    }

    private void OnSnapshotChanged()
    {
        var s = SessionState.Current;
        ServiceBanner.Visibility = s.ServiceReachable ? Visibility.Collapsed : Visibility.Visible;

        var snap = s.Snapshot;
        bool on = s.ServiceReachable && snap is { GlobalProtectionEnabled: true };

        _updatingToggle = true;
        GlobalToggle.IsChecked = on;
        _updatingToggle = false;

        if (!s.ServiceReachable)
        {
            StatusText.Text = Loc.S("Status_Offline");
            StatusDot.Fill = (Brush)FindResource("Brush.Danger");
        }
        else if (on)
        {
            int count = snap!.Apps.Count(a => a.Enabled);
            StatusText.Text = count > 0 ? Loc.S("Status_Active") : Loc.S("Status_NoApps");
            StatusDot.Fill = (Brush)FindResource(count > 0 ? "Brush.Success" : "Brush.Warning");
        }
        else
        {
            StatusText.Text = Loc.S("Status_Paused");
            StatusDot.Fill = (Brush)FindResource("Brush.Warning");
        }
    }

    private async void GlobalToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingToggle) return;
        bool desired = GlobalToggle.IsChecked == true;

        if (!desired)
        {
            bool ok = ConfirmDialog.Show(this, Loc.S("Confirm_PauseAllTitle"),
                Loc.S("Confirm_PauseAllMsg"),
                Loc.S("Confirm_PauseAllBtn"), danger: true, glyph: "");
            if (!ok) { _updatingToggle = true; GlobalToggle.IsChecked = true; _updatingToggle = false; return; }
        }

        var resp = await SessionState.Current.RunPrivilegedAsync(this,
            token => SessionState.Current.Client.SetGlobalProtectionAsync(desired, token));

        if (resp.Status != ResponseStatus.Ok)
        {
            _updatingToggle = true; GlobalToggle.IsChecked = !desired; _updatingToggle = false;
            if (resp.Status != ResponseStatus.Denied)
                MessageBoxCard.ShowError(this, Loc.S("Err_ProtectionTitle"), Loc.S("Err_Generic"));
        }
        await SessionState.Current.RefreshAsync();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Close to tray — the protection service keeps running regardless.
        Hide();
    }
}

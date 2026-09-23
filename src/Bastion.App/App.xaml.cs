using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bastion.Core.Models;
using Bastion.Ui.Localization;
using Bastion.Ui.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace Bastion.App;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "Bastion.App.SingleInstance.v1";
    private const string ShowEventName = "Bastion.App.ShowWindow.v1";

    private TaskbarIcon? _tray;
    private MainWindow? _main;
    private EventWaitHandle? _showSignal;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // Signal the running instance to surface, then exit.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ListenForShowSignal();

        DispatcherUnhandledException += OnUnhandled;

        ThemeManager.Apply(AppTheme.System);
        await SessionState.Current.RefreshAsync();
        ApplyThemeFromState();
        ApplyLanguageFromState();
        Loc.Current.LanguageChanged += () => _tray?.Dispatcher.Invoke(BuildTrayMenu);
        SetUpTray();

        var snap = SessionState.Current.Snapshot;
        bool onboarded = SessionState.Current.ServiceReachable &&
                         snap is { OnboardingComplete: true, MasterConfigured: true };
        bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        if (onboarded)
        {
            // Launched at login with --minimized: stay in the tray, don't pop the window.
            if (!startMinimized) ShowMain();
        }
        else
        {
            ShowOnboarding();
        }

#if DEBUG
        ApplyDebugArgs(e.Args);
#endif
    }

#if DEBUG
    private void ApplyDebugArgs(string[] args)
    {
        foreach (var a in args)
        {
            if (a.StartsWith("--theme:", StringComparison.OrdinalIgnoreCase))
                ThemeManager.Apply(a.EndsWith("dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark : AppTheme.Light);
            if (a.StartsWith("--page:", StringComparison.OrdinalIgnoreCase) && _main is not null)
            {
                var s = a["--page:".Length..].ToLowerInvariant() switch
                {
                    "apps" or "applications" => NavSection.Applications,
                    "activity" => NavSection.Activity,
                    "settings" => NavSection.Settings,
                    _ => NavSection.Home,
                };
                _main.NavigateTo(s);
            }
        }
    }
#endif

    private void ApplyThemeFromState()
    {
        var theme = SessionState.Current.Snapshot?.General.Theme ?? AppTheme.System;
        ThemeManager.Apply(theme);
    }

    private void ApplyLanguageFromState()
    {
        var lang = SessionState.Current.Snapshot?.General.Language ?? AppLanguage.System;
        Loc.Current.SetLanguage(lang);
    }

    public void ReapplyTheme() => ApplyThemeFromState();

    private void SetUpTray()
    {
        _tray = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/bastion.ico")),
        };
        _tray.TrayMouseDoubleClick += (_, _) => ShowMain();
        BuildTrayMenu();
    }

    private void BuildTrayMenu()
    {
        if (_tray is null) return;
        _tray.ToolTipText = Loc.S("Tray_Tooltip");
        var menu = new ContextMenu();
        menu.Items.Add(TrayItem(Loc.S("Tray_Open"), (_, _) => ShowMain()));
        menu.Items.Add(TrayItem(Loc.S("Tray_RelockAll"), async (_, _) =>
        {
            await SessionState.Current.Client.RelockAllAsync();
            await SessionState.Current.RefreshAsync();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem(Loc.S("Tray_Settings"), (_, _) => { ShowMain(); _main?.NavigateTo(NavSection.Settings); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem(Loc.S("Tray_Exit"), (_, _) => ExitUi()));
        _tray.ContextMenu = menu;
    }

    private static MenuItem TrayItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    private void ListenForShowSignal()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _showSignal!.WaitOne();
                    Dispatcher.Invoke(ShowMain);
                }
                catch { break; }
            }
        }) { IsBackground = true };
        thread.Start();
    }

    public void ShowMain()
    {
        if (_main is null || !_main.IsLoaded)
        {
            _main = new MainWindow();
            _main.Closed += (_, _) => _main = null;
        }
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        _main.Topmost = true;
        _main.Topmost = false;
    }

    private void ShowOnboarding()
    {
        var onboarding = new OnboardingWindow();
        onboarding.Completed += () =>
        {
            ApplyThemeFromState();
            ApplyLanguageFromState();
            ShowMain();
        };
        onboarding.Show();
    }

    private void ExitUi()
    {
        _tray?.Dispose();
        Shutdown();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBoxCard.ShowError(_main, Loc.S("Err_Something"), e.Exception.Message);
    }
}

using System.Windows;
using Microsoft.Win32;
using Bastion.Core.Models;

namespace Bastion.Ui.Services;

/// <summary>Applies the light/dark palette to the running WPF application.</summary>
public static class ThemeManager
{
    private const string BaseUri = "pack://application:,,,/Bastion.Ui;component/Themes/";

    public static bool CurrentIsDark { get; private set; }

    public static void Apply(AppTheme theme)
    {
        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemPrefersDark(),
        };
        CurrentIsDark = dark;

        var app = Application.Current;
        if (app is null) return;

        var palette = new ResourceDictionary { Source = new Uri(BaseUri + (dark ? "Dark.xaml" : "Light.xaml")) };
        var controls = new ResourceDictionary { Source = new Uri(BaseUri + "Controls.xaml") };

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(palette);
        app.Resources.MergedDictionaries.Add(controls);
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v == 0;
        }
        catch { }
        return false;
    }
}

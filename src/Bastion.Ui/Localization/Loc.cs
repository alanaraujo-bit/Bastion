using System.ComponentModel;
using System.Globalization;
using Bastion.Core.Models;

namespace Bastion.Ui.Localization;

/// <summary>
/// Runtime localization source. XAML binds to its indexer via the {ui:T Key}
/// markup extension; changing the language raises a single indexer change
/// notification so every bound string updates live.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Current { get; } = new();

    private string _lang = "en";

    public AppLanguage Language { get; private set; } = AppLanguage.System;

    public string this[string key] => Strings.Get(key, _lang);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the active language changes (for code that rebuilds text).</summary>
    public event Action? LanguageChanged;

    public void SetLanguage(AppLanguage language)
    {
        Language = language;
        _lang = Resolve(language);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    private static string Resolve(AppLanguage language) => language switch
    {
        AppLanguage.PtBr => "pt",
        AppLanguage.En => "en",
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase) ? "pt" : "en",
    };

    /// <summary>Current-language string for use from code-behind.</summary>
    public static string S(string key) => Current[key];

    /// <summary>Current-language formatted string.</summary>
    public static string F(string key, params object[] args) => string.Format(Current[key], args);
}

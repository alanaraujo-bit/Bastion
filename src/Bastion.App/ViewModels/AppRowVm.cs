using System.Windows.Media;
using Bastion.Core.Ipc;
using Bastion.Core.Models;
using Bastion.Ui.Localization;
using Bastion.Ui.Services;

namespace Bastion.App.ViewModels;

/// <summary>Display projection of a <see cref="ProtectedApp"/> for list rows.</summary>
public sealed class AppRowVm
{
    public required ProtectedApp App { get; init; }
    public string DisplayName => string.IsNullOrWhiteSpace(App.DisplayName) ? App.ImageName : App.DisplayName;
    public string Path => App.TargetPath;
    public bool Enabled => App.Enabled;
    public ImageSource? Icon { get; init; }
    public string PolicyText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public bool IsUnlocked { get; init; }
    public bool HasIcon => Icon is not null;

    public static IReadOnlyList<AppRowVm> Build(StatusSnapshot snap)
    {
        var list = new List<AppRowVm>(snap.Apps.Count);
        foreach (var app in snap.Apps.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var policy = app.OverridePolicy ? app.Policy : snap.DefaultPolicy;
            bool unlocked = snap.ActiveGrants.Contains(app.ImageName, StringComparer.OrdinalIgnoreCase);
            list.Add(new AppRowVm
            {
                App = app,
                Icon = IconExtractor.FromExecutable(app.TargetPath, 48),
                PolicyText = Describe(policy),
                StatusText = !app.Enabled ? Loc.S("Pill_Paused") : unlocked ? Loc.S("Pill_Unlocked") : Loc.S("Pill_Protected"),
                IsUnlocked = unlocked,
            });
        }
        return list;
    }

    public static string Describe(UnlockPolicy p) => p.Mode switch
    {
        UnlockMode.Always => Loc.S("Policy_Always"),
        UnlockMode.UntilAppCloses => Loc.S("Policy_UntilClose"),
        UnlockMode.ForMinutes => Loc.F("Policy_Minutes", p.Minutes),
        UnlockMode.WholeSession => Loc.S("Policy_Session"),
        _ => "",
    };
}

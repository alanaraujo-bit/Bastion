using Bastion.Core.Models;
using Bastion.Ui.Localization;

namespace Bastion.App.ViewModels;

public sealed class ActivityRowVm
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Glyph { get; init; } = "";
    public string ColorKey { get; init; } = "Brush.TextSecondary";
    public string TimeText { get; init; } = "";

    public static ActivityRowVm From(ActivityEvent e)
    {
        var name = e.DisplayName ?? e.ImageName ?? "";
        var (title, glyph, color) = e.Kind switch
        {
            ActivityKind.LaunchAuthorized => (Loc.F("Act_Unlocked", name), "", "Brush.Success"),
            ActivityKind.LaunchDenied => (Loc.F("Act_Denied", name), "", "Brush.Danger"),
            ActivityKind.LaunchCancelled => (Loc.F("Act_Cancelled", name), "", "Brush.TextSecondary"),
            ActivityKind.Relocked => (Loc.F("Act_Relocked", name), "", "Brush.TextSecondary"),
            ActivityKind.ProtectionEnabled => (Loc.S("Act_ProtEnabled"), "", "Brush.Success"),
            ActivityKind.ProtectionDisabled => (Loc.S("Act_ProtPaused"), "", "Brush.Warning"),
            ActivityKind.AppAdded => (Loc.F("Act_Protected", name), "", "Brush.Accent"),
            ActivityKind.AppRemoved => (Loc.F("Act_Removed", name), "", "Brush.TextSecondary"),
            ActivityKind.MasterPasswordChanged => (Loc.S("Act_MasterChanged"), "", "Brush.Accent"),
            ActivityKind.ConfigChanged => (Loc.S("Act_SettingsUpdated"), "", "Brush.TextSecondary"),
            ActivityKind.ServiceStarted => (Loc.S("Act_ServiceStarted"), "", "Brush.TextSecondary"),
            ActivityKind.ServiceStopped => (Loc.S("Act_ServiceStopped"), "", "Brush.Warning"),
            ActivityKind.Error => (Loc.S("Act_Problem"), "", "Brush.Danger"),
            _ => (e.Kind.ToString(), "", "Brush.TextSecondary"),
        };
        return new ActivityRowVm
        {
            Title = title,
            Subtitle = e.Detail ?? "",
            Glyph = glyph,
            ColorKey = color,
            TimeText = FormatTime(e.TimestampUtc.ToLocalTime()),
        };
    }

    private static string FormatTime(DateTimeOffset t)
    {
        var now = DateTimeOffset.Now;
        var span = now - t;
        if (span.TotalMinutes < 1) return Loc.S("Time_JustNow");
        if (span.TotalMinutes < 60) return Loc.F("Time_MinAgo", (int)span.TotalMinutes);
        if (span.TotalHours < 24 && t.Date == now.Date) return t.ToString("HH:mm");
        if (t.Date == now.Date.AddDays(-1)) return Loc.F("Time_Yesterday", t.ToString("HH:mm"));
        return t.ToString("dd MMM, HH:mm");
    }
}

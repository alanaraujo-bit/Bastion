using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bastion.Core.Ipc;
using Bastion.Core.Security;
using Bastion.Ui.Localization;

namespace Bastion.App;

public partial class OnboardingWindow : Window
{
    private int _step;
    private readonly StackPanel[] _panels;
    private readonly System.Windows.Shapes.Ellipse[] _dots;
    private bool _masterSet;

    public event Action? Completed;

    public OnboardingWindow()
    {
        InitializeComponent();
        _panels = new[] { Step0, Step1, Step2, Step3, Step4 };
        _dots = new[] { Dot0, Dot1, Dot2, Dot3, Dot4 };
        // draggable
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        Show(0);
    }

    private void Show(int step)
    {
        _step = step;
        for (int i = 0; i < _panels.Length; i++)
            _panels[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _dots.Length; i++)
            _dots[i].Fill = (Brush)FindResource(i <= step ? "Brush.Accent" : "Brush.BorderStrong");

        BackBtn.Visibility = step is > 0 and < 4 ? Visibility.Visible : Visibility.Collapsed;
        SkipBtn.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = step switch
        {
            0 => Loc.S("Onb_GetStarted"),
            2 => Loc.S("Onb_PwdCreate"),
            3 => Loc.S("Common_Continue"),
            4 => Loc.S("Onb_Open"),
            _ => Loc.S("Common_Continue"),
        };

        if (step == 2) Pwd1.Focus();
    }

    private void Pwd_Changed(object sender, RoutedEventArgs e)
    {
        int score = PasswordService.EstimateStrength(Pwd1.Password);
        StrengthFill.Width = StrengthTrack.ActualWidth * (score / 4.0);
        StrengthFill.Background = (Brush)FindResource(score >= 3 ? "Brush.Success" : score >= 2 ? "Brush.Warning" : "Brush.Danger");
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 2:
                if (!await CreateMasterAsync()) return;
                Show(3);
                break;
            case 3:
                if (!await SaveRecoveryIfProvidedAsync()) return;
                await FinishAsync();
                Show(4);
                break;
            case 4:
                Completed?.Invoke();
                Close();
                break;
            default:
                Show(_step + 1);
                break;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (_step > 0) Show(_step - 1); }

    private async void Skip_Click(object sender, RoutedEventArgs e)
    {
        await FinishAsync();
        Show(4);
    }

    private async Task<bool> CreateMasterAsync()
    {
        Step2Error.Visibility = Visibility.Collapsed;
        if (Pwd1.Password.Length < 6) { Step2Fail(Loc.S("Onb_PwdShort")); return false; }
        if (Pwd1.Password != Pwd2.Password) { Step2Fail(Loc.S("Onb_PwdMismatch")); return false; }
        if (_masterSet) return true;

        NextBtn.IsEnabled = false;
        var resp = await SessionState.Current.Client.SetMasterPasswordAsync(Pwd1.Password);

        if (resp.Status == ResponseStatus.Ok || resp.Status == ResponseStatus.AlreadyConfigured)
        {
            NextBtn.IsEnabled = true;
            _masterSet = true;
            return true;
        }
        // Error covers both "pipe unreachable" and "service threw" — ping to tell them apart.
        bool serviceDown = resp.Status == ResponseStatus.Error && !await SessionState.Current.Client.IsReachableAsync();
        NextBtn.IsEnabled = true;
        if (serviceDown)
            Step2Fail(Loc.S("Onb_ServiceDown"));
        else
            Step2Fail(string.IsNullOrWhiteSpace(resp.Message)
                ? Loc.S("Onb_PwdFailed")
                : $"{Loc.S("Onb_PwdFailed")} ({resp.Message})");
        return false;
    }

    private async Task<bool> SaveRecoveryIfProvidedAsync()
    {
        if (string.IsNullOrWhiteSpace(RecQuestion.Text) && RecAnswer.Password.Length == 0)
            return true; // nothing entered — treat as skip
        if (string.IsNullOrWhiteSpace(RecQuestion.Text) || RecAnswer.Password.Trim().Length < 2)
            return true; // incomplete — silently skip rather than block onboarding

        // During onboarding the master was just set; config-change protection allows this via the password we know.
        await SessionState.Current.Client.SetupRecoveryAsync(RecQuestion.Text.Trim(), RecAnswer.Password, adminToken: null);
        return true;
    }

    private async Task FinishAsync()
    {
        await SessionState.Current.Client.CompleteOnboardingAsync();
        StartupManager.SetRunAtLogin(true, startMinimized: true);
        await SessionState.Current.RefreshAsync();
    }

    private void Step2Fail(string msg) { Step2Error.Text = msg; Step2Error.Visibility = Visibility.Visible; }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Bastion.Core.Ipc;
using Bastion.Ui.Localization;
using Bastion.Ui.Services;

namespace Bastion.Gatekeeper;

public partial class PromptWindow : Window
{
    private readonly ServiceClient _client;
    private readonly string _image, _target, _arguments;
    private readonly int _session;
    private bool _syncing;
    private bool _busy;
    private bool _launched;
    private DispatcherTimer? _cooldownTimer;

    public PromptWindow(ServiceClient client, string image, string target, string arguments, int session,
                        string displayName, bool helloAllowed, bool unavailable)
    {
        InitializeComponent();
        _client = client;
        _image = image;
        _target = target;
        _arguments = arguments;
        _session = session;

        AppName.Text = displayName;

        var icon = IconExtractor.FromExecutable(target, 64);
        if (icon is not null) AppIcon.Source = icon;
        else { AppIcon.Visibility = Visibility.Collapsed; IconFallback.Visibility = Visibility.Visible; }

        HelloButton.Visibility = helloAllowed ? Visibility.Visible : Visibility.Collapsed;

        Opacity = 0;
        Loaded += OnLoaded;
        PreviewKeyDown += OnKeyDown;

        if (unavailable) EnterUnavailableState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        Password.Focus();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); }
        else if (e.Key == Key.Enter && !_busy) { _ = UnlockAsync(); }
    }

    // ---- password reveal plumbing --------------------------------------

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        PasswordReveal.Text = Password.Password;
        _syncing = false;
        UpdatePlaceholder();
    }

    private void Reveal_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        Password.Password = PasswordReveal.Text;
        _syncing = false;
        UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
        => Placeholder.Visibility = string.IsNullOrEmpty(Password.Password) ? Visibility.Visible : Visibility.Collapsed;

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        bool show = RevealToggle.IsChecked == true;
        if (show)
        {
            PasswordReveal.Text = Password.Password;
            PasswordReveal.Visibility = Visibility.Visible;
            Password.Visibility = Visibility.Collapsed;
            PasswordReveal.CaretIndex = PasswordReveal.Text.Length;
            PasswordReveal.Focus();
        }
        else
        {
            Password.Visibility = Visibility.Visible;
            PasswordReveal.Visibility = Visibility.Collapsed;
            Password.Focus();
        }
    }

    // ---- actions -------------------------------------------------------

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Unlock_Click(object sender, RoutedEventArgs e) => _ = UnlockAsync();

    private async Task UnlockAsync()
    {
        var pw = Password.Password;
        if (string.IsNullOrEmpty(pw)) { Shake(); return; }
        await TryUnlockAsync(pw);
    }

    private async void Hello_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var pw = await HelloService.VerifyAndUnsealAsync($"Unlock {AppName.Text}");
        if (string.IsNullOrEmpty(pw))
        {
            ShowError(Loc.S("GK_HelloFailed"));
            return;
        }
        await TryUnlockAsync(pw);
    }

    private async Task TryUnlockAsync(string password)
    {
        SetBusy(true);
        HideFeedback();
        var resp = await _client.RequestLaunchAsync(_image, _target, _arguments, _session, password);
        SetBusy(false);

        switch (resp.Status)
        {
            case ResponseStatus.Ok:
                EnterSuccessState();
                break;
            case ResponseStatus.Cooldown:
                StartCooldown(resp.CooldownSeconds);
                break;
            case ResponseStatus.Denied:
            case ResponseStatus.NeedAuthentication:
                Shake();
                ClearPassword();
                ShowError(Loc.S("GK_Incorrect"));
                break;
            default:
                ShowError(Loc.S("GK_CouldNotOpen"));
                break;
        }
    }

    // ---- state -----------------------------------------------------------

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UnlockButton.IsEnabled = !busy;
        Password.IsEnabled = !busy;
        PasswordReveal.IsEnabled = !busy;
        HelloButton.IsEnabled = !busy;
        UnlockButton.Content = busy ? Loc.S("GK_Verifying") : Loc.S("GK_Unlock");
    }

    private void EnterSuccessState()
    {
        _launched = true;
        HideFeedback();
        UnlockButton.Content = Loc.S("GK_Identity");
        UnlockButton.IsEnabled = false;
        Password.IsEnabled = false;
        RevealToggle.IsEnabled = false;
        HelloButton.IsEnabled = false;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(430) };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private void EnterUnavailableState()
    {
        Subtitle.Text = Loc.S("GK_UnavailableSub");
        Password.IsEnabled = false;
        RevealToggle.IsEnabled = false;
        UnlockButton.IsEnabled = false;
        HelloButton.Visibility = Visibility.Collapsed;
        ShowError(Loc.S("GK_UnavailableErr"));
    }

    private void StartCooldown(int seconds)
    {
        int remaining = Math.Max(1, seconds);
        SetBusy(true);
        UnlockButton.Content = Loc.S("GK_Locked");
        Password.IsEnabled = false;

        void Tick()
        {
            if (remaining <= 0)
            {
                _cooldownTimer?.Stop();
                SetBusy(false);
                Password.IsEnabled = true;
                HideFeedback();
                Password.Focus();
                return;
            }
            ShowError(Loc.F("GK_TooMany", remaining));
            remaining--;
        }

        Tick();
        _cooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cooldownTimer.Tick += (_, _) => Tick();
        _cooldownTimer.Start();
    }

    private void ShowError(string message)
    {
        Feedback.Text = message;
        Feedback.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger");
        Feedback.Visibility = Visibility.Visible;
    }

    private void HideFeedback() => Feedback.Visibility = Visibility.Collapsed;

    private void ClearPassword()
    {
        Password.Clear();
        PasswordReveal.Clear();
        UpdatePlaceholder();
        Password.Focus();
    }

    private void Shake()
    {
        var sb = new Storyboard();
        var anim = new DoubleAnimationUsingKeyFrames();
        double[] offsets = { 0, -8, 7, -5, 4, -2, 0 };
        var t = 0.0;
        foreach (var o in offsets)
        {
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(o, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
            t += 45;
        }
        Storyboard.SetTarget(anim, ShakeT);
        Storyboard.SetTargetProperty(anim, new PropertyPath("X"));
        sb.Children.Add(anim);
        sb.Begin();
    }
}

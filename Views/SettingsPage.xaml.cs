using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Traymote.Core;
using Traymote.Services;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Core;

namespace Traymote.Views;

/// <summary>Settings shown inside the flyout; there are too few to earn a window.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly Action _goBack;
    private readonly ResourceLoader _resources = new();
    private bool _loading = true;
    private bool _loadingHotKey;
    private string? _hotKeyMessage;

    public SettingsPage(Action goBack)
    {
        _goBack = goBack;
        InitializeComponent();
        VersionText.Text = $"{App.DisplayName} {GetVersion()}";
    }

    /// <summary>The page appeared; the startup task may have been changed in Windows Settings meanwhile.</summary>
    public void OnShown()
    {
        ShowHotKeyState();
        _ = LoadStartupStateAsync();
        BackButton.Focus(FocusState.Programmatic);
    }

    internal bool IsCapturingHotKey { get; private set; }

    internal void OnHidden()
    {
        if (IsCapturingHotKey)
        {
            IsCapturingHotKey = false;
            _hotKeyMessage = null;
            App.Current.SetHotKeyCapture(false);
            ShowHotKeyState();
        }
    }

    private void ShowHotKeyState()
    {
        _loadingHotKey = true;
        HotKeyToggle.IsOn = SettingsService.HotKeyEnabled;
        HotKeyButton.IsEnabled = SettingsService.HotKeyEnabled;
        HotKeyButton.Content = IsCapturingHotKey
            ? _resources.GetString("HotKeyCapturePrompt")
            : FormatHotKey(SettingsService.HotKey);
        AutomationProperties.SetName(HotKeyButton, string.Format(CultureInfo.CurrentCulture,
            _resources.GetString("HotKeyButtonNameFormat"), HotKeyButton.Content));
        string message = _hotKeyMessage ?? (IsCapturingHotKey ? "HotKeyCaptureHelp"
            : !SettingsService.HotKeyEnabled ? "HotKeyDisabled"
            : App.Current.HotKeyError == 1409 ? "HotKeyConflict"
            : App.Current.HotKeyError != 0 ? "HotKeyUnavailable"
            : "HotKeyReady");
        HotKeyDescription.Text = _resources.GetString(message);
        _loadingHotKey = false;
    }

    private string FormatHotKey(HotKey hotKey)
    {
        List<string> parts = [];
        if ((hotKey.Modifiers & HotKey.Control) != 0) parts.Add(_resources.GetString("HotKeyControl"));
        if ((hotKey.Modifiers & HotKey.Alt) != 0) parts.Add(_resources.GetString("HotKeyAlt"));
        if ((hotKey.Modifiers & HotKey.Shift) != 0) parts.Add(_resources.GetString("HotKeyShift"));
        if ((hotKey.Modifiers & HotKey.Win) != 0) parts.Add(_resources.GetString("HotKeyWin"));
        parts.Add(hotKey.VirtualKey switch
        {
            >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)hotKey.VirtualKey).ToString(),
            >= 0x70 and <= 0x87 => string.Format(CultureInfo.CurrentCulture,
                _resources.GetString("HotKeyFunctionKey"), hotKey.VirtualKey - 0x6F),
            0x20 => _resources.GetString("HotKeySpace"),
            _ => string.Format(CultureInfo.CurrentCulture, _resources.GetString("HotKeyVirtualKey"), hotKey.VirtualKey),
        });
        return string.Join("+", parts);
    }

    private void HotKeyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingHotKey)
        {
            return;
        }

        bool enabled = HotKeyToggle.IsOn;
        OnHidden();
        _hotKeyMessage = SettingsService.TrySaveHotKey(enabled, SettingsService.HotKey)
            ? null : "HotKeySaveFailed";
        ShowHotKeyState();
    }

    private void HotKeyButton_Click(object sender, RoutedEventArgs e)
    {
        IsCapturingHotKey = true;
        _hotKeyMessage = null;
        App.Current.SetHotKeyCapture(true);
        HotKeyButton.Focus(FocusState.Programmatic);
        ShowHotKeyState();
    }

    private void HotKeyButton_LostFocus(object sender, RoutedEventArgs e) => OnHidden();

    private void HotKeyButton_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsCapturingHotKey)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            OnHidden();
            return;
        }

        uint key = (uint)e.Key;
        if (HotKey.IsModifierKey(key))
        {
            return;
        }

        uint modifiers = 0;
        if (IsDown(VirtualKey.Control)) modifiers |= HotKey.Control;
        if (IsDown(VirtualKey.Menu)) modifiers |= HotKey.Alt;
        if (IsDown(VirtualKey.Shift)) modifiers |= HotKey.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= HotKey.Win;
        HotKey candidate = new(modifiers, key);
        if (!candidate.IsValid)
        {
            _hotKeyMessage = "HotKeyInvalid";
            ShowHotKeyState();
            return;
        }

        _hotKeyMessage = SettingsService.TrySaveHotKey(true, candidate) ? null : "HotKeySaveFailed";
        IsCapturingHotKey = false;
        App.Current.SetHotKeyCapture(false);
        ShowHotKeyState();
    }

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private static string GetVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }

    private async Task LoadStartupStateAsync() => ShowStartupState(await StartupService.GetStateAsync());

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user (Task Manager, Settings > Apps > Startup) or policy has the final say, so say
        // where to change it instead of offering a dead toggle.
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "Keep the remote ready after you sign in.",
        };
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _goBack();
}

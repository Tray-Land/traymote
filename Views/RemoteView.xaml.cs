using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Traymote.Services;
using Windows.System;

namespace Traymote.Views;

/// <summary>
/// The remote itself, plus the device picker it falls back to when no Roku is known.
/// </summary>
public sealed partial class RemoteView : UserControl, IDisposable
{
    private RemoteService _remote = null!;
    private bool _isDiscovering;

    public RemoteView()
    {
        InitializeComponent();
    }

    internal void Initialize(RemoteService remote)
    {
        _remote = remote;
        _remote.Changed += OnRemoteChanged;
        UpdateStatus();
    }

    /// <summary>The service outlives this view; unsubscribe so a closed flyout can be collected.</summary>
    public void Dispose() => _remote.Changed -= OnRemoteChanged;

    private void OnRemoteChanged(object? sender, EventArgs e) => UpdateStatus();

    /// <summary>Called each time the flyout opens.</summary>
    internal void OnShown(bool showDevices)
    {
        if (showDevices || (_remote.Device is null && _remote.State != ConnectionState.Searching))
        {
            ShowDevices();
            return;
        }

        ShowRemote();
        if (_remote.State == ConnectionState.Offline)
            _ = _remote.ReconnectAsync();
    }

    private void ShowRemote()
    {
        DevicesPanel.Visibility = Visibility.Collapsed;
        RemotePanel.Visibility = Visibility.Visible;

        FocusRemote();
    }

    /// <summary>
    /// Focus something that isn't the text box, so the keyboard shortcuts work right away. Also
    /// called on window activation: focusing before the window is active leaves XAML with no
    /// keyboard target, so arrow keys do nothing until a click.
    /// </summary>
    internal void FocusRemote()
    {
        if (RemotePanel.Visibility == Visibility.Visible && TextEntry.FocusState == FocusState.Unfocused)
            OkButton.Focus(FocusState.Programmatic);
    }

    private async void ShowDevices()
    {
        RemotePanel.Visibility = Visibility.Collapsed;
        DevicesPanel.Visibility = Visibility.Visible;
        DevicesBackButton.Visibility = _remote.Device is null ? Visibility.Collapsed : Visibility.Visible;
        ManualErrorText.Visibility = Visibility.Collapsed;
        await RefreshDevicesAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        if (_isDiscovering)
            return;

        _isDiscovering = true;
        RescanButton.IsEnabled = false;
        SearchingPanel.Visibility = Visibility.Visible;
        NoDevicesText.Visibility = Visibility.Collapsed;
        DeviceList.ItemsSource = null;

        IReadOnlyList<RokuDevice> devices = await _remote.DiscoverAsync();

        DeviceList.ItemsSource = devices;
        SearchingPanel.Visibility = Visibility.Collapsed;
        NoDevicesText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RescanButton.IsEnabled = true;
        _isDiscovering = false;
    }

    private void UpdateStatus()
    {
        RokuDevice? device = _remote.Device;
        DeviceNameText.Text = device?.Name ?? "No Roku selected";

        (string status, string brushKey) = _remote.State switch
        {
            ConnectionState.Connected => ($"Connected · {device?.Host}", "SystemFillColorSuccessBrush"),
            ConnectionState.Searching => ("Looking for your Roku…", "SystemFillColorCautionBrush"),
            ConnectionState.Offline => ("Offline", "SystemFillColorCriticalBrush"),
            _ => ("Choose a Roku to get started", "SystemFillColorNeutralBrush"),
        };

        StatusText.Text = status;
        if (Application.Current.Resources.TryGetValue(brushKey, out object brush))
            StatusDot.Fill = (Brush)brush;

        ErrorBar.Message = _remote.Error ?? string.Empty;
        ErrorBar.IsOpen = true;
        ErrorOverlay.Visibility = _remote.Error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ErrorBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) =>
        ErrorOverlay.Visibility = Visibility.Collapsed;

    private async void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
            await _remote.SendKeyAsync(key);
    }

    private async void SendText_Click(object sender, RoutedEventArgs e) => await SendTextAsync();

    private async void TextEntry_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        await SendTextAsync();
    }

    private async Task SendTextAsync()
    {
        string text = TextEntry.Text;
        if (text.Length == 0)
            return;

        TextEntry.Text = string.Empty;
        await _remote.SendTextAsync(text);
    }

    /// <summary>
    /// Keyboard shortcuts for the remote, so the flyout can be driven without the mouse.
    /// Skipped while typing in a text box.
    /// </summary>
    private async void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (RemotePanel.Visibility != Visibility.Visible || e.OriginalSource is TextBox)
            return;

        string? key = e.Key switch
        {
            VirtualKey.Up => RokuClient.KeyUp,
            VirtualKey.Down => RokuClient.KeyDown,
            VirtualKey.Left => RokuClient.KeyLeft,
            VirtualKey.Right => RokuClient.KeyRight,
            VirtualKey.Enter => RokuClient.KeySelect,
            VirtualKey.Back => RokuClient.KeyBack,
            VirtualKey.H => RokuClient.KeyHome,
            VirtualKey.Space => RokuClient.KeyPlay,
            VirtualKey.I or VirtualKey.Multiply => RokuClient.KeyInfo,
            (VirtualKey)188 => RokuClient.KeyRev, // ,
            (VirtualKey)190 => RokuClient.KeyFwd, // .
            _ => null,
        };

        if (key is null)
            return;

        e.Handled = true;
        await _remote.SendKeyAsync(key);
    }

    private void ChooseDevice_Click(object sender, RoutedEventArgs e) => ShowDevices();

    private void DevicesBack_Click(object sender, RoutedEventArgs e) => ShowRemote();

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private void Reconnect_Click(object sender, RoutedEventArgs e) => _ = _remote.ReconnectAsync();

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApp();

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void DeviceList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RokuDevice device)
        {
            _remote.Select(device);
            ShowRemote();
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectManuallyAsync();

    private async void ManualHostBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        await ConnectManuallyAsync();
    }

    private async Task ConnectManuallyAsync()
    {
        ManualErrorText.Visibility = Visibility.Collapsed;
        ConnectButton.IsEnabled = false;

        bool connected = await _remote.ConnectToHostAsync(ManualHostBox.Text);

        ConnectButton.IsEnabled = true;
        if (connected)
        {
            ManualHostBox.Text = string.Empty;
            ShowRemote();
            return;
        }

        ManualErrorText.Text = "Couldn't reach a Roku at that address.";
        ManualErrorText.Visibility = Visibility.Visible;
    }
}

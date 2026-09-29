using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Traymote.Services;
using Traymote.Views;
using WinUIEx;

namespace Traymote;

/// <summary>
/// Tray-only application: no window at launch, just a notification-area icon that toggles the
/// flyout, which is the remote. The flyout exists only while it's in use; the idle app is an
/// icon and little else.
/// </summary>
public partial class App : Application
{
    public const string DisplayName = "Traymote";

    private const string MutexName = @"Local\Traymote_SingleInstance";
    private const string ShowEventName = @"Local\Traymote_ShowFlyout";
    private const int MaxTooltipLength = 127; // NOTIFYICONDATA.szTip limit

    // Startup leaves JIT and discovery state behind that the idle app never touches again.
    private static readonly TimeSpan StartupTrimDelay = TimeSpan.FromSeconds(30);

    private RemoteService? _remote;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private TrayIcon? _trayIcon;
    private TrayFlyoutWindow? _flyout;
    private DispatcherQueue? _dispatcher;
    private bool _isExiting;

    public App()
    {
        InitializeComponent();

        // A tray app lives on after its windows close; only the Exit menu ends it.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
    }

    public static new App Current => (App)Application.Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance owns the tray icon: ask it to open its flyout, then quit.
            try
            {
                // Let the running instance take the foreground when it shows its flyout.
                Windows.Win32.PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
                using EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch
            {
                // The other instance may be shutting down; nothing to do.
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Exit();
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => _dispatcher.TryEnqueue(() => ShowFlyout()), null, Timeout.Infinite, executeOnlyOnce: false);

        _remote = new RemoteService();
        _remote.Changed += (_, _) => UpdateTooltip();

        InitializeTrayIcon();
        StartAsync();
    }

    public void ShowFlyout(bool showDevices = false)
    {
        TrayFlyoutWindow flyout = EnsureFlyout();
        flyout.ShowMainPage();
        if (!flyout.IsPopupVisible)
        {
            flyout.ShowPopup(showDevices);
        }
    }

    /// <summary>Shows the flyout on its settings page.</summary>
    public void ShowSettings()
    {
        TrayFlyoutWindow flyout = EnsureFlyout();
        flyout.ShowSettingsPage();
        if (!flyout.IsPopupVisible)
        {
            flyout.ShowPopup();
        }
    }

    public void ExitApp()
    {
        _isExiting = true;
        _flyout?.CloseWindow();

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Exit();
    }

    private async void StartAsync()
    {
        bool firstRun = !_remote!.HasRememberedDevice;
        await _remote.ReconnectAsync();

        // First run with no single obvious Roku: open the picker so the user isn't left
        // wondering where the app went. Otherwise shed the startup garbage.
        if (firstRun && _remote.Device is null)
        {
            ShowFlyout(showDevices: true);
        }
        else
        {
            MemoryService.ReleaseIdle(StartupTrimDelay);
        }
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TrayIcon(1, TrayIconPath, DisplayName);
        _trayIcon.Selected += (_, _) => EnsureFlyout().Toggle();
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        WindowPlacementService.SetTrayIcon(_trayIcon);
        SystemThemeService.Changed += (_, _) => _dispatcher?.TryEnqueue(() => _trayIcon?.SetIcon(TrayIconPath));
    }

    // A white glyph disappears on a light taskbar, which follows the Windows theme, not the app theme.
    private static string TrayIconPath => Path.Combine(
        AppContext.BaseDirectory, "Assets", SystemThemeService.IsLight ? "AppIcon-dark.ico" : "AppIcon.ico");

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        MenuFlyoutItem open = new() { Text = "Open remote", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => ShowFlyout();
        MenuFlyoutItem choose = new() { Text = "Choose Roku…", Icon = new FontIcon { Glyph = "" } };
        choose.Click += (_, _) => ShowFlyout(showDevices: true);
        MenuFlyoutItem settings = new() { Text = "Settings", Icon = new FontIcon { Glyph = "" } };
        settings.Click += (_, _) => ShowSettings();
        MenuFlyoutItem exit = new() { Text = "Exit", Icon = new FontIcon { Glyph = "" } };
        exit.Click += (_, _) => ExitApp();

        MenuFlyout menu = new();
        menu.Items.Add(open);
        menu.Items.Add(choose);
        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);
        args.Flyout = menu;
    }

    private void UpdateTooltip()
    {
        if (_trayIcon is null || _remote is null)
        {
            return;
        }

        string tooltip = _remote.Device is { } device ? $"{DisplayName}\n{device.Name}" : DisplayName;
        _trayIcon.Tooltip = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
    }

    private TrayFlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            // The flyout closes itself after staying hidden for a while; the next open builds a new one.
            _flyout = new TrayFlyoutWindow(_remote!);
            _flyout.Closed += (_, _) =>
            {
                _flyout = null;
                if (!_isExiting)
                {
                    MemoryService.ReleaseIdle();
                }
            };
        }

        return _flyout;
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "Traymote-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Never throw from the crash logger.
        }
    }
}

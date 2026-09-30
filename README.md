# Traymote

A Roku remote that lives in the Windows system tray.

Click the tray icon and a small flyout opens next to the taskbar with a full Roku remote. Click away and it disappears. Traymote finds your Roku on the local network, remembers it, and sends button presses over Roku's [External Control Protocol (ECP)](https://developer.roku.com/docs/developer-program/dev-tools/external-control-api.md). It needs no account and no cloud service. All traffic stays on your LAN.

## Features

- **Tray-only.** There is no main window. The tray icon is the app, and Exit is in the flyout's menu.
- **Auto-discovery.** Finds Rokus with an SSDP `roku:ecp` search. It searches from every local IPv4 interface, so VPN and Hyper-V adapters don't hide your device.
- **Manual connect.** If discovery can't see your Roku, type its IP address (`192.168.1.50`, `192.168.1.50:8060` and full URLs all work).
- **Full remote.** D-pad, Select, Back, Home, Play/Pause, Rewind, Fast Forward and the `*` options button.
- **Keyboard control.** With the flyout focused, use the arrow keys, `Enter` (Select), `Backspace` (Back), `H` (Home), `Space` (Play/Pause), `,` (Rewind), `.` (Fast Forward) and `I` or `*` (Options).
- **Text entry.** Type into Roku search boxes from your PC keyboard.
- **Remembers your device** and reconnects automatically. Reconnect and device switching are in the flyout menu.
- **Start with Windows**, toggled from the in-flyout settings page.
- **Shell-style flyout** that follows the system light and dark theme, with light and dark tray icons.
- **Light on resources.** The flyout is closed after a minute hidden, and Release builds tune GC, trimming, and globalization for a small idle footprint.

## Requirements

- Windows 10 version 1809 (10.0.17763) or later. Windows 11 is the primary target.
- x64 or ARM64.
- A Roku on the same network as your PC, with **Control by mobile apps** set to *Default* or *Permissive*. On the Roku, go to Settings › System › Advanced system settings › Control by mobile apps › Network access.

To find a Roku's IP address, go to Settings › Network › About on the Roku.

## Building

Prerequisites:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows **Developer Mode** turned on
- The [`winapp` CLI](https://github.com/microsoft/WinAppCli). It's pulled in through the `Microsoft.Windows.SDK.BuildTools.WinApp` package, so `dotnet run` works without a separate install.

```powershell
$arch = $env:PROCESSOR_ARCHITECTURE
$Platform = if ($arch -eq 'AMD64') { 'x64' } else { $arch }

# Build
dotnet build -c Debug -p:Platform=$Platform

# Run with package identity (registers a loose-layout package via winapp)
dotnet run -c Debug -p:Platform=$Platform
```

Traymote is an MSIX-packaged app and needs package identity to run. Use `dotnet run` or `winapp run`. Don't register the package by hand. To test first-run behavior, use `winapp run <build-output> --clean`, which wipes local settings.

## Releasing

`build-msix-for-gh.ps1` builds and signs an MSIX and publishes it as a GitHub release:

```powershell
.\build-msix-for-gh.ps1                 # build, sign, tag and create a GitHub release
.\build-msix-for-gh.ps1 -SkipRelease    # build and sign only
.\build-msix-for-gh.ps1 -Prerelease -Notes "First preview"
```

The release tag is derived from the version in `Package.appxmanifest`. The script needs an authenticated `gh` CLI.

## Project layout

```
App.xaml(.cs)               Tray icon, single-instance handling, and the only exit path
Views/
  TrayFlyoutWindow          Shell-style flyout window, hidden on dismiss
  RemoteView                The remote, device picker and text entry
  SettingsPage              In-flyout settings
Services/
  RokuClient                Minimal ECP client (keypress, text, device-info)
  RokuDiscovery             SSDP discovery across all local interfaces
  RemoteService             Connection state, remembered device, key/text sending
  SettingsService           Persisted settings (remembered Roku)
  StartupService            Start-with-Windows registration
  SystemThemeService        Light/dark theme tracking for UI and tray icon
  WindowPlacementService    Positions the flyout near the taskbar/tray
  MemoryService             Returns memory to Windows when idle
Controls/ShellBackdrop.cs   Flyout backdrop styling
branding/                   Source SVGs for the app icon
```

## How it talks to the Roku

Traymote uses only Roku's documented local ECP API on port `8060`:

| Purpose | Request |
|---|---|
| Discover | SSDP `M-SEARCH` to `239.255.255.250:1900` with `ST: roku:ecp` |
| Identify | `GET /query/device-info` |
| Press a button | `POST /keypress/<Key>` |
| Type text | `POST /keypress/Lit_<char>` for each character |

## Tech

WinUI 3 on the Windows App SDK, .NET 10, MSIX packaging, [WinUIEx](https://github.com/dotMorten/WinUIEx), and CsWin32 for Win32 interop.

## License

[MIT](LICENSE) © 2026 Joseph Finney

Traymote is an independent project and is not affiliated with or endorsed by Roku, Inc. Roku is a trademark of Roku, Inc.

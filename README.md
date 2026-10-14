# TrayWiz

Control your [WiZ](https://www.wizconnected.com/) smart bulbs from the Windows system tray. TrayWiz is a small WinUI 3 app with no main window: click the tray icon, and a flyout styled like the Windows shell flyouts opens next to the taskbar.

## What it does

- **Finds your bulbs automatically.** It broadcasts a discovery request on your local network at startup, and again when you open the flyout with no bulbs known. There is no account and no cloud.
- **Controls each bulb.** On/off, brightness, color temperature, RGB color (presets plus a picker), and the built-in WiZ scenes. Tunable-white bulbs only show the controls and scenes they can render.
- **Groups.** Put bulbs into named groups, reorder them, and switch a whole group at once.
- **Extra tray icons.** Right-click a light, group, or custom button and choose *Add new tray icon*. Each icon has its own glyph and its own left click, right click, and double-clicks: do nothing, open TrayWiz, toggle, swap between brightness levels or colors, or run the button. Left or right click must show the icon's menu, which is where you edit or remove it.
- **Remembers your setup.** Names, last-known IP addresses (keyed by MAC), and groups persist across runs, because bulbs don't store names themselves.
- **Tray menu.** Right-click the icon for *Open TrayWiz*, *All on*, *All off*, *Search for bulbs*, *Start with Windows*, and *Exit*.
- **Follows the taskbar theme.** The tray icon switches between a light and dark glyph to match the Windows taskbar, and the flyout matches the shell's acrylic.
- **Single instance.** Launching it again opens the flyout of the running copy.

The flyout opens when you launch the app (not when Windows starts it at sign-in, which stays quiet in the tray). Left-click the tray icon to toggle the flyout. It hides when you click away.

## Requirements

- Windows 10 version 1809 (build 17763) or later; Windows 11 recommended
- x64 or ARM64
- WiZ bulbs on the same network, with **Allow local communication** turned on in the WiZ app (Settings > Privacy > Allow local communication)

## Install

Download the signed MSIX from the [Releases](https://github.com/Tray-Land/tray-wiz/releases) page. If Windows won't install it, import the signing certificate included with the release into *Trusted People* first, then open the MSIX.

## Privacy and footprint

- **Network:** only UDP on the local network, port 38899, to your own bulbs. The WiZ local protocol is unauthenticated, so anyone on your LAN can already control a bulb with local communication enabled. TrayWiz sends nothing to the internet.
- **Storage:** `%LocalAppData%\TrayWiz\bulbs.json` and `groups.json` (names, IPs, group membership), and `trayicons.json` (extra tray icons). No secrets are stored.
- **Idle cost:** nothing polls in the background. Bulb state is refreshed only when the flyout opens. The flyout hides on dismiss for an instant reopen, is closed after a minute hidden, and the app then collects garbage and trims its working set.
- **Crashes:** unhandled exceptions are appended to `%TEMP%\TrayWiz-crash.log`.

## Build from source

Prerequisites: Windows 11 or 10 with Developer Mode on, the [.NET 10 SDK](https://dotnet.microsoft.com/download), and the [winapp CLI](https://github.com/microsoft/WinAppCli):

```powershell
winget install Microsoft.WinAppCli --source winget
```

Run with package identity (needed for the startup task):

```powershell
winapp run          # or: dotnet run
winapp run --clean  # reset settings to test first run
```

To try the UI against real bulbs without changing them, launch with `--dry-run`: reads still go out, but commands that would change a bulb are counted and dropped.

Build:

```powershell
dotnet build -c Debug -p:Platform=x64   # or ARM64
```

Release: `build-msix-for-gh.ps1` builds signed x64 and ARM64 packages and creates a draft GitHub release.

### Project layout

| Path | Purpose |
|---|---|
| `App.xaml.cs` | Tray icon, context menu, single instance, the only exit path |
| `Controls/TrayPopupWindow` | Borderless flyout: placement, light-dismiss, animation, idle close |
| `Controls/BulbDetailView` | Per-bulb controls |
| `Controls/ShellBackdrop.cs` | Acrylic that matches shell flyouts |
| `MainPage`, `ViewModels/MainViewModel.cs` | Bulb list, groups, scan and refresh |
| `Services/WizClient.cs` | WiZ UDP JSON protocol: discovery, requests, throttled `setPilot` |
| `Services/BulbStore.cs` | Persisted names, IPs, and groups |
| `Services/` (other) | Window placement, system theme, startup task, memory release |
| `branding/` | Icon source SVGs |

Regenerate icons after editing `branding\icon.svg`:

```powershell
winapp manifest update-assets branding\icon-white.svg --light-image branding\icon-black.svg
```

Then rebuild `Assets\AppIcon-dark.ico` from the black SVG.

## License

MIT, see [LICENSE](LICENSE).

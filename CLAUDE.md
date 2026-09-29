@AGENTS.md

## Tray app conventions

This is a tray-only WinUI 3 app following the `winui-tray-app` skill. Load that skill before changing app lifetime, the popup, the tray icon, polling, settings, or secrets.

- Launch with `winapp run` (or `dotnet run`); never register the package by hand. Use `winapp run --clean` to test first-run behavior.
- `App.xaml.cs` owns the tray icon, single instance, and the only exit path (the Exit menu). There is no main window.
- The popup (`Controls/TrayPopupWindow`) is hidden on dismiss and closed after a minute hidden. Anything it subscribes to must be undone in `CloseWindow` (`MainPage.Release` stops x:Bind tracking on the singleton view model).
- Icon source is `branding\icon.svg`. Regenerate with `winapp manifest update-assets branding\icon-white.svg --light-image branding\icon-black.svg`, then rebuild `Assets\AppIcon-dark.ico` from the black SVG.
- Bulb discovery runs once at startup and when the popup opens with no bulbs; state refresh runs only when the popup opens.

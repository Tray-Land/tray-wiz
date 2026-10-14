using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayWiz.Controls;
using TrayWiz.Services;
using TrayWiz.ViewModels;
using Windows.ApplicationModel;
using WinUIEx;

namespace TrayWiz;

/// <summary>
/// Tray-only app: no main window. Left-click the tray icon to toggle the popup,
/// right-click for a quick menu.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "Local\\TrayWiz_SingleInstance";
    private const string ShowEventName = "Local\\TrayWiz_ShowPopup";

    // Startup leaves JIT and first-scan state behind that the idle app never touches again.
    private static readonly TimeSpan StartupTrimDelay = TimeSpan.FromSeconds(30);

    private TrayIcon? _trayIcon;
    private TrayPopupWindow? _popup;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private StartupTaskState? _startupState;
    private bool _isShuttingDown;

    public App()
    {
        InitializeComponent();

        // The popup is hidden, not closed, but make sure the app never quits just because
        // no window is showing.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "TrayWiz-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Never throw from the crash logger.
        }
    }

    public static new App Current => (App)Application.Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WizClient.DryRun = Environment.GetCommandLineArgs().Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Already running: ask that instance to show its popup, then quit.
            try
            {
                // Let the running instance take the foreground when it shows its popup.
                Windows.Win32.PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
                using EventWaitHandle show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
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

        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => _dispatcher.TryEnqueue(() => GetPopup().ShowPopup()), null, Timeout.Infinite, executeOnlyOnce: false);

        _trayIcon = new TrayIcon(1, TrayIconPath, "TrayWiz");
        _trayIcon.Selected += (sender, _) =>
        {
            WindowPlacementService.SetTrayIconSource(sender);
            GetPopup().Toggle(DateTime.UtcNow);
        };
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        WindowPlacementService.SetTrayIconSource(_trayIcon);
        TrayIcons = new TrayIconManager(_dispatcher, ShowPopupAt, EditTrayIcon);
        SystemThemeService.Changed += (_, _) => _dispatcher?.TryEnqueue(() =>
        {
            _trayIcon?.SetIcon(TrayIconPath);
            TrayIcons?.RefreshTheme();
        });
        _ = RefreshStartupStateAsync();

        // Start discovery right away so the list is ready by the first click.
        _ = MainViewModel.Instance.ScanAsync();
        MemoryService.ReleaseIdle(StartupTrimDelay);

        // Opening the popup tells a user who just launched the app where it lives. A silent
        // start with Windows stays in the tray.
        if (!IsStartupTaskLaunch())
            _dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => GetPopup().ShowPopup());
    }

    private static bool IsStartupTaskLaunch()
    {
        try
        {
            return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The icons the user added for individual lights, groups and buttons.</summary>
    internal TrayIconManager? TrayIcons { get; private set; }

    /// <summary>Opens the popup against the given icon, so it appears next to the one that was clicked.</summary>
    private void ShowPopupAt(TrayIcon icon)
    {
        WindowPlacementService.SetTrayIconSource(icon);
        GetPopup().ShowPopup();
    }

    private void EditTrayIcon(SavedTrayIcon saved)
    {
        TrayPopupWindow popup = GetPopup();
        popup.ShowPopup();
        popup.EditTrayIcon(saved);
    }

    // A white glyph disappears on a light taskbar, which follows the Windows theme, not the app theme.
    internal static string TrayIconPath => Path.Combine(
        AppContext.BaseDirectory, "Assets", SystemThemeService.IsLight ? "AppIcon-dark.ico" : "AppIcon.ico");

    private async Task RefreshStartupStateAsync() => _startupState = await StartupService.GetStateAsync();

    private TrayPopupWindow GetPopup()
    {
        if (_popup is null)
        {
            _popup = new TrayPopupWindow();
            _popup.Closed += (_, _) =>
            {
                _popup = null;
                if (!_isShuttingDown)
                    MemoryService.ReleaseIdle();
            };
        }
        return _popup;
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        MainViewModel vm = MainViewModel.Instance;
        MenuFlyout menu = new();

        MenuFlyoutItem open = new() { Text = "Open TrayWiz", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => ShowPopupAt(sender);
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(new MenuFlyoutItem { Text = "All on", Icon = new FontIcon { Glyph = "" }, Command = vm.AllOnCommand });
        menu.Items.Add(new MenuFlyoutItem { Text = "All off", Icon = new FontIcon { Glyph = "" }, Command = vm.AllOffCommand });
        menu.Items.Add(new MenuFlyoutItem { Text = "Search for bulbs", Icon = new FontIcon { Glyph = "" }, Command = vm.ScanCommand });
        menu.Items.Add(new MenuFlyoutSeparator());

        if (_startupState is not null)
        {
            ToggleMenuFlyoutItem startup = new()
            {
                Text = "Start with Windows",
                IsChecked = _startupState is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy,
                IsEnabled = _startupState is StartupTaskState.Enabled or StartupTaskState.Disabled,
            };
            startup.Click += async (_, _) => _startupState = await StartupService.SetEnabledAsync(startup.IsChecked);
            menu.Items.Add(startup);
        }

        MenuFlyoutItem exit = new() { Text = "Exit", Icon = new FontIcon { Glyph = "" } };
        exit.Click += (_, _) => ShutdownAndExit();
        menu.Items.Add(exit);

        args.Flyout = menu;
    }

    /// <summary>The app's only exit path: removes the tray icon and releases single-instance handles.</summary>
    public void ShutdownAndExit()
    {
        if (_isShuttingDown)
            return;
        _isShuttingDown = true;

        _popup?.CloseWindow();
        TrayIcons?.Dispose();
        TrayIcons = null;
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
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayWiz.Controls;
using TrayWiz.Services;
using TrayWiz.ViewModels;
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

    private TrayIcon? _trayIcon;
    private TrayPopupWindow? _popup;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private bool _isShuttingDown;

    public App()
    {
        InitializeComponent();

        // The popup is hidden, not closed, but make sure the app never quits just because
        // no window is showing.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
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
                using EventWaitHandle show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Exit();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ListenForShowRequests();

        _trayIcon = new TrayIcon(1, "Assets/AppIcon.ico", "TrayWiz");
        _trayIcon.Selected += (_, _) => GetPopup().Toggle(DateTime.UtcNow);
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        WindowPlacementService.SetTrayIconSource(_trayIcon);

        // Start discovery right away so the list is ready by the first click.
        _ = MainViewModel.Instance.ScanAsync();
    }

    private TrayPopupWindow GetPopup()
    {
        if (_popup is null)
        {
            _popup = new TrayPopupWindow();
            _popup.Closed += (_, _) => _popup = null;
        }
        return _popup;
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        MainViewModel vm = MainViewModel.Instance;
        MenuFlyout menu = new();

        MenuFlyoutItem open = new() { Text = "Open TrayWiz", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => GetPopup().ShowPopup();
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(new MenuFlyoutItem { Text = "All on", Icon = new FontIcon { Glyph = "" }, Command = vm.AllOnCommand });
        menu.Items.Add(new MenuFlyoutItem { Text = "All off", Icon = new FontIcon { Glyph = "" }, Command = vm.AllOffCommand });
        menu.Items.Add(new MenuFlyoutItem { Text = "Search for bulbs", Icon = new FontIcon { Glyph = "" }, Command = vm.ScanCommand });
        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem exit = new() { Text = "Exit", Icon = new FontIcon { Glyph = "" } };
        exit.Click += (_, _) => ShutdownAndExit();
        menu.Items.Add(exit);

        args.Flyout = menu;
    }

    private void ListenForShowRequests()
    {
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        EventWaitHandle showEvent = _showEvent!;

        Thread listener = new(() =>
        {
            try
            {
                while (showEvent.WaitOne())
                {
                    if (_isShuttingDown)
                        return;
                    dispatcher.TryEnqueue(() => GetPopup().ShowPopup());
                }
            }
            catch (ObjectDisposedException) { }
        })
        {
            IsBackground = true,
            Name = "TrayWiz show-request listener",
        };
        listener.Start();
    }

    /// <summary>The app's only exit path: removes the tray icon and releases single-instance handles.</summary>
    public void ShutdownAndExit()
    {
        if (_isShuttingDown)
            return;
        _isShuttingDown = true;

        _popup?.Close();
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _showEvent?.Set();
        _showEvent?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        Exit();
    }
}

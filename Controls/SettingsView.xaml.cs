using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TrayWiz.Services;
using Windows.ApplicationModel;

namespace TrayWiz.Controls;

/// <summary>App-wide options, shown in the popup like the other detail views.</summary>
public sealed partial class SettingsView : UserControl
{
    // True while the toggle is set in code, so Toggled isn't mistaken for a user change.
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
        VersionText.Text = $"TrayWiz {AppVersion()}";
        Loaded += (_, _) => BackButton.Focus(FocusState.Programmatic);
        _ = LoadStartupAsync();
    }

    public event EventHandler? BackRequested;

    private static string AppVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return typeof(SettingsView).Assembly.GetName().Version?.ToString(3) ?? "";
        }
    }

    private async Task LoadStartupAsync()
    {
        ShowStartup(await StartupService.GetStateAsync());
        _loading = false;
    }

    private void ShowStartup(StartupTaskState? state)
    {
        // Unpackaged runs have no startup task, so there is nothing to offer.
        StartupPanel.Visibility = state is null ? Visibility.Collapsed : Visibility.Visible;
        if (state is null)
            return;

        bool wasLoading = _loading;
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        _loading = wasLoading;

        string? note = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Windows Settings > Apps > Startup. Turn it back on there.",
            StartupTaskState.DisabledByPolicy => "Disabled by your organization.",
            StartupTaskState.EnabledByPolicy => "Required by your organization.",
            _ => null,
        };
        StartupNote.Text = note ?? "";
        StartupNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        // Windows may refuse (for example when the user disabled it), so show what actually happened.
        ShowStartup(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}

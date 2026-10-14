using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TrayWiz.Models;
using TrayWiz.Services;
using TrayWiz.ViewModels;
using Windows.UI;

namespace TrayWiz.Controls;

/// <summary>
/// Three-step editor for a custom action: which lights, what to do to them (limited to what
/// every chosen light supports), then a name and icon. Nothing here talks to a light; the
/// result is handed back through <see cref="Saved"/>.
/// </summary>
public sealed partial class ActionEditorView : UserControl
{
    private const string DefaultGlyph = "";

    private static readonly string[] Icons =
    [
        "", "", "", "", "", "", "",
        "", "", "", "", "",
    ];

    private readonly MainViewModel _viewModel;
    private readonly LightAction? _editing;
    private readonly SavedAction _initial;
    private readonly Dictionary<CheckBox, string> _groupChecks = [];
    private readonly Dictionary<CheckBox, string> _bulbChecks = [];

    // True while controls are being filled in code, so their change events are ignored.
    private bool _loading = true;
    private bool _seeded;
    private bool _isColorPickerArmed;
    private int _step;
    private Color _color = Color.FromArgb(255, 255, 180, 100);

    public ActionEditorView(MainViewModel viewModel, LightAction? editing)
    {
        _viewModel = viewModel;
        _editing = editing;
        _initial = editing?.Settings ?? new SavedAction { Id = Guid.NewGuid().ToString("N") };
        InitializeComponent();

        foreach (BulbGroup group in viewModel.Groups)
        {
            int count = group.Tiles.OfType<Bulb>().Count();
            CheckBox box = new()
            {
                Content = $"{group.Name} ({(count == 1 ? "1 light" : $"{count} lights")})",
                IsChecked = _initial.GroupIds.Contains(group.Id),
            };
            box.Checked += Selection_Changed;
            box.Unchecked += Selection_Changed;
            _groupChecks[box] = group.Id;
            GroupChecks.Children.Add(box);
        }
        GroupsHeading.Visibility = GroupChecks.Visibility = _groupChecks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (Bulb bulb in viewModel.Bulbs.OrderBy(b => b.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            CheckBox box = new()
            {
                Content = bulb.DisplayName,
                IsChecked = _initial.Macs.Contains(bulb.Mac, StringComparer.OrdinalIgnoreCase),
            };
            box.Checked += Selection_Changed;
            box.Unchecked += Selection_Changed;
            _bulbChecks[box] = bulb.Mac;
            BulbChecks.Children.Add(box);
        }
        BulbsHeading.Visibility = BulbChecks.Visibility = _bulbChecks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoLightsText.Visibility = _bulbChecks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        NameBox.Text = _initial.Name;
        IconGrid.ItemsSource = Icons;
        IconGrid.SelectedItem = Icons.Contains(_initial.Glyph) ? _initial.Glyph : DefaultGlyph;

        _loading = false;
        ShowStep(0);
        Loaded += (_, _) => BackButton.Focus(FocusState.Programmatic);
    }

    public event EventHandler? BackRequested;

    public event EventHandler<SavedAction>? Saved;

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private void Cancel_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        GoBack();
    }

    private void GoBack()
    {
        if (_step == 0)
            BackRequested?.Invoke(this, EventArgs.Empty);
        else
            ShowStep(_step - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < 2)
            ShowStep(_step + 1);
        else
            Saved?.Invoke(this, Build());
    }

    private void ShowStep(int step)
    {
        _step = step;
        TargetsPanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        NamePanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;

        string[] names = ["Lights", "What it does", "Name and icon"];
        TitleText.Text = _editing is null ? "New button" : "Edit button";
        StepText.Text = $"Step {step + 1} of 3 · {names[step]}";
        NextButton.Content = step < 2 ? "Next" : _editing is null ? "Create" : "Save";

        if (step == 1)
            ConfigureSettings();
        UpdateNext();
    }

    private void UpdateNext() =>
        NextButton.IsEnabled = _step switch
        {
            0 => SelectedBulbs().Count > 0,
            1 => PowerSetting == false
                || ((PowerSetting is not null || BrightnessCheck.IsChecked == true || SelectedLook != ActionLook.Unchanged)
                    && (SelectedLook != ActionLook.Scene || SceneCombo.SelectedItem is not null)),
            _ => !string.IsNullOrWhiteSpace(NameBox.Text),
        };

    private List<Bulb> SelectedBulbs()
    {
        HashSet<string> macs = new(StringComparer.OrdinalIgnoreCase);
        foreach (BulbGroup group in _viewModel.Groups.Where(g => CheckedGroupIds().Contains(g.Id)))
            macs.UnionWith(group.Macs);
        macs.UnionWith(CheckedMacs());
        return [.. _viewModel.Bulbs.Where(b => macs.Contains(b.Mac))];
    }

    private HashSet<string> CheckedGroupIds() => [.. _groupChecks.Where(p => p.Key.IsChecked == true).Select(p => p.Value)];

    private List<string> CheckedMacs() => [.. _bulbChecks.Where(p => p.Key.IsChecked == true).Select(p => p.Value)];

    private bool? PowerSetting => PowerChoice.SelectedIndex switch { 1 => true, 2 => false, _ => null };

    private ActionLook SelectedLook =>
        (LookCombo.SelectedItem as ComboBoxItem)?.Tag is ActionLook look ? look : ActionLook.Unchanged;

    /// <summary>Offers only the settings every selected light can do.</summary>
    private void ConfigureSettings()
    {
        List<Bulb> targets = SelectedBulbs();
        bool allRgb = targets.All(b => b.IsRgb);
        double min = targets.Max(b => b.MinTemperature);
        double max = targets.Min(b => b.MaxTemperature);
        bool allTemperature = targets.All(b => b.SupportsTemperature) && min < max;

        ActionLook previousLook = _seeded ? SelectedLook : _initial.Look;
        int? previousScene = _seeded ? (SceneCombo.SelectedItem as WizScene)?.Id : _initial.SceneId;
        double previousTemperature = _seeded ? TemperatureSlider.Value : _initial.Temperature ?? 2700;

        _loading = true;
        if (!_seeded)
        {
            PowerChoice.SelectedIndex = _initial.Power switch { true => 1, false => 2, _ => 0 };
            BrightnessCheck.IsChecked = _initial.Brightness is not null;
            BrightnessSlider.Value = _initial.Brightness ?? 100;
            if (Bulb.TryParseHex(_initial.Color, out Color saved))
                _color = saved;
            ColorPreview.Fill = new SolidColorBrush(_color);
            _seeded = true;
        }

        List<ComboBoxItem> looks = [new() { Content = "Leave as is", Tag = ActionLook.Unchanged }];
        if (allTemperature)
            looks.Add(new() { Content = "White temperature", Tag = ActionLook.White });
        if (allRgb)
            looks.Add(new() { Content = "Color", Tag = ActionLook.Color });
        looks.Add(new() { Content = "Scene", Tag = ActionLook.Scene });
        LookCombo.ItemsSource = looks;
        LookCombo.SelectedItem = looks.FirstOrDefault(i => (ActionLook)i.Tag == previousLook) ?? looks[0];

        TemperatureSlider.Maximum = allTemperature ? max : 6500;
        TemperatureSlider.Minimum = allTemperature ? min : 2200;
        TemperatureSlider.Value = Math.Clamp(previousTemperature, TemperatureSlider.Minimum, TemperatureSlider.Maximum);

        IReadOnlyList<WizScene> scenes = allRgb ? WizScene.All : WizScene.White;
        SceneCombo.ItemsSource = scenes;
        SceneCombo.SelectedItem = scenes.FirstOrDefault(s => s.Id == previousScene);

        CapabilityText.Text = targets.Count == 1
            ? "Showing what this light supports."
            : $"Showing what all {targets.Count} selected lights support.";
        _loading = false;

        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        ActionLook look = SelectedLook;
        LightingPanel.Visibility = PowerSetting == false ? Visibility.Collapsed : Visibility.Visible;
        BrightnessSlider.IsEnabled = BrightnessCheck.IsChecked == true;
        TemperatureSlider.Visibility = look == ActionLook.White ? Visibility.Visible : Visibility.Collapsed;
        ColorPanel.Visibility = look == ActionLook.Color ? Visibility.Visible : Visibility.Collapsed;
        SceneCombo.Visibility = look == ActionLook.Scene ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Selection_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            UpdateNext();
    }

    private void PowerChoice_SelectionChanged(object sender, SelectionChangedEventArgs e) => Setting_Changed(sender, e);

    private void LookCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => Setting_Changed(sender, e);

    private void Setting_Changed(object sender, object e)
    {
        if (_loading)
            return;

        ApplyVisibility();
        UpdateNext();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
            UpdateNext();
    }

    private void PresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && Bulb.TryParseHex(hex, out Color color))
            SetColor(color);
    }

    private void SetColor(Color color)
    {
        _color = color;
        ColorPreview.Fill = new SolidColorBrush(color);
    }

    private void ColorFlyout_Opening(object? sender, object e)
    {
        _isColorPickerArmed = false;
        CustomColorPicker.Color = _color;
        _isColorPickerArmed = true;
    }

    private void ColorFlyout_Closed(object? sender, object e) => _isColorPickerArmed = false;

    private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_isColorPickerArmed && args.NewColor != _color)
            SetColor(args.NewColor);
    }

    private SavedAction Build()
    {
        bool? power = PowerSetting;
        ActionLook look = power == false ? ActionLook.Unchanged : SelectedLook;

        // Bulbs this editor has no checkbox for (not found yet) stay in the action.
        List<string> macs = CheckedMacs();
        macs.AddRange(_initial.Macs.Where(m => !_bulbChecks.Values.Contains(m, StringComparer.OrdinalIgnoreCase)));

        return new SavedAction
        {
            Id = _initial.Id,
            Name = NameBox.Text.Trim(),
            Glyph = IconGrid.SelectedItem as string ?? DefaultGlyph,
            GroupIds = [.. CheckedGroupIds()],
            Macs = macs,
            Power = power,
            Brightness = power != false && BrightnessCheck.IsChecked == true ? (int)BrightnessSlider.Value : null,
            Look = look,
            Temperature = look == ActionLook.White ? (int)TemperatureSlider.Value : null,
            Color = look == ActionLook.Color ? $"{_color.R:X2}{_color.G:X2}{_color.B:X2}" : null,
            SceneId = look == ActionLook.Scene && SceneCombo.SelectedItem is WizScene scene ? scene.Id : null,
        };
    }
}

using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using TrayWiz.Models;
using Windows.System;
using Windows.UI;

namespace TrayWiz.Controls;

/// <summary>
/// Full settings for one bulb, shown in place of the tile grid (like a Quick Settings detail page).
/// Create a new instance per bulb; the view is never rebound to a different bulb.
/// </summary>
public sealed partial class BulbDetailView : UserControl
{
    /// <summary>
    /// Key of the connected animation that carries a tile's whole face between the tile and <see cref="CardElement"/>.
    /// Prepare it before showing the view and the glyph flies in; the light strikes when it lands.
    /// </summary>
    public const string FlightKey = "BulbCard";

    // The picker is only "armed" after its initial color is set, so opening the flyout
    // never pushes a normalized color back to the bulb.
    private bool _isColorPickerArmed;

    private bool _isRenaming;

    public BulbDetailView(Bulb bulb)
    {
        Bulb = bulb;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            BackButton.Focus(FocusState.Programmatic);
            Arrive();
        };
    }

    public event EventHandler? BackRequested;

    public Bulb Bulb { get; }

    /// <summary>The stage card, where the connected animation from a tile lands and leaves from.</summary>
    public UIElement CardElement => Stage;

    // With a flight prepared, the tile grows into the stage while the header and controls ride in alongside it.
    // The stage shows the bulb's state from the start, so a lit bulb is lit throughout.
    private void Arrive() =>
        ConnectedAnimationService.GetForCurrentView().GetAnimation(FlightKey)?.TryStart(Stage, [Header, Body]);

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    // Escape while renaming only cancels the rename; it goes back otherwise.
    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_isRenaming)
            EndRename(commit: false, returnFocus: true);
        else
            BackRequested?.Invoke(this, EventArgs.Empty);
    }

    // The pencil swaps the name for a box holding the name as typed (empty when the bulb has none and shows
    // its default), which Enter or leaving the box saves.
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        _isRenaming = true;
        NameBox.Text = Bulb.Name ?? "";
        NameDisplay.Visibility = Visibility.Collapsed;
        NameBox.Visibility = Visibility.Visible;
        NameBox.UpdateLayout();
        NameBox.Focus(FocusState.Programmatic);
        NameBox.SelectAll();
    }

    private void NameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            EndRename(commit: true, returnFocus: true);
            e.Handled = true;
        }
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e) => EndRename(commit: true, returnFocus: false);

    // Hiding the box raises LostFocus again, so the flag is cleared first.
    private void EndRename(bool commit, bool returnFocus)
    {
        if (!_isRenaming)
            return;

        _isRenaming = false;
        if (commit)
        {
            string? name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();
            if (name != Bulb.Name)
                Bulb.Name = name;
        }

        NameBox.Visibility = Visibility.Collapsed;
        NameDisplay.Visibility = Visibility.Visible;
        if (returnFocus)
            EditButton.Focus(FocusState.Programmatic);
    }

    // Control-to-bulb handlers. A change is only user input when the control is already loaded
    // (initial binding and range clamping happen before Loaded) and the new value differs from
    // the bulb's state (OneWay updates from the bulb always match it).
    private static bool IsUserChange(FrameworkElement control) => control.IsLoaded;

    private void Stage_PowerChanged(object? sender, bool isOn)
    {
        if (isOn != Bulb.IsOn)
            Bulb.SetOn(isOn);
    }

    private void Stage_BrightnessChanged(object? sender, double brightness)
    {
        if (Math.Abs(brightness - Bulb.Brightness) >= 1)
            Bulb.SetBrightness(brightness);
    }

    private void Temperature_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (IsUserChange((Slider)sender) && Math.Abs(e.NewValue - Bulb.Temperature) >= 1)
            Bulb.SetTemperature(e.NewValue);
    }

    private void Scene_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && IsUserChange(combo)
            && combo.SelectedIndex >= 0 && combo.SelectedIndex != Bulb.SceneIndex)
        {
            Bulb.SetScene(combo.SelectedIndex);
        }
    }

    private void PresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            uint rgb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Bulb.SetColor(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }
    }

    private void ColorFlyout_Opening(object? sender, object e)
    {
        _isColorPickerArmed = false;
        CustomColorPicker.Color = Bulb.Color;
        _isColorPickerArmed = true;
    }

    private void ColorFlyout_Closed(object? sender, object e) => _isColorPickerArmed = false;

    private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_isColorPickerArmed && args.NewColor != Bulb.Color)
            Bulb.SetColor(args.NewColor);
    }
}

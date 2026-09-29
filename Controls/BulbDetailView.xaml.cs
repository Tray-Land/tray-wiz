using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
    // The picker is only "armed" after its initial color is set, so opening the flyout
    // never pushes a normalized color back to the bulb.
    private bool _isColorPickerArmed;

    public BulbDetailView(Bulb bulb)
    {
        Bulb = bulb;
        InitializeComponent();
        Loaded += (_, _) => BackButton.Focus(FocusState.Programmatic);
    }

    public event EventHandler? BackRequested;

    public Bulb Bulb { get; }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitName((TextBox)sender);
            e.Handled = true;
        }
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e) => CommitName((TextBox)sender);

    private void CommitName(TextBox box)
    {
        string? name = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
        if (name != Bulb.Name)
            Bulb.Name = name;
    }

    // Control-to-bulb handlers. A change is only user input when the control is already loaded
    // (initial binding and range clamping happen before Loaded) and the new value differs from
    // the bulb's state (OneWay updates from the bulb always match it).
    private static bool IsUserChange(FrameworkElement control) => control.IsLoaded;

    private void Power_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && IsUserChange(toggle) && toggle.IsOn != Bulb.IsOn)
            Bulb.SetOn(toggle.IsOn);
    }

    private void Brightness_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (IsUserChange((Slider)sender) && Math.Abs(e.NewValue - Bulb.Brightness) >= 1)
            Bulb.SetBrightness(e.NewValue);
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

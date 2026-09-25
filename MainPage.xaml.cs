using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TrayWiz.Models;
using TrayWiz.ViewModels;
using Windows.System;
using Windows.UI;

namespace TrayWiz;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel => MainViewModel.Instance;

    /// <summary>Called each time the tray popup opens.</summary>
    public void OnPopupShown()
    {
        // Park focus on the page so no button starts focused (a stray Enter would press it).
        Focus(FocusState.Programmatic);

        if (ViewModel.Bulbs.Count == 0)
            _ = ViewModel.ScanAsync();
        else
            _ = ViewModel.RefreshStatesAsync();
    }

    private void NameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitName(sender);
            e.Handled = true;
        }
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e) => CommitName(sender);

    private static void CommitName(object sender)
    {
        if (sender is TextBox { DataContext: Bulb bulb } box)
        {
            string? name = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
            if (name != bulb.Name)
                bulb.Name = name;
        }
    }

    // Control-to-bulb handlers. A change is only user input when the control is already loaded
    // (initial binding and range clamping happen before Loaded) and the new value differs from
    // the bulb's state (OneWay updates from the bulb always match it).
    private static bool IsUserChange(FrameworkElement control) => control.IsLoaded;

    private void Power_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { DataContext: Bulb bulb } toggle && IsUserChange(toggle) && toggle.IsOn != bulb.IsOn)
            bulb.SetOn(toggle.IsOn);
    }

    private void Brightness_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider { DataContext: Bulb bulb } slider && IsUserChange(slider) && Math.Abs(e.NewValue - bulb.Brightness) >= 1)
            bulb.SetBrightness(e.NewValue);
    }

    private void Temperature_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider { DataContext: Bulb bulb } slider && IsUserChange(slider) && Math.Abs(e.NewValue - bulb.Temperature) >= 1)
            bulb.SetTemperature(e.NewValue);
    }

    private void Scene_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: Bulb bulb } combo && IsUserChange(combo)
            && combo.SelectedIndex >= 0 && combo.SelectedIndex != bulb.SceneIndex)
        {
            bulb.SetScene(combo.SelectedIndex);
        }
    }

    private void PresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Bulb bulb, Tag: string hex })
        {
            uint rgb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            bulb.SetColor(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }
    }

    // The picker is only "armed" (Tag = bulb) after its initial color is set, so opening
    // the flyout never pushes a normalized color back to the bulb.
    private void ColorFlyout_Opening(object? sender, object e)
    {
        if (sender is Flyout { Target.DataContext: Bulb bulb, Content: ColorPicker picker })
        {
            picker.Tag = null;
            picker.Color = bulb.Color;
            picker.Tag = bulb;
        }
    }

    private void ColorFlyout_Closed(object? sender, object e)
    {
        if (sender is Flyout { Content: ColorPicker picker })
            picker.Tag = null;
    }

    private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (sender.Tag is Bulb bulb && args.NewColor != bulb.Color)
            bulb.SetColor(args.NewColor);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ShutdownAndExit();
}

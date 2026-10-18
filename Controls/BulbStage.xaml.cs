using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TrayWiz.Controls;

/// <summary>
/// The top of a bulb's detail view: the home page tile blown up. The bulb sits top left, the power switch top
/// right and the brightness slider along the bottom. While on, the card takes the light's color and glows from
/// the bulb as brightly as the light is set, just as the tile does (see <see cref="GlyphLight"/>); while off it
/// is the plain card. It starts out matching the bulb, and changes only animate once it has loaded.
/// </summary>
public sealed partial class BulbStage : UserControl
{
    // A tile is about 120 wide; this is about three times that, so the light hovers that much higher.
    private const float LightReach = 2.5f;

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(BulbStage), new PropertyMetadata(false, OnLookChanged));

    public static readonly DependencyProperty LightColorProperty =
        DependencyProperty.Register(nameof(LightColor), typeof(Color), typeof(BulbStage), new PropertyMetadata(Colors.White, OnLookChanged));

    public static readonly DependencyProperty BrightnessProperty =
        DependencyProperty.Register(nameof(Brightness), typeof(double), typeof(BulbStage), new PropertyMetadata(100.0, OnBrightnessChanged));

    private static readonly AccessibilitySettings Accessibility = new();
    private static readonly UISettings Settings = new();

    private readonly SolidColorBrush _tint;
    private readonly SolidColorBrush _ink;
    private readonly GlyphLight _light;

    // False until the first look is shown, so a bulb that is on opens lit rather than blooming.
    private bool _isShown;

    public BulbStage()
    {
        InitializeComponent();
        _tint = (SolidColorBrush)Resources["TintBrush"];
        _ink = (SolidColorBrush)Resources["InkBrush"];

        _light = new GlyphLight(Root, GlyphIcon, LightReach, Card);
        _light.Dark += (_, _) => VisualStateManager.GoToState(this, IsOn ? "OnHighContrast" : "Off", true);

        Loaded += (_, _) =>
        {
            UpdateLook();
            _isShown = true;
        };
    }

    /// <summary>Raised only for user changes to the power switch, never when <see cref="IsOn"/> is set.</summary>
    public event EventHandler<bool>? PowerChanged;

    /// <summary>Raised only for user changes to the slider, never when <see cref="Brightness"/> is set.</summary>
    public event EventHandler<double>? BrightnessChanged;

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>The color the bulb gives off while on. It can change while off; the light comes on in it.</summary>
    public Color LightColor
    {
        get => (Color)GetValue(LightColorProperty);
        set => SetValue(LightColorProperty, value);
    }

    /// <summary>Brightness percentage, 10 to 100. Display only; user changes come back through <see cref="BrightnessChanged"/>.</summary>
    public double Brightness
    {
        get => (double)GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    public static string ReadoutText(bool isOn, double brightness) => isOn ? $"{brightness:0}%" : "Off";

    private bool CanAnimate => _isShown && Settings.AnimationsEnabled;

    private static void OnLookChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((BulbStage)sender).UpdateLook();

    // Brightness runs 10 to 100 (the slider's range); the light's level runs 0 to 1 across it.
    private static void OnBrightnessChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        BulbStage stage = (BulbStage)sender;
        stage._light.SetLevel(Math.Clamp(((double)args.NewValue - 10) / 90, 0, 1), stage.CanAnimate);
    }

    // Switching off leaves the card in its "On" colors while the light fades; GlyphLight.Dark then takes it
    // to the unlit state. High contrast never shows the light's own color, so it's never lit either.
    private void UpdateLook()
    {
        if (!IsOn || Accessibility.HighContrast)
        {
            _light.SwitchOff(CanAnimate);
            return;
        }

        _tint.Color = LightColor;
        _ink.Color = GlyphLight.InkFor(LightColor);
        VisualStateManager.GoToState(this, "On", true);
        _light.SwitchOn(CanAnimate);
    }

    // User input only: initial binding happens before the control loads, and later OneWay updates always
    // equal the bulb's state.
    private void Power_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true } toggle && toggle.IsOn != IsOn)
            PowerChanged?.Invoke(this, toggle.IsOn);
    }

    private void Brightness_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (((Slider)sender).IsLoaded && Math.Abs(e.NewValue - Brightness) >= 1)
            BrightnessChanged?.Invoke(this, e.NewValue);
    }
}

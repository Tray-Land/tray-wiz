using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TrayWiz.Controls;

/// <summary>
/// A home page tile. The main area toggles a light (or runs a button), the chevron on the right opens
/// its details, and an optional brightness slider runs along the main area's bottom edge. While on, the
/// tile takes the light's color and glows from its glyph as brightly as the light is set (see
/// <see cref="GlyphLight"/>). Modeled on WinUI's SplitButton (transparent halves over separate
/// background layers, a 1 px divider), except the halves stay separate tab stops and UIA elements,
/// because the right half navigates instead of opening a flyout.
/// </summary>
public sealed partial class RichSplitButton : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(RichSplitButton), new PropertyMetadata(""));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(RichSplitButton), new PropertyMetadata(""));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(RichSplitButton), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(RichSplitButton), new PropertyMetadata(""));

    public static readonly DependencyProperty IsToggleProperty =
        DependencyProperty.Register(nameof(IsToggle), typeof(bool), typeof(RichSplitButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(RichSplitButton), new PropertyMetadata(false, OnLookChanged));

    public static readonly DependencyProperty TintColorProperty =
        DependencyProperty.Register(nameof(TintColor), typeof(Color), typeof(RichSplitButton), new PropertyMetadata(default(Color), OnLookChanged));

    public static readonly DependencyProperty IsBrightnessVisibleProperty =
        DependencyProperty.Register(nameof(IsBrightnessVisible), typeof(bool), typeof(RichSplitButton), new PropertyMetadata(false));

    public static readonly DependencyProperty BrightnessProperty =
        DependencyProperty.Register(nameof(Brightness), typeof(double), typeof(RichSplitButton), new PropertyMetadata(100.0, OnBrightnessChanged));

    private static readonly AccessibilitySettings Accessibility = new();
    private static readonly UISettings Settings = new();

    private readonly SolidColorBrush _tint;
    private readonly SolidColorBrush _ink;
    private readonly SolidColorBrush _divider;
    private readonly GlyphLight _light;

    // False until the first look is shown, so tiles that open already on appear lit rather than all blooming at once.
    private bool _isShown;

    public RichSplitButton()
    {
        InitializeComponent();
        _tint = (SolidColorBrush)Resources["TintBrush"];
        _ink = (SolidColorBrush)Resources["InkBrush"];
        _divider = (SolidColorBrush)Resources["DividerBrush"];

        // Only the background layers are lit, so text and icons keep the ink color InkFor picked.
        _light = new GlyphLight(Root, GlyphIcon, PrimaryBackground, SecondaryBackground);
        _light.Dark += (_, _) => VisualStateManager.GoToState(this, IsOn ? "OnHighContrast" : "Off", true);

        Loaded += (_, _) =>
        {
            UpdateLook();
            _isShown = true;
        };
    }

    /// <summary>Raised when the main area is clicked (for a toggle, after it flips).</summary>
    public event RoutedEventHandler? Click;

    /// <summary>Raised when the chevron is clicked.</summary>
    public event RoutedEventHandler? SecondaryClick;

    /// <summary>Raised only for user changes to the slider, never when <see cref="Brightness"/> is set.</summary>
    public event EventHandler<double>? BrightnessChanged;

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Short state text in the top corner, such as "70%" or "Off".</summary>
    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>Tooltip for the main area.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>True: the main area is a toggle button bound to <see cref="IsOn"/>. False: a plain button.</summary>
    public bool IsToggle
    {
        get => (bool)GetValue(IsToggleProperty);
        set => SetValue(IsToggleProperty, value);
    }

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>The tile's fill while <see cref="IsOn"/>; text switches between black and white to stay readable on it.</summary>
    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public bool IsBrightnessVisible
    {
        get => (bool)GetValue(IsBrightnessVisibleProperty);
        set => SetValue(IsBrightnessVisibleProperty, value);
    }

    /// <summary>Brightness percentage, 10 to 100. Display only; user changes come back through <see cref="BrightnessChanged"/>.</summary>
    public double Brightness
    {
        get => (double)GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    public static bool Not(bool value) => !value;

    // The bottom padding keeps the title clear of the slider.
    public static Thickness FacePadding(bool hasSlider) => new(12, 10, 8, hasSlider ? 22 : 10);

    // While off, the slider still shows (and sets) the level the light comes back at, so it's muted.
    public static double SliderOpacity(bool isOn) => isOn ? 1 : 0.5;

    public static string BrightnessName(string title) => $"{title} brightness";

    public static string SecondaryName(string title) => $"{title} settings";

    /// <summary>Moves focus to the chevron, e.g. when coming back from the details it opened.</summary>
    public void FocusSecondary() => SecondaryButton.Focus(FocusState.Programmatic);

    private bool CanAnimate => _isShown && Settings.AnimationsEnabled;

    private static void OnLookChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((RichSplitButton)sender).UpdateLook();

    // Brightness runs 10 to 100 (the slider's range); the light's level runs 0 to 1 across it.
    private static void OnBrightnessChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        RichSplitButton button = (RichSplitButton)sender;
        button._light.SetLevel(Math.Clamp(((double)args.NewValue - 10) / 90, 0, 1), button.CanAnimate);
    }

    // Switching off leaves the tile in its "On" colors while the light fades; GlyphLight.Dark then takes it
    // to the unlit state. High contrast never shows the light's own color, so it's never lit either.
    private void UpdateLook()
    {
        if (!IsOn || Accessibility.HighContrast)
        {
            _light.SwitchOff(CanAnimate);
            return;
        }

        Color ink = GlyphLight.InkFor(TintColor);
        _tint.Color = TintColor;
        _ink.Color = ink;
        _divider.Color = Color.FromArgb(0x33, ink.R, ink.G, ink.B);
        VisualStateManager.GoToState(this, "On", true);
        _light.SwitchOn(CanAnimate);
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);

    private void Secondary_Click(object sender, RoutedEventArgs e) => SecondaryClick?.Invoke(this, e);

    // User input only: the first value is bound before the slider loads, and later OneWay updates always
    // equal Brightness. Requiring a real difference also keeps a range clamp from ever reaching a light.
    private void Brightness_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (((Slider)sender).IsLoaded && Math.Abs(e.NewValue - Brightness) >= 1)
            BrightnessChanged?.Invoke(this, e.NewValue);
    }
}

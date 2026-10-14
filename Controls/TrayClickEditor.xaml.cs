using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TrayWiz.Models;
using TrayWiz.Services;
using Windows.UI;

namespace TrayWiz.Controls;

/// <summary>
/// Picks what one kind of click on a tray icon does, and for the cycling choices the values it
/// swaps between. Only offers what the target supports.
/// </summary>
public sealed partial class TrayClickEditor : UserControl
{
    private const int MinCycleValues = 2;
    private const int MaxCycleValues = 8;

    private static readonly string[] SuggestedColors = ["FF3B30", "007AFF", "34C759", "FF9500", "AF52DE", "FFCC00", "32ADE6", "FFFFFF"];
    private static readonly string[] SuggestedBrightness = ["100", "50", "20", "75", "35", "10", "90", "60"];

    private readonly List<string> _brightnessValues;
    private readonly List<string> _colorValues;

    // True while controls are being filled in code, so their change events are ignored.
    private bool _loading = true;

    public TrayClickEditor(string header, TrayTargetKind target, bool allowMenu, bool allowColor, SavedTrayClick initial)
    {
        InitializeComponent();
        KindCombo.Header = header;
        AutomationProperties.SetName(KindCombo, header);

        _brightnessValues = initial.Kind == TrayClickKind.CycleBrightness && initial.Values.Count > 0 ? [.. initial.Values] : [.. SuggestedBrightness[..MinCycleValues]];
        _colorValues = initial.Kind == TrayClickKind.CycleColor && initial.Values.Count > 0 ? [.. initial.Values] : [.. SuggestedColors[..MinCycleValues]];

        List<ComboBoxItem> kinds = [Item(TrayClickKind.None, "Do nothing")];
        if (allowMenu)
            kinds.Add(Item(TrayClickKind.ShowMenu, "Show the menu"));
        kinds.Add(Item(TrayClickKind.OpenPopup, "Open TrayWiz"));
        if (target == TrayTargetKind.Action)
        {
            kinds.Add(Item(TrayClickKind.Run, "Run this button"));
        }
        else
        {
            kinds.Add(Item(TrayClickKind.Toggle, "Toggle on and off"));
            kinds.Add(Item(TrayClickKind.CycleBrightness, "Swap between brightness levels"));
            if (allowColor)
                kinds.Add(Item(TrayClickKind.CycleColor, "Swap between colors"));
        }

        KindCombo.ItemsSource = kinds;
        KindCombo.SelectedItem = kinds.FirstOrDefault(i => (TrayClickKind)i.Tag == initial.Kind) ?? kinds[0];

        _loading = false;
        UpdateValuesPanel();
    }

    /// <summary>Raised when the choice or any cycle value changes.</summary>
    public event EventHandler? Changed;

    public TrayClickKind Kind => (KindCombo.SelectedItem as ComboBoxItem)?.Tag is TrayClickKind kind ? kind : TrayClickKind.None;

    /// <summary>False when a cycle has too few values or one that isn't usable.</summary>
    public bool IsValid => Kind switch
    {
        TrayClickKind.CycleBrightness => _brightnessValues.Count >= MinCycleValues && _brightnessValues.All(v => int.TryParse(v, CultureInfo.InvariantCulture, out int p) && p is >= 10 and <= 100),
        TrayClickKind.CycleColor => _colorValues.Count >= MinCycleValues,
        _ => true,
    };

    public SavedTrayClick Build() => new()
    {
        Kind = Kind,
        Values = Kind switch
        {
            TrayClickKind.CycleBrightness => [.. _brightnessValues],
            TrayClickKind.CycleColor => [.. _colorValues],
            _ => [],
        },
    };

    private static ComboBoxItem Item(TrayClickKind kind, string text) => new() { Content = text, Tag = kind };

    private List<string>? ActiveValues => Kind switch
    {
        TrayClickKind.CycleBrightness => _brightnessValues,
        TrayClickKind.CycleColor => _colorValues,
        _ => null,
    };

    private void KindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;

        UpdateValuesPanel();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateValuesPanel()
    {
        List<string>? values = ActiveValues;
        ValuesPanel.Visibility = values is null ? Visibility.Collapsed : Visibility.Visible;
        if (values is null)
            return;

        ValuesHint.Text = Kind == TrayClickKind.CycleBrightness
            ? "Each click moves to the next level, then starts over."
            : "Each click moves to the next color, then starts over.";
        RebuildRows();
    }

    private void RebuildRows()
    {
        List<string>? values = ActiveValues;
        Rows.Children.Clear();
        if (values is null)
            return;

        bool isBrightness = Kind == TrayClickKind.CycleBrightness;
        for (int i = 0; i < values.Count; i++)
        {
            int index = i;
            Grid row = new() { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement editor = isBrightness ? BrightnessBox(values, index) : ColorButton(values, index);
            Grid.SetColumn(editor, 0);
            row.Children.Add(editor);

            Button remove = new()
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                IsEnabled = values.Count > MinCycleValues,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(remove, $"Remove value {index + 1}");
            ToolTipService.SetToolTip(remove, "Remove");
            remove.Click += (_, _) =>
            {
                values.RemoveAt(index);
                RebuildRows();
                Changed?.Invoke(this, EventArgs.Empty);
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);

            Rows.Children.Add(row);
        }

        AddButton.IsEnabled = values.Count < MaxCycleValues;
    }

    private FrameworkElement BrightnessBox(List<string> values, int index)
    {
        int.TryParse(values[index], CultureInfo.InvariantCulture, out int percent);
        NumberBox box = new()
        {
            Minimum = 10,
            Maximum = 100,
            SmallChange = 5,
            LargeChange = 10,
            Value = Math.Clamp(percent, 10, 100),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Header = $"Level {index + 1} (%)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.ValueChanged += (_, args) =>
        {
            // NaN means the box was cleared; keep the entry but make it invalid until retyped.
            values[index] = double.IsNaN(args.NewValue) ? "" : ((int)Math.Round(args.NewValue)).ToString(CultureInfo.InvariantCulture);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        return box;
    }

    private FrameworkElement ColorButton(List<string> values, int index)
    {
        Color color = Bulb.TryParseHex(values[index], out Color parsed) ? parsed : Color.FromArgb(255, 255, 255, 255);
        Ellipse swatch = new()
        {
            Width = 16,
            Height = 16,
            Fill = new SolidColorBrush(color),
            Stroke = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            StrokeThickness = 1,
        };
        TextBlock label = new() { Text = $"Color {index + 1}", VerticalAlignment = VerticalAlignment.Center };

        // Set before the handler is attached so opening the flyout never counts as a change.
        ColorPicker picker = new()
        {
            Color = color,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
            IsAlphaEnabled = false,
            IsColorChannelTextInputVisible = false,
            IsColorPreviewVisible = false,
            IsHexInputVisible = false,
            IsMoreButtonVisible = false,
        };
        picker.ColorChanged += (_, args) =>
        {
            values[index] = $"{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
            swatch.Fill = new SolidColorBrush(args.NewColor);
            Changed?.Invoke(this, EventArgs.Empty);
        };

        Button button = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { swatch, label } },
            Flyout = new Flyout { Content = picker, ShouldConstrainToRootBounds = false },
        };
        AutomationProperties.SetName(button, $"Color {index + 1}");
        return button;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        List<string>? values = ActiveValues;
        if (values is null || values.Count >= MaxCycleValues)
            return;

        string[] suggestions = Kind == TrayClickKind.CycleBrightness ? SuggestedBrightness : SuggestedColors;
        values.Add(suggestions.FirstOrDefault(s => !values.Contains(s)) ?? suggestions[0]);
        RebuildRows();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

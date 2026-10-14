using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TrayWiz.Models;
using TrayWiz.Services;
using TrayWiz.ViewModels;

namespace TrayWiz.Controls;

/// <summary>
/// Sets up a tray icon for a light, group or custom button: its name and icon, and what a left
/// click, right click and each double-click do. Nothing here touches a light; the result is
/// handed back through <see cref="Saved"/>.
/// </summary>
public sealed partial class TrayIconEditorView : UserControl
{
    private static readonly string[] Icons =
    [
        "", "", "", "", "", "", "", "", "", "",
    ];

    private readonly SavedTrayIcon _initial;
    private readonly TrayClickEditor _left;
    private readonly TrayClickEditor _right;
    private readonly TrayClickEditor _leftDouble;
    private readonly TrayClickEditor _rightDouble;

    // True while controls are being filled in code, so their change events are ignored.
    private bool _loading = true;

    public TrayIconEditorView(MainViewModel viewModel, SavedTrayIcon initial, bool isNew)
    {
        _initial = initial;
        InitializeComponent();

        TitleText.Text = isNew ? "Add tray icon" : "Edit tray icon";
        string kind = initial.TargetKind switch { TrayTargetKind.Light => "Light", TrayTargetKind.Group => "Group", _ => "Button" };
        TargetText.Text = $"{kind} · {viewModel.TargetName(initial.TargetKind, initial.TargetId) ?? initial.Name}";
        SaveButton.Content = isNew ? "Add" : "Save";

        // A light that can't do color shouldn't be offered a color cycle.
        bool allowColor = initial.TargetKind != TrayTargetKind.Light
            || viewModel.ResolveBulbs(initial.TargetKind, initial.TargetId).Any(b => b.IsRgb);

        _left = AddEditor("Left click", allowMenu: true, allowColor, initial.LeftClick);
        _right = AddEditor("Right click", allowMenu: true, allowColor, initial.RightClick);
        _leftDouble = AddEditor("Left double-click", allowMenu: false, allowColor, initial.LeftDoubleClick);
        _rightDouble = AddEditor("Right double-click", allowMenu: false, allowColor, initial.RightDoubleClick);

        NameBox.Text = initial.Name;
        IconGrid.ItemsSource = Icons;
        IconGrid.SelectedItem = Icons.Contains(initial.Glyph) ? initial.Glyph : Icons[0];

        _loading = false;
        Update();
        Loaded += (_, _) => BackButton.Focus(FocusState.Programmatic);
    }

    public event EventHandler? BackRequested;

    public event EventHandler<SavedTrayIcon>? Saved;

    private TrayClickEditor AddEditor(string header, bool allowMenu, bool allowColor, SavedTrayClick initial)
    {
        TrayClickEditor editor = new(header, _initial.TargetKind, allowMenu, allowColor, initial);
        editor.Changed += (_, _) => Update();
        ClickEditors.Children.Add(editor);
        return editor;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
            Update();
    }

    private void Update()
    {
        string? problem = null;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            problem = "Give the icon a name.";
        else if (_left.Kind != TrayClickKind.ShowMenu && _right.Kind != TrayClickKind.ShowMenu)
            problem = "Left or right click must show the menu, which is how you edit or remove this icon.";
        else if (!(_left.IsValid && _right.IsValid && _leftDouble.IsValid && _rightDouble.IsValid))
            problem = "Swapping needs at least two values.";

        ProblemText.Text = problem ?? "";
        SaveButton.IsEnabled = problem is null;
    }

    private void Save_Click(object sender, RoutedEventArgs e) =>
        Saved?.Invoke(this, new SavedTrayIcon
        {
            Id = _initial.Id,
            Name = NameBox.Text.Trim(),
            Glyph = IconGrid.SelectedItem as string ?? Icons[0],
            TargetKind = _initial.TargetKind,
            TargetId = _initial.TargetId,
            LeftClick = _left.Build(),
            RightClick = _right.Build(),
            LeftDoubleClick = _leftDouble.Build(),
            RightDoubleClick = _rightDouble.Build(),
        });
}

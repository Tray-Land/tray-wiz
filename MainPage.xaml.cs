using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using TrayWiz.Controls;
using TrayWiz.Models;
using TrayWiz.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.ViewManagement;

namespace TrayWiz;

public sealed partial class MainPage : Page
{
    // How far (in DIPs) the pointer must move over a tile before a press becomes a drag.
    private const double DragThreshold = 8;

    // The tile chevron that opened the current detail view, so focus can return to it.
    private Control? _detailOrigin;

    // The tile being dragged. Drags only ever happen within this page, so the payload is kept here.
    private Bulb? _draggedBulb;

    // Where the left button went down on a tile; null when no drag can start.
    private Windows.Foundation.Point? _dragOrigin;

    private readonly PointerEventHandler _tilePointerPressed;
    private readonly PointerEventHandler _tilePointerMoved;

    public MainPage()
    {
        _tilePointerPressed = Tile_PointerPressed;
        _tilePointerMoved = Tile_PointerMoved;
        InitializeComponent();
    }

    public MainViewModel ViewModel => MainViewModel.Instance;

    /// <summary>Called each time the tray popup opens.</summary>
    public void OnPopupShown()
    {
        // Like Quick Settings, the popup always reopens on the tile grid.
        ShowHome(restoreFocus: false);

        // Park focus on the page so no button starts focused (a stray Enter would press it).
        Focus(FocusState.Programmatic);

        if (ViewModel.Bulbs.Count == 0)
            _ = ViewModel.ScanAsync();
        else
            _ = ViewModel.RefreshStatesAsync();
    }

    /// <summary>
    /// Called when the popup window closes for real. x:Bind subscribes to the app-lifetime view
    /// model, which would otherwise keep this page (and its window) reachable.
    /// </summary>
    public void Release()
    {
        ShowHome(restoreFocus: false);
        Bindings?.StopTracking();
    }

    public static Style TileButtonStyle(bool isOn) =>
        (Style)Application.Current.Resources[isOn ? "AccentButtonStyle" : "DefaultButtonStyle"];

    public static string DetailsName(string displayName) => $"{displayName} settings";

    public static double TileLabelOpacity(bool isReachable) => isReachable ? 1 : 0.5;

    public static string GroupOptionsName(string groupName) => $"{groupName} options";

    public static string GroupPowerName(string groupName, bool on) => $"Turn {groupName} {(on ? "on" : "off")}";

    private void GroupOn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BulbGroup group })
            group.SetAllOn(true);
    }

    private void GroupOff_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BulbGroup group })
            group.SetAllOn(false);
    }

    // Click only fires for user input, never when the OneWay IsChecked binding updates.
    private void TilePower_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: Bulb bulb } toggle)
            bulb.SetOn(toggle.IsChecked == true);
    }

    private void TileDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Bulb bulb } button)
            ShowDetail(bulb, button);
    }

    private void ShowDetail(Bulb bulb, Control origin)
    {
        // A new view per bulb: rebinding existing sliders to another bulb could clamp and send values.
        BulbDetailView detail = new(bulb);
        detail.BackRequested += (_, _) => ShowHome(restoreFocus: true);

        _detailOrigin = origin;
        DetailHost.Content = detail;
        DetailHost.Visibility = Visibility.Visible;
        HomeView.Visibility = Visibility.Collapsed;
    }

    private void ShowHome(bool restoreFocus)
    {
        if (DetailHost.Content is null)
            return;

        DetailHost.Content = null;
        DetailHost.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;

        if (restoreFocus)
            _detailOrigin?.Focus(FocusState.Programmatic);
        _detailOrigin = null;
    }

    // Tiles are mostly buttons, which capture the pointer and swallow CanDrag's gesture, so drags
    // are started by hand once the pointer moves far enough (handledEventsToo sees the button's input).
    private void Tile_Loaded(object sender, RoutedEventArgs e)
    {
        UIElement tile = (UIElement)sender;
        tile.RemoveHandler(PointerPressedEvent, _tilePointerPressed);
        tile.RemoveHandler(PointerMovedEvent, _tilePointerMoved);
        tile.AddHandler(PointerPressedEvent, _tilePointerPressed, handledEventsToo: true);
        tile.AddHandler(PointerMovedEvent, _tilePointerMoved, handledEventsToo: true);
    }

    private void Tile_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint((UIElement)sender);
        _dragOrigin = point.Properties.IsLeftButtonPressed ? point.Position : null;
    }

    private void Tile_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragOrigin is not Windows.Foundation.Point origin)
            return;

        UIElement tile = (UIElement)sender;
        PointerPoint point = e.GetCurrentPoint(tile);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _dragOrigin = null;
            return;
        }

        if (Math.Abs(point.Position.X - origin.X) >= DragThreshold || Math.Abs(point.Position.Y - origin.Y) >= DragThreshold)
        {
            _dragOrigin = null;
            _ = tile.StartDragAsync(point);
        }
    }

    private void Tile_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: Bulb bulb })
        {
            args.Cancel = true;
            return;
        }

        _draggedBulb = bulb;
        args.Data.SetText(bulb.DisplayName);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void Tile_DropCompleted(UIElement sender, DropCompletedEventArgs args) => _draggedBulb = null;

    private void Group_DragEnter(object sender, DragEventArgs e)
    {
        if (_draggedBulb is not null && sender is Panel panel)
            panel.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
    }

    private void Group_DragLeave(object sender, DragEventArgs e) => ClearDropHighlight(sender);

    private void Group_DragOver(object sender, DragEventArgs e)
    {
        // Handled so the surrounding "remove from group" target doesn't override this one.
        e.Handled = true;
        if (_draggedBulb is null || sender is not FrameworkElement { DataContext: BulbGroup group })
            return;

        bool isMove = ViewModel.GroupOf(_draggedBulb) != group;
        e.AcceptedOperation = isMove ? DataPackageOperation.Move : DataPackageOperation.None;
        e.DragUIOverride.Caption = isMove ? $"Move to {group.Name}" : "";
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void Group_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHighlight(sender);
        if (_draggedBulb is not null && sender is FrameworkElement { DataContext: BulbGroup group })
            ViewModel.MoveToGroup(_draggedBulb, group);
    }

    private static void ClearDropHighlight(object sender)
    {
        if (sender is Panel panel)
            panel.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void Ungrouped_DragOver(object sender, DragEventArgs e)
    {
        bool isMove = _draggedBulb is not null && ViewModel.GroupOf(_draggedBulb) is not null;
        e.AcceptedOperation = isMove ? DataPackageOperation.Move : DataPackageOperation.None;
        e.DragUIOverride.Caption = isMove ? "Remove from group" : "";
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void Ungrouped_Drop(object sender, DragEventArgs e)
    {
        if (_draggedBulb is not null)
            ViewModel.MoveToGroup(_draggedBulb, null);
    }

    // Menu equivalent of drag and drop, for keyboard and screen reader users.
    private void Tile_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: Bulb bulb } tile)
            return;

        args.Handled = true;
        BulbGroup? current = ViewModel.GroupOf(bulb);
        MenuFlyout menu = new();

        MenuFlyoutSubItem moveTo = new() { Text = "Move to group", Icon = new FontIcon { Glyph = "" } };
        foreach (BulbGroup group in ViewModel.Groups.Where(g => g != current))
        {
            MenuFlyoutItem item = new() { Text = group.Name };
            item.Click += (_, _) => ViewModel.MoveToGroup(bulb, group);
            moveTo.Items.Add(item);
        }
        if (moveTo.Items.Count > 0)
            moveTo.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem newGroup = new() { Text = "New group" };
        newGroup.Click += (_, _) =>
        {
            BulbGroup group = ViewModel.CreateGroup();
            ViewModel.MoveToGroup(bulb, group);
            BeginRenameGroup(group);
        };
        moveTo.Items.Add(newGroup);
        menu.Items.Add(moveTo);

        if (current is not null)
        {
            MenuFlyoutItem remove = new() { Text = $"Remove from {current.Name}", Icon = new FontIcon { Glyph = "" } };
            remove.Click += (_, _) => ViewModel.MoveToGroup(bulb, null);
            menu.Items.Add(remove);
        }

        if (args.TryGetPosition(tile, out Windows.Foundation.Point point))
            menu.ShowAt(tile, point);
        else
            menu.ShowAt(tile);
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e) => BeginRenameGroup(ViewModel.CreateGroup());

    private void GroupOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: BulbGroup group } button)
            return;

        MenuFlyout menu = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        MenuFlyoutItem rename = new() { Text = "Rename", Icon = new FontIcon { Glyph = "" } };
        rename.Click += (_, _) => BeginRenameGroup(group);
        MenuFlyoutItem delete = new() { Text = "Delete group", Icon = new FontIcon { Glyph = "" } };
        delete.Click += (_, _) => ViewModel.DeleteGroup(group);
        int index = ViewModel.Groups.IndexOf(group);
        MenuFlyoutItem moveUp = new() { Text = "Move up", Icon = new FontIcon { Glyph = "" }, IsEnabled = index > 0 };
        moveUp.Click += (_, _) => MoveGroupAnimated(group, -1);
        MenuFlyoutItem moveDown = new() { Text = "Move down", Icon = new FontIcon { Glyph = "" }, IsEnabled = index < ViewModel.Groups.Count - 1 };
        moveDown.Click += (_, _) => MoveGroupAnimated(group, 1);

        menu.Items.Add(rename);
        menu.Items.Add(moveUp);
        menu.Items.Add(moveDown);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(delete);
        menu.ShowAt(button);
    }

    /// <summary>
    /// Reorders a group and slides every group that moved from its old spot to its new one.
    /// Containers are recreated on a move, so positions are matched up by group, not by element.
    /// </summary>
    private void MoveGroupAnimated(BulbGroup group, int offset)
    {
        Dictionary<BulbGroup, double> before = ViewModel.Groups.ToDictionary(g => g, GroupTop);
        ViewModel.MoveGroup(group, offset);

        if (!new UISettings().AnimationsEnabled)
            return;

        GroupsList.UpdateLayout();
        Compositor compositor = ElementCompositionPreview.GetElementVisual(GroupsList).Compositor;
        CubicBezierEasingFunction easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        foreach ((BulbGroup moved, double oldTop) in before)
        {
            if (GroupsList.ContainerFromItem(moved) is not UIElement container)
                continue;

            float delta = (float)(oldTop - GroupTop(moved));
            if (Math.Abs(delta) < 0.5f)
                continue;

            // The group the user moved slides over the ones making way for it.
            Canvas.SetZIndex(container, moved == group ? 1 : 0);

            ElementCompositionPreview.SetIsTranslationEnabled(container, true);
            Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0, new Vector3(0, delta, 0));
            slide.InsertKeyFrame(1, Vector3.Zero, easing);
            slide.Duration = TimeSpan.FromMilliseconds(333);
            ElementCompositionPreview.GetElementVisual(container).StartAnimation("Translation", slide);
        }
    }

    private double GroupTop(BulbGroup group) =>
        GroupsList.ContainerFromItem(group) is UIElement container
            ? container.TransformToVisual(GroupsList).TransformPoint(default).Y
            : 0;

    /// <summary>Shows a name box over the group's heading; the name is saved when it closes.</summary>
    private void BeginRenameGroup(BulbGroup group)
    {
        GroupsList.UpdateLayout();
        if (GroupsList.ContainerFromItem(group) is not FrameworkElement container)
            return;

        container.StartBringIntoView();

        TextBox box = new() { Text = group.Name, MaxLength = 40, Width = 280 };
        AutomationProperties.SetName(box, "Group name");
        Flyout flyout = new() { Content = box, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                flyout.Hide();
            }
        };
        flyout.Closed += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(box.Text))
                group.Name = box.Text.Trim();
        };
        flyout.ShowAt(container);
    }


    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ShutdownAndExit();
}

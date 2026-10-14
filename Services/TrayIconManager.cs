using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using TrayWiz.Models;
using TrayWiz.ViewModels;
using WinUIEx;

namespace TrayWiz.Services;

/// <summary>
/// Owns the extra notification-area icons the user adds for a light, group or custom button, and
/// runs whatever each click is set to do. Nothing here runs while idle: an icon only acts when
/// it's clicked.
/// </summary>
internal sealed class TrayIconManager : IDisposable
{
    private const string DefaultLightGlyph = "";
    private const string DefaultGroupGlyph = "";

    // Extra icons take ids after the app's own icon (1).
    private uint _nextId = 2;

    private readonly DispatcherQueue _dispatcher;
    private readonly Action<TrayIcon> _openPopup;
    private readonly Action<SavedTrayIcon> _edit;
    private readonly Dictionary<string, Entry> _entries = [];

    public TrayIconManager(DispatcherQueue dispatcher, Action<TrayIcon> openPopup, Action<SavedTrayIcon> edit)
    {
        _dispatcher = dispatcher;
        _openPopup = openPopup;
        _edit = edit;

        MainViewModel.Instance.TargetDeleted += OnTargetDeleted;
        foreach (SavedTrayIcon saved in BulbStore.LoadTrayIcons())
            Add(saved);
    }

    public SavedTrayIcon? Find(string id) => _entries.GetValueOrDefault(id)?.Saved;

    /// <summary>A starting point for the editor: sensible clicks for the kind of target.</summary>
    public static SavedTrayIcon CreateDefault(TrayTargetKind kind, string targetId, string name, string? glyph) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Glyph = string.IsNullOrEmpty(glyph) ? (kind == TrayTargetKind.Group ? DefaultGroupGlyph : DefaultLightGlyph) : glyph,
        TargetKind = kind,
        TargetId = targetId,
        LeftClick = new() { Kind = kind == TrayTargetKind.Action ? TrayClickKind.Run : TrayClickKind.Toggle },
        RightClick = new() { Kind = TrayClickKind.ShowMenu },
    };

    /// <summary>Adds a new icon, or updates the one with the same id.</summary>
    public void Save(SavedTrayIcon saved)
    {
        if (_entries.TryGetValue(saved.Id, out Entry? existing))
        {
            existing.Saved = saved;
            Array.Clear(existing.CycleIndex);
            Apply(existing);
        }
        else
        {
            Add(saved);
        }

        Persist();
    }

    public void Remove(string id)
    {
        if (!_entries.Remove(id, out Entry? entry))
            return;

        Dispose(entry);
        Persist();
    }

    /// <summary>Redraws every icon for the current taskbar theme.</summary>
    public void RefreshTheme()
    {
        foreach (Entry entry in _entries.Values)
            Apply(entry);
    }

    public void Dispose()
    {
        MainViewModel.Instance.TargetDeleted -= OnTargetDeleted;
        foreach (Entry entry in _entries.Values)
            Dispose(entry);
        _entries.Clear();
    }

    private void OnTargetDeleted(TrayTargetKind kind, string targetId)
    {
        foreach (SavedTrayIcon doomed in _entries.Values.Select(e => e.Saved).Where(s => s.TargetKind == kind && s.TargetId == targetId).ToList())
            Remove(doomed.Id);
    }

    private void Add(SavedTrayIcon saved)
    {
        TrayIcon icon = new(_nextId++, App.TrayIconPath, saved.Name);
        Entry entry = new(saved, icon);
        _entries[saved.Id] = entry;

        icon.Selected += (_, e) => HandleClick(entry, Side.Left, e);
        icon.ContextMenu += (_, e) => HandleClick(entry, Side.Right, e);
        icon.LeftDoubleClick += (_, _) => HandleDoubleClick(entry, Side.Left);
        icon.RightDoubleClick += (_, _) => HandleDoubleClick(entry, Side.Right);

        Apply(entry);
        icon.IsVisible = true;
    }

    private static void Apply(Entry entry)
    {
        entry.Icon.Tooltip = entry.Saved.Name;
        entry.Icon.SetIcon(GlyphIconService.GetIconPath(entry.Saved.Glyph, SystemThemeService.IsLight) ?? App.TrayIconPath);
    }

    private static void Dispose(Entry entry)
    {
        entry.Pending[0]?.Cancel();
        entry.Pending[1]?.Cancel();
        entry.Icon.IsVisible = false;
        entry.Icon.Dispose();
    }

    private void Persist() => BulbStore.SaveTrayIcons(_entries.Values.Select(e => e.Saved));

    private void HandleClick(Entry entry, Side side, TrayIconEventArgs args)
    {
        // Windows reports the second click of a double-click as one more click; it isn't one.
        if (DateTime.UtcNow - entry.LastDoubleAt[(int)side] < DoubleClickTime)
            return;

        SavedTrayClick click = ClickFor(entry.Saved, side, isDouble: false);
        if (click.Kind == TrayClickKind.ShowMenu)
        {
            args.Flyout = BuildMenu(entry);
            return;
        }

        if (click.Kind == TrayClickKind.None)
            return;

        if (ClickFor(entry.Saved, side, isDouble: true).Kind == TrayClickKind.None)
        {
            Run(entry, side, isDouble: false);
            return;
        }

        // A double-click is also two clicks, so hold the single click back until it's clear
        // the second one isn't coming.
        entry.Pending[(int)side]?.Cancel();
        CancellationTokenSource cts = new();
        entry.Pending[(int)side] = cts;
        _ = Task.Delay(DoubleClickTime, cts.Token).ContinueWith(
            t => _dispatcher.TryEnqueue(() => Run(entry, side, isDouble: false)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void HandleDoubleClick(Entry entry, Side side)
    {
        entry.LastDoubleAt[(int)side] = DateTime.UtcNow;
        entry.Pending[(int)side]?.Cancel();
        entry.Pending[(int)side] = null;
        Run(entry, side, isDouble: true);
    }

    private static TimeSpan DoubleClickTime => TimeSpan.FromMilliseconds(Windows.Win32.PInvoke.GetDoubleClickTime());

    private static SavedTrayClick ClickFor(SavedTrayIcon saved, Side side, bool isDouble) => (side, isDouble) switch
    {
        (Side.Left, false) => saved.LeftClick,
        (Side.Right, false) => saved.RightClick,
        (Side.Left, true) => saved.LeftDoubleClick,
        _ => saved.RightDoubleClick,
    };

    private void Run(Entry entry, Side side, bool isDouble)
    {
        SavedTrayClick click = ClickFor(entry.Saved, side, isDouble);
        int slot = (int)side + (isDouble ? 2 : 0);
        _ = RunAsync(entry, click, slot);
    }

    private async Task RunAsync(Entry entry, SavedTrayClick click, int slot)
    {
        try
        {
            MainViewModel vm = MainViewModel.Instance;
            SavedTrayIcon saved = entry.Saved;
            switch (click.Kind)
            {
                case TrayClickKind.OpenPopup:
                    _openPopup(entry.Icon);
                    break;

                case TrayClickKind.Run:
                    if (vm.Actions.FirstOrDefault(a => a.Id == saved.TargetId) is LightAction action)
                        vm.RunAction(action);
                    break;

                case TrayClickKind.Toggle:
                    await SetPowerAsync(entry, on: null);
                    break;

                case TrayClickKind.CycleBrightness when click.Values.Count > 0:
                    {
                        string value = click.Values[entry.CycleIndex[slot]++ % click.Values.Count];
                        if (int.TryParse(value, out int percent))
                        {
                            foreach (Bulb bulb in vm.ResolveBulbs(saved.TargetKind, saved.TargetId))
                                bulb.SetBrightness(percent);
                        }
                        break;
                    }

                case TrayClickKind.CycleColor when click.Values.Count > 0:
                    {
                        string value = click.Values[entry.CycleIndex[slot]++ % click.Values.Count];
                        if (Bulb.TryParseHex(value, out Windows.UI.Color color))
                        {
                            foreach (Bulb bulb in vm.ResolveBulbs(saved.TargetKind, saved.TargetId))
                                bulb.SetColor(color);
                        }
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            // A click must never take the app down.
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] Click failed: {ex}");
        }
    }

    /// <summary>Turns the target on or off; with null, flips it (off if anything is lit, otherwise on).</summary>
    private async Task SetPowerAsync(Entry entry, bool? on)
    {
        List<Bulb> bulbs = MainViewModel.Instance.ResolveBulbs(entry.Saved.TargetKind, entry.Saved.TargetId);
        if (bulbs.Count == 0)
            return;

        if (on is null)
        {
            // State is only refreshed when the popup opens, so ask the lights before flipping.
            await MainViewModel.RefreshAsync(bulbs);
            on = !bulbs.Any(b => b.IsReachable && b.IsOn);
        }

        foreach (Bulb bulb in bulbs)
            bulb.SetOn(on.Value);
    }

    private MenuFlyout BuildMenu(Entry entry)
    {
        SavedTrayIcon saved = entry.Saved;
        MenuFlyout menu = new();

        if (saved.TargetKind == TrayTargetKind.Action)
        {
            MenuFlyoutItem run = new() { Text = $"Run {saved.Name}", Icon = new FontIcon { Glyph = "" } };
            run.Click += (_, _) => _ = RunAsync(entry, new SavedTrayClick { Kind = TrayClickKind.Run }, 0);
            menu.Items.Add(run);
        }
        else
        {
            MenuFlyoutItem on = new() { Text = "Turn on", Icon = new FontIcon { Glyph = "" } };
            on.Click += (_, _) => _ = SetPowerAsync(entry, true);
            MenuFlyoutItem off = new() { Text = "Turn off", Icon = new FontIcon { Glyph = "" } };
            off.Click += (_, _) => _ = SetPowerAsync(entry, false);
            menu.Items.Add(on);
            menu.Items.Add(off);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem open = new() { Text = "Open TrayWiz", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => _openPopup(entry.Icon);
        menu.Items.Add(open);

        MenuFlyoutItem edit = new() { Text = "Edit tray icon", Icon = new FontIcon { Glyph = "" } };
        edit.Click += (_, _) => _edit(saved);
        menu.Items.Add(edit);

        MenuFlyoutItem remove = new() { Text = "Remove tray icon", Icon = new FontIcon { Glyph = "" } };
        remove.Click += (_, _) => Remove(saved.Id);
        menu.Items.Add(remove);

        return menu;
    }

    private enum Side
    {
        Left,
        Right,
    }

    private sealed class Entry(SavedTrayIcon saved, TrayIcon icon)
    {
        public SavedTrayIcon Saved { get; set; } = saved;

        public TrayIcon Icon { get; } = icon;

        /// <summary>Where each of the four clicks is in its cycle.</summary>
        public int[] CycleIndex { get; } = new int[4];

        public CancellationTokenSource?[] Pending { get; } = new CancellationTokenSource?[2];

        public DateTime[] LastDoubleAt { get; } = [DateTime.MinValue, DateTime.MinValue];
    }
}

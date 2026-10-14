using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayWiz.Models;
using TrayWiz.Services;

namespace TrayWiz.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public static MainViewModel Instance { get; } = new();

    private readonly Dictionary<string, SavedBulb> _saved = BulbStore.Load();

    private MainViewModel()
    {
        WizClient.CommandSuppressed += () => StatusText = $"Dry run · {WizClient.SuppressedCount} command(s) blocked";

        foreach (SavedGroup saved in BulbStore.LoadGroups())
            Groups.Add(CreateGroupModel(saved.Id, saved.Name, saved.Macs));

        foreach (SavedAction saved in BulbStore.LoadActions())
        {
            LightAction action = new(saved);
            Actions.Add(action);
            (GroupOf(action)?.Tiles ?? Ungrouped).Add(action);
        }
        Sort(Ungrouped);
        foreach (BulbGroup group in Groups)
            Sort(group.Tiles);
    }

    /// <summary>Every known bulb, grouped or not.</summary>
    public ObservableCollection<Bulb> Bulbs { get; } = [];

    /// <summary>Custom actions, grouped or not.</summary>
    public ObservableCollection<LightAction> Actions { get; } = [];

    /// <summary>Tiles (bulbs and actions) that aren't in any group, shown above the groups.</summary>
    public ObservableCollection<TileItem> Ungrouped { get; } = [];

    public ObservableCollection<BulbGroup> Groups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateVisibility))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public Microsoft.UI.Xaml.Visibility EmptyStateVisibility =>
        !IsScanning && Bulbs.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// Finds bulbs by broadcast, also probing last-known IPs directly in case the broadcast
    /// is dropped, then loads each new bulb's capabilities and state.
    /// </summary>
    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning)
            return;

        IsScanning = true;
        StatusText = "Searching for bulbs…";
        try
        {
            Dictionary<string, JsonObject> found = await WizClient.DiscoverAsync(TimeSpan.FromSeconds(2));

            IEnumerable<string> knownIps = _saved.Values.Select(s => s.LastIp)
                .Concat(Bulbs.Select(b => b.Ip))
                .OfType<string>()
                .Where(ip => !found.ContainsKey(ip))
                .Distinct();
            await Task.WhenAll(knownIps.Select(async ip =>
            {
                if (await WizClient.RequestAsync(ip, "getPilot") is JsonObject pilot)
                    lock (found) found[ip] = pilot;
            }));

            HashSet<string> seenMacs = new(StringComparer.OrdinalIgnoreCase);
            await Task.WhenAll(found.Select(async pair =>
            {
                if (pair.Value["mac"] is not JsonValue macValue || !macValue.TryGetValue(out string? mac) || string.IsNullOrEmpty(mac))
                    return;
                lock (seenMacs) seenMacs.Add(mac);
                await AddOrUpdateAsync(mac, pair.Key, pair.Value);
            }));

            // Remember bulbs that didn't answer (e.g. their fixture is switched off).
            foreach (SavedBulb saved in _saved.Values.ToList())
            {
                if (!Bulbs.Any(b => string.Equals(b.Mac, saved.Mac, StringComparison.OrdinalIgnoreCase)))
                    AddBulb(CreateOfflineBulb(saved));
            }

            foreach (Bulb missing in Bulbs.Where(b => !seenMacs.Contains(b.Mac)))
                missing.IsReachable = false;

            SortBulbs();
            SaveAll();
            StatusText = (Bulbs.Count == 1 ? "1 bulb" : $"{Bulbs.Count} bulbs") + (WizClient.DryRun ? " · dry run" : "");
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Re-reads every known bulb's state; cheap enough to run each time the popup opens.</summary>
    public async Task RefreshStatesAsync()
    {
        if (IsScanning)
            return;

        await Task.WhenAll(Bulbs.ToList().Select(async bulb =>
        {
            if (await WizClient.RequestAsync(bulb.Ip, "getPilot") is JsonObject pilot)
                bulb.ApplyPilot(pilot);
            else
                bulb.IsReachable = false;
        }));
    }

    [RelayCommand]
    private void AllOn() => SetAll(true);

    [RelayCommand]
    private void AllOff() => SetAll(false);

    private void SetAll(bool on)
    {
        foreach (Bulb bulb in Bulbs.Where(b => b.IsReachable))
            bulb.SetOn(on);
    }

    public BulbGroup? GroupOf(TileItem tile) => Groups.FirstOrDefault(g => g.Macs.Contains(tile.Key));

    /// <summary>Moves a tile into <paramref name="target"/>, or out of its group when null.</summary>
    public void MoveToGroup(TileItem tile, BulbGroup? target)
    {
        BulbGroup? current = GroupOf(tile);
        if (current == target)
            return;

        if (current is null)
        {
            Ungrouped.Remove(tile);
        }
        else
        {
            current.Macs.Remove(tile.Key);
            current.Tiles.Remove(tile);
        }

        target?.Macs.Add(tile.Key);
        InsertSorted(target?.Tiles ?? Ungrouped, tile);
        SaveGroups();
    }

    /// <summary>Adds a new action, or updates the one with the same id.</summary>
    public void SaveAction(SavedAction saved)
    {
        LightAction? existing = Actions.FirstOrDefault(a => a.Id == saved.Id);
        if (existing is not null)
        {
            existing.Update(saved);
            ObservableCollection<TileItem> home = GroupOf(existing)?.Tiles ?? Ungrouped;
            home.Remove(existing);
            InsertSorted(home, existing);
        }
        else
        {
            LightAction action = new(saved);
            Actions.Add(action);
            InsertSorted(Ungrouped, action);
        }

        SaveActions();
    }

    public void DeleteAction(LightAction action)
    {
        BulbGroup? group = GroupOf(action);
        if (group is not null)
        {
            group.Macs.Remove(action.Key);
            group.Tiles.Remove(action);
            SaveGroups();
        }
        else
        {
            Ungrouped.Remove(action);
        }

        Actions.Remove(action);
        SaveActions();
    }

    /// <summary>Applies the action to every light it covers.</summary>
    public void RunAction(LightAction action)
    {
        SavedAction settings = action.Settings;
        HashSet<string> macs = new(settings.Macs, StringComparer.OrdinalIgnoreCase);
        foreach (BulbGroup group in Groups.Where(g => settings.GroupIds.Contains(g.Id)))
            macs.UnionWith(group.Macs);

        List<Bulb> targets = [.. Bulbs.Where(b => macs.Contains(b.Mac))];
        foreach (Bulb bulb in targets)
            bulb.ApplyAction(settings);

        StatusText = targets.Count == 0
            ? $"{action.Name} · no lights found"
            : $"{action.Name} · {(targets.Count == 1 ? "1 light" : $"{targets.Count} lights")}" + (WizClient.DryRun ? " · dry run" : "");
    }

    public BulbGroup CreateGroup()
    {
        string name = "New group";
        for (int n = 2; Groups.Any(g => g.Name == name); n++)
            name = $"New group {n}";

        BulbGroup group = CreateGroupModel(Guid.NewGuid().ToString("N"), name, []);
        Groups.Add(group);
        SaveGroups();
        return group;
    }

    /// <summary>Moves the group <paramref name="offset"/> places up (negative) or down in the list.</summary>
    public void MoveGroup(BulbGroup group, int offset)
    {
        int from = Groups.IndexOf(group);
        int to = from + offset;
        if (from < 0 || to < 0 || to >= Groups.Count)
            return;

        Groups.Move(from, to);
        SaveGroups();
    }

    /// <summary>Deletes the group; its bulbs go back to the ungrouped list.</summary>
    public void DeleteGroup(BulbGroup group)
    {
        foreach (TileItem tile in group.Tiles.ToList())
            InsertSorted(Ungrouped, tile);

        // Actions aimed at the group keep working: they now name its bulbs directly.
        bool actionsChanged = false;
        foreach (LightAction action in Actions.Where(a => a.Settings.GroupIds.Contains(group.Id)))
        {
            action.Settings.GroupIds.Remove(group.Id);
            foreach (string mac in group.Macs.Where(m => !m.StartsWith(LightAction.KeyPrefix, StringComparison.Ordinal)))
            {
                if (!action.Settings.Macs.Contains(mac, StringComparer.OrdinalIgnoreCase))
                    action.Settings.Macs.Add(mac);
            }
            actionsChanged = true;
        }

        Groups.Remove(group);
        SaveGroups();
        if (actionsChanged)
            SaveActions();
    }

    private BulbGroup CreateGroupModel(string id, string name, IEnumerable<string> macs)
    {
        BulbGroup group = new(id, name, macs);
        group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BulbGroup.Name))
                SaveGroups();
        };
        return group;
    }

    private void SaveGroups() => BulbStore.SaveGroups(Groups.Select(g => g.ToSaved()));

    private void SaveActions() => BulbStore.SaveActions(Actions.Select(a => a.Settings));

    private async Task AddOrUpdateAsync(string mac, string ip, JsonObject pilot)
    {
        Bulb? existing = Bulbs.FirstOrDefault(b => string.Equals(b.Mac, mac, StringComparison.OrdinalIgnoreCase));
        Bulb bulb = existing ?? new(mac, ip);
        if (existing is null && _saved.TryGetValue(mac, out SavedBulb? saved))
            bulb.ApplySaved(saved);

        // Capabilities must be in place before state is applied and before the bulb reaches
        // the UI, so bound sliders/pickers never coerce a value and write it back to the bulb.
        if (existing is null || existing.ModuleName.Length == 0)
        {
            Task<JsonObject?> system = WizClient.RequestAsync(ip, "getSystemConfig");
            Task<JsonObject?> model = WizClient.RequestAsync(ip, "getModelConfig");
            bulb.ApplySystemConfig(await system, await model);
        }

        bulb.Ip = ip;
        bulb.ApplyPilot(pilot);

        if (existing is null)
            AddBulb(bulb);
    }

    private static Bulb CreateOfflineBulb(SavedBulb saved)
    {
        Bulb bulb = new(saved.Mac, saved.LastIp ?? "");
        bulb.ApplySaved(saved);
        bulb.IsReachable = false;
        return bulb;
    }

    private void AddBulb(Bulb bulb)
    {
        bulb.PropertyChanged += Bulb_PropertyChanged;
        Bulbs.Add(bulb);
        (GroupOf(bulb)?.Tiles ?? Ungrouped).Add(bulb);
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private void Bulb_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Bulb.Name) or nameof(Bulb.Ip))
            SaveAll();
    }

    private void SortBulbs()
    {
        Sort(Bulbs);
        Sort(Ungrouped);
        foreach (BulbGroup group in Groups)
            Sort(group.Tiles);
    }

    // Actions come first, then bulbs, each by name.
    private static int CompareTiles(TileItem a, TileItem b)
    {
        int byKind = (a is LightAction ? 0 : 1).CompareTo(b is LightAction ? 0 : 1);
        return byKind != 0 ? byKind : StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName);
    }

    private static void Sort<T>(ObservableCollection<T> tiles) where T : TileItem
    {
        List<T> sorted = [.. tiles.Order(Comparer<T>.Create(CompareTiles))];
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = tiles.IndexOf(sorted[i]);
            if (current != i)
                tiles.Move(current, i);
        }
    }

    private static void InsertSorted<T>(ObservableCollection<T> tiles, T tile) where T : TileItem
    {
        int index = 0;
        while (index < tiles.Count && CompareTiles(tiles[index], tile) <= 0)
            index++;
        tiles.Insert(index, tile);
    }

    private void SaveAll()
    {
        foreach (Bulb bulb in Bulbs)
            _saved[bulb.Mac] = bulb.ToSaved();
        BulbStore.Save(_saved.Values);
    }
}

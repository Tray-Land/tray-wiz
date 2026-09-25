namespace TrayWiz.Models;

public sealed record WizScene(int Id, string Name)
{
    public override string ToString() => Name;

    public static readonly IReadOnlyList<WizScene> All =
    [
        new(1, "Ocean"), new(2, "Romance"), new(3, "Sunset"), new(4, "Party"),
        new(5, "Fireplace"), new(6, "Cozy"), new(7, "Forest"), new(8, "Pastel colors"),
        new(9, "Wake up"), new(10, "Bedtime"), new(11, "Warm white"), new(12, "Daylight"),
        new(13, "Cool white"), new(14, "Night light"), new(15, "Focus"), new(16, "Relax"),
        new(17, "True colors"), new(18, "TV time"), new(19, "Plant growth"), new(20, "Spring"),
        new(21, "Summer"), new(22, "Fall"), new(23, "Deep dive"), new(24, "Jungle"),
        new(25, "Mojito"), new(26, "Club"), new(27, "Christmas"), new(28, "Halloween"),
        new(29, "Candlelight"), new(30, "Golden white"), new(31, "Pulse"), new(32, "Steampunk"),
    ];

    // Scenes a tunable-white-only bulb can render (no RGB channels).
    private static readonly HashSet<int> WhiteSceneIds = [6, 9, 10, 11, 12, 13, 14, 15, 16, 18, 29, 30, 31, 32];

    public static readonly IReadOnlyList<WizScene> White = [.. All.Where(s => WhiteSceneIds.Contains(s.Id))];
}

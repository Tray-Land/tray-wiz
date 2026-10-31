using Windows.UI;

namespace TrayWiz.Models;

/// <summary>
/// A built-in WiZ scene. <see cref="Swatch"/> approximates its look (the main color of a changing scene).
/// <see cref="Dimming"/> is set for a scene that runs at a fixed brightness, which its bulbs don't report.
/// </summary>
public sealed record WizScene(int Id, string Name, Color Swatch, int? Dimming = null)
{
    public override string ToString() => Name;

    public static readonly IReadOnlyList<WizScene> All = (List<WizScene>)
    [
        new(1, "Ocean", Rgb(30, 130, 220)), new(2, "Romance", Rgb(235, 70, 120)),
        new(3, "Sunset", Rgb(255, 120, 50)), new(4, "Party", Rgb(220, 50, 200)),
        new(5, "Fireplace", Rgb(255, 100, 30)), new(6, "Cozy", Kelvin(2200)),
        new(7, "Forest", Rgb(60, 170, 70)), new(8, "Pastel colors", Rgb(190, 170, 235)),
        new(9, "Wake up", Kelvin(2500)), new(10, "Bedtime", Kelvin(2200)),
        new(11, "Warm white", Kelvin(2700)), new(12, "Daylight", Kelvin(4200)),
        new(13, "Cool white", Kelvin(6500)), new(14, "Night light", Kelvin(2000), Dimming: 10),
        new(15, "Focus", Kelvin(5000)), new(16, "Relax", Kelvin(2700)),
        new(17, "True colors", Kelvin(4000)), new(18, "TV time", Kelvin(4500)),
        new(19, "Plant growth", Rgb(220, 70, 200)), new(20, "Spring", Rgb(150, 220, 110)),
        new(21, "Summer", Rgb(255, 200, 70)), new(22, "Fall", Rgb(230, 120, 40)),
        new(23, "Deep dive", Rgb(30, 60, 210)), new(24, "Jungle", Rgb(40, 180, 70)),
        new(25, "Mojito", Rgb(120, 230, 130)), new(26, "Club", Rgb(170, 60, 230)),
        new(27, "Christmas", Rgb(220, 40, 40)), new(28, "Halloween", Rgb(255, 110, 20)),
        new(29, "Candlelight", Kelvin(1900)), new(30, "Golden white", Kelvin(3000)),
        new(31, "Pulse", Kelvin(4000)), new(32, "Steampunk", Kelvin(2000)),
    ];

    // Scenes a tunable-white-only bulb can render (no RGB channels).
    private static readonly HashSet<int> WhiteSceneIds = [6, 9, 10, 11, 12, 13, 14, 15, 16, 18, 29, 30, 31, 32];

    public static readonly IReadOnlyList<WizScene> White = (List<WizScene>)[.. All.Where(s => WhiteSceneIds.Contains(s.Id))];

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    private static Color Kelvin(double kelvin) => Bulb.KelvinToColor(kelvin);
}

using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace TrayWiz.Controls;

/// <summary>
/// Lights a tile from its glyph, as if the icon were the bulb. A point light hovers just above the glyph and
/// an ambient light fills in the rest, so the tile is brightest at the icon and falls off toward the far
/// corner. <see cref="SetLevel"/> (the bulb's brightness) raises and strengthens the point light, widening
/// the pool; switching on blooms it up from the glyph and switching off fades it back down.
/// </summary>
internal sealed class GlyphLight
{
    /// <summary>
    /// Composition lights multiply the color they land on, so on their own they could only darken a fully
    /// saturated tint. The ambient light holds the whole tile a little below its color instead, and the point
    /// light lifts it back up around the glyph (and on toward white, where the tint has room).
    /// </summary>
    public const float AmbientIntensity = 0.84f;

    /// <summary>The most the light lifts the tile under its text (the title, the text nearest the glyph).</summary>
    public const double BrightestTextShade = 1.1;

    // The point light's strength and height above the glyph at the lowest and highest levels. Higher
    // spreads the pool wider; a tile is about 120 x 84 with the glyph near its top-left corner.
    private const float MinIntensity = 0.26f;
    private const float MaxIntensity = 0.56f;
    private const float MinHeight = 12;
    private const float MaxHeight = 24;

    // Switching on overshoots to this power before settling, a brief bloom like a bulb striking.
    private const float BloomPower = 1.25f;

    private static readonly TimeSpan SwitchOnDuration = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan SwitchOffDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan LevelDuration = TimeSpan.FromMilliseconds(300);

    private readonly Compositor _compositor;
    private readonly Visual[] _targets;
    private readonly AmbientLight _ambient;
    private readonly PointLight _point;
    private readonly CubicBezierEasingFunction _decelerate;
    private readonly CubicBezierEasingFunction _settle;

    // Power (0 off, 1 on), Level (0 to 1) and Source (the glyph's center); the point light's expressions read
    // all three, so animating Power or Level here moves everything they drive.
    private readonly CompositionPropertySet _glow;

    private bool _isOn;
    private bool _isFading;

    // Bumped by every switch, so a fade that was overtaken by switching back on doesn't then go dark.
    private int _switches;

    public GlyphLight(UIElement space, FrameworkElement glyph, params UIElement[] targets)
    {
        Visual spaceVisual = ElementCompositionPreview.GetElementVisual(space);
        _compositor = spaceVisual.Compositor;
        _targets = Array.ConvertAll(targets, ElementCompositionPreview.GetElementVisual);

        _glow = _compositor.CreatePropertySet();
        _glow.InsertScalar("Power", 0);
        _glow.InsertScalar("Level", 1);
        _glow.InsertVector2("Source", Vector2.Zero);

        _decelerate = _compositor.CreateCubicBezierEasingFunction(new(0, 0), new(0, 1));
        _settle = _compositor.CreateCubicBezierEasingFunction(new(0.55f, 0.55f), new(0, 1));

        _ambient = _compositor.CreateAmbientLight();
        _ambient.Color = Colors.White;
        _ambient.Intensity = AmbientIntensity;

        _point = _compositor.CreatePointLight();
        _point.Color = Colors.White;
        _point.CoordinateSpace = spaceVisual;
        _point.StartAnimation("Intensity", Expression("glow.Power * Lerp(MinIntensity, MaxIntensity, glow.Level)"));
        _point.StartAnimation("Offset", Expression("Vector3(glow.Source.X, glow.Source.Y, Lerp(MinHeight, MaxHeight, glow.Level))"));

        glyph.SizeChanged += (_, _) =>
        {
            Windows.Foundation.Point center = glyph.TransformToVisual(space).TransformPoint(new(glyph.ActualWidth / 2, glyph.ActualHeight / 2));
            _glow.InsertVector2("Source", new((float)center.X, (float)center.Y));
        };
    }

    /// <summary>Raised once the light is fully off: at once, or when switching off has faded it out.</summary>
    public event EventHandler? Dark;

    /// <summary>Sets how strongly the light glows while on, from 0 (dimmest) to 1.</summary>
    public void SetLevel(double level, bool animate)
    {
        if (!animate)
        {
            Set("Level", (float)level);
            return;
        }

        ScalarKeyFrameAnimation animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, (float)level, _decelerate);
        animation.Duration = LevelDuration;
        _glow.StartAnimation("Level", animation);
    }

    public void SwitchOn(bool animate)
    {
        if (_isOn)
            return;

        // A fading light is still on its targets; it just blooms back up from wherever it got to.
        if (!_isFading)
        {
            foreach (Visual target in _targets)
            {
                _ambient.Targets.Add(target);
                _point.Targets.Add(target);
            }
        }

        _isOn = true;
        _isFading = false;
        _switches++;

        if (!animate)
        {
            Set("Power", 1);
            return;
        }

        ScalarKeyFrameAnimation bloom = _compositor.CreateScalarKeyFrameAnimation();
        bloom.InsertExpressionKeyFrame(0, "this.StartingValue");
        bloom.InsertKeyFrame(0.3f, BloomPower, _decelerate);
        bloom.InsertKeyFrame(1, 1, _settle);
        bloom.Duration = SwitchOnDuration;
        _glow.StartAnimation("Power", bloom);
    }

    /// <summary>Fades the light out (or puts it out at once), then raises <see cref="Dark"/>.</summary>
    public void SwitchOff(bool animate)
    {
        if (_isFading)
            return;

        if (!_isOn || !animate)
        {
            GoDark();
            return;
        }

        _isOn = false;
        _isFading = true;
        int switches = ++_switches;

        ScalarKeyFrameAnimation fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertExpressionKeyFrame(0, "this.StartingValue");
        fade.InsertKeyFrame(1, 0, _decelerate);
        fade.Duration = SwitchOffDuration;

        CompositionScopedBatch batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _glow.StartAnimation("Power", fade);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (switches == _switches)
                GoDark();
        };
    }

    private void GoDark()
    {
        _isOn = false;
        _isFading = false;
        Set("Power", 0);
        _ambient.Targets.RemoveAll();
        _point.Targets.RemoveAll();
        Dark?.Invoke(this, EventArgs.Empty);
    }

    private void Set(string property, float value)
    {
        _glow.StopAnimation(property);
        _glow.InsertScalar(property, value);
    }

    private ExpressionAnimation Expression(string expression)
    {
        ExpressionAnimation animation = _compositor.CreateExpressionAnimation(expression);
        animation.SetReferenceParameter("glow", _glow);
        animation.SetScalarParameter("MinIntensity", MinIntensity);
        animation.SetScalarParameter("MaxIntensity", MaxIntensity);
        animation.SetScalarParameter("MinHeight", MinHeight);
        animation.SetScalarParameter("MaxHeight", MaxHeight);
        return animation;
    }
}

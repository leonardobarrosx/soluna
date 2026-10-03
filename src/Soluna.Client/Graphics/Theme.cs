using Microsoft.Xna.Framework;

namespace Soluna.Client.Graphics;

/// <summary>
/// Dark palette for every bit of UI. Moonlight violet and sun gold as the two accents.
/// SpriteBatch blends premultiplied alpha, so translucent colors are written as Color * opacity.
/// </summary>
internal static class Theme
{
    public static readonly Color Background = new(11, 12, 18);
    public static readonly Color Panel = new Color(20, 22, 30) * 0.92f;
    public static readonly Color PanelRaised = new Color(30, 33, 44) * 0.96f;
    public static readonly Color Border = new(48, 52, 68);
    public static readonly Color Text = new(222, 224, 235);
    public static readonly Color TextDim = new(138, 143, 163);
    public static readonly Color Luna = new(178, 160, 255);
    public static readonly Color Sol = new(240, 184, 96);
    public static readonly Color Danger = new(235, 96, 110);
    public static readonly Color System = new(120, 200, 170);

    /// <summary>Night tint laid over the world; the light around the player cuts through it.</summary>
    public static readonly Color Night = new(6, 8, 26);
    public const float NightStrength = 0.66f;
}

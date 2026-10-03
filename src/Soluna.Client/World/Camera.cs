using Microsoft.Xna.Framework;

namespace Soluna.Client.World;

internal sealed class Camera
{
    public const float MinZoom = 1, MaxZoom = 4;

    /// <summary>Top-left of the view in world pixels.</summary>
    public Vector2 Position { get; private set; }

    public float Zoom { get; set; } = 2;

    public Point Viewport { get; set; }

    public Vector2 WorldSize => new(Viewport.X / Zoom, Viewport.Y / Zoom);

    public Matrix Transform =>
        Matrix.CreateTranslation(-MathF.Round(Position.X * Zoom) / Zoom, -MathF.Round(Position.Y * Zoom) / Zoom, 0)
        * Matrix.CreateScale(Zoom);

    /// <summary>Centers on a point, keeping the view inside the map (or centering a map smaller than the view).</summary>
    public void Follow(Vector2 target, Vector2 mapPixels)
    {
        var view = WorldSize;
        var pos = target - view / 2;
        pos.X = mapPixels.X <= view.X ? (mapPixels.X - view.X) / 2 : Math.Clamp(pos.X, 0, mapPixels.X - view.X);
        pos.Y = mapPixels.Y <= view.Y ? (mapPixels.Y - view.Y) / 2 : Math.Clamp(pos.Y, 0, mapPixels.Y - view.Y);
        Position = pos;
    }

    public Vector2 ScreenToWorld(Vector2 screen) => screen / Zoom + Position;

    public Vector2 WorldToScreen(Vector2 world) => (world - Position) * Zoom;
}

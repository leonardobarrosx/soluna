using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Soluna.Client.Graphics;

/// <summary>A tiny CPU pixel buffer for painting placeholder art before uploading it as a texture.</summary>
internal sealed class Canvas(int width, int height)
{
    private readonly Color[] _pixels = new Color[width * height];

    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Drawing offset, so tile painters can work in 0..31 coordinates.</summary>
    public Point Origin { get; set; }

    /// <summary>Clip rectangle in canvas space; nothing is written outside it.</summary>
    public Rectangle Clip { get; set; } = new(0, 0, width, height);

    public void Set(int x, int y, Color c)
    {
        x += Origin.X;
        y += Origin.Y;
        if (!Clip.Contains(x, y)) return;
        if (c.A == 255)
        {
            _pixels[y * Width + x] = c;
            return;
        }
        // Alpha blend over what is there, for shadows and highlights.
        var dst = _pixels[y * Width + x];
        var a = c.A / 255f;
        _pixels[y * Width + x] = new Color(
            (byte)(c.R * a + dst.R * (1 - a)),
            (byte)(c.G * a + dst.G * (1 - a)),
            (byte)(c.B * a + dst.B * (1 - a)),
            (byte)Math.Max(dst.A, c.A));
    }

    public void Fill(int x, int y, int w, int h, Color c)
    {
        for (var j = y; j < y + h; j++)
        for (var i = x; i < x + w; i++)
            Set(i, j, c);
    }

    public void Ellipse(float cx, float cy, float rx, float ry, Color c)
    {
        for (var j = (int)(cy - ry); j <= (int)(cy + ry); j++)
        for (var i = (int)(cx - rx); i <= (int)(cx + rx); i++)
        {
            var dx = (i + 0.5f - cx) / rx;
            var dy = (j + 0.5f - cy) / ry;
            if (dx * dx + dy * dy <= 1) Set(i, j, c);
        }
    }

    public void Circle(float cx, float cy, float r, Color c) => Ellipse(cx, cy, r, r, c);

    public void Speckle(int x, int y, int w, int h, Random rng, float density, params Color[] colors)
    {
        for (var j = y; j < y + h; j++)
        for (var i = x; i < x + w; i++)
            if (rng.NextDouble() < density) Set(i, j, colors[rng.Next(colors.Length)]);
    }

    public Texture2D ToTexture(GraphicsDevice device)
    {
        var texture = new Texture2D(device, Width, Height);
        texture.SetData(_pixels);
        return texture;
    }
}

using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;

namespace Soluna.Client.UI;

internal static class Ui
{
    public static void Panel(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color? fill = null)
    {
        batch.Draw(pixel, rect, fill ?? Theme.Panel);
        Outline(batch, pixel, rect, Theme.Border);
    }

    public static void Outline(SpriteBatch batch, Texture2D pixel, Rectangle r, Color color, int thickness = 1)
    {
        batch.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, thickness), color);
        batch.Draw(pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, thickness, r.Height), color);
        batch.Draw(pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
    }

    public static void Text(SpriteBatch batch, SpriteFontBase font, string text, Vector2 position, Color color)
    {
        position = Vector2.Round(position);
        batch.DrawString(font, text, position + Vector2.One, Color.Black * 0.63f);
        batch.DrawString(font, text, position, color);
    }

    /// <summary>A short label on a dark plate, centered on <paramref name="center"/>.</summary>
    public static void Tag(SpriteBatch batch, Texture2D pixel, SpriteFontBase font, string text, Vector2 center, Color color)
    {
        var size = font.MeasureString(text);
        var rect = new Rectangle((int)(center.X - size.X / 2 - 5), (int)(center.Y - size.Y / 2 - 1), (int)size.X + 10, (int)size.Y + 2);
        batch.Draw(pixel, rect, new Color(10, 11, 16) * 0.67f);
        batch.DrawString(font, text, new Vector2(rect.X + 5, rect.Y + 1), color);
    }
}

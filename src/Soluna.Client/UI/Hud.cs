using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;

namespace Soluna.Client.UI;

/// <summary>Your health, mana and experience, under the status bar.</summary>
internal sealed class Hud
{
    public int Hp { get; set; }
    public int MaxHp { get; set; } = 1;
    public int Mp { get; set; }
    public int MaxMp { get; set; } = 1;
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int ExpToNext { get; set; } = 1;

    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts)
    {
        var panel = new Rectangle(12, 50, 236, 62);
        Ui.Panel(batch, pixel, panel);
        Ui.Text(batch, fonts.Title, Level.ToString(), new Vector2(panel.X + 12, panel.Y + 14), Theme.Sol);
        Ui.Text(batch, fonts.Small, "nível", new Vector2(panel.X + 10, panel.Y + 40), Theme.TextDim);

        var x = panel.X + 52;
        var width = panel.Width - 64;
        Bar(batch, pixel, fonts, new Rectangle(x, panel.Y + 9, width, 14), Hp, MaxHp, Theme.Danger, $"HP {Hp}/{MaxHp}");
        Bar(batch, pixel, fonts, new Rectangle(x, panel.Y + 27, width, 14), Mp, MaxMp, Theme.Luna, $"MP {Mp}/{MaxMp}");
        Bar(batch, pixel, fonts, new Rectangle(x, panel.Y + 46, width, 6), Exp, Math.Max(1, ExpToNext), Theme.Sol, null);
    }

    public static void Bar(SpriteBatch batch, Texture2D pixel, Fonts fonts, Rectangle rect, int value, int max, Color color, string? label)
    {
        batch.Draw(pixel, rect, Theme.Background);
        var fill = max > 0 ? (int)(rect.Width * Math.Clamp(value / (float)max, 0, 1)) : 0;
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, fill, rect.Height), color * 0.85f);
        Ui.Outline(batch, pixel, rect, Theme.Border);
        if (label == null) return;
        var size = fonts.Small.MeasureString(label);
        Ui.Text(batch, fonts.Small, label, new Vector2(rect.Center.X - size.X / 2, rect.Center.Y - size.Y / 2 - 1), Theme.Text);
    }
}

/// <summary>Damage and heal numbers that float up from where they happened and fade.</summary>
internal sealed class FloatingText
{
    private const float Seconds = 0.9f;

    private readonly List<(Vector2 world, string text, Color color, float age)> _items = [];

    public void Add(Vector2 world, int change)
    {
        var text = change < 0 ? (-change).ToString() : $"+{change}";
        var color = change < 0 ? Theme.Text : Theme.System;
        _items.Add((world, text, color, 0));
    }

    public void Update(float dt)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.age += dt;
            if (item.age >= Seconds) _items.RemoveAt(i);
            else _items[i] = item;
        }
    }

    public void Draw(SpriteBatch batch, Fonts fonts, Func<Vector2, Vector2> toScreen)
    {
        foreach (var (world, text, color, age) in _items)
        {
            var t = age / Seconds;
            var screen = toScreen(world) - new Vector2(0, 18 + t * 26);
            var size = fonts.Body.MeasureString(text);
            Ui.Text(batch, fonts.Body, text, screen - new Vector2(size.X / 2, 0), color * (1 - t * t));
        }
    }
}

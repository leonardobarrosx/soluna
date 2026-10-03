using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;

namespace Soluna.Client.UI;

/// <summary>A one-line text box fed by the window's text input. Masked fields show dots.</summary>
internal sealed class TextField(string label, int maxLength, bool masked = false)
{
    private readonly StringBuilder _text = new();

    public string Label { get; } = label;
    public string Text => _text.ToString();

    public void Set(string text)
    {
        _text.Clear();
        _text.Append(text.Length > maxLength ? text[..maxLength] : text);
    }

    public void OnTextInput(char c)
    {
        if (c == '\b')
        {
            if (_text.Length > 0) _text.Length--;
            return;
        }
        if (!char.IsControl(c) && _text.Length < maxLength) _text.Append(c);
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts, Rectangle rect, bool focused, float time)
    {
        batch.Draw(pixel, rect, focused ? Theme.PanelRaised : Theme.Background * 0.6f);
        Ui.Outline(batch, pixel, rect, focused ? Theme.Luna : Theme.Border);
        Ui.Text(batch, fonts.Small, Label, new Vector2(rect.X + 12, rect.Y + 4), Theme.TextDim);
        var shown = masked ? new string('•', _text.Length) : Text;
        var caret = focused && time % 1 < 0.5f ? "|" : "";
        Ui.Text(batch, fonts.Body, shown + caret, new Vector2(rect.X + 12, rect.Y + 20), Theme.Text);
    }
}

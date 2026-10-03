using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;
using Soluna.Shared;

namespace Soluna.Client.UI;

internal sealed class ChatBox
{
    private const int VisibleLines = 8;
    private const int Width = 460;

    private readonly List<(string text, Color color)> _lines = [];
    private readonly StringBuilder _input = new();

    public bool Typing { get; private set; }

    public void Add(string from, string text)
    {
        _lines.Add(from.Length == 0 ? (text, Theme.System) : ($"{from}: {text}", Theme.Text));
        if (_lines.Count > 100) _lines.RemoveAt(0);
    }

    public void Open()
    {
        Typing = true;
        _input.Clear();
    }

    public void Cancel()
    {
        Typing = false;
        _input.Clear();
    }

    /// <summary>Closes the input and returns what was typed, or null if it was empty.</summary>
    public string? Submit()
    {
        var text = _input.ToString().Trim();
        Cancel();
        return text.Length == 0 ? null : text;
    }

    public void OnTextInput(char c)
    {
        if (!Typing) return;
        if (c == '\b')
        {
            if (_input.Length > 0) _input.Length--;
            return;
        }
        if (!char.IsControl(c) && _input.Length < Constants.MaxChatLength) _input.Append(c);
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts, Point screen)
    {
        var shown = Math.Min(VisibleLines, _lines.Count);
        if (shown == 0 && !Typing) return;

        var lineHeight = fonts.Body.LineHeight + 2;
        var height = shown * lineHeight + 16 + (Typing ? lineHeight + 10 : 0);
        var rect = new Rectangle(12, screen.Y - height - 12, Width, height);
        Ui.Panel(batch, pixel, rect, Typing ? Theme.Panel : new Color(14, 15, 22) * 0.6f);

        var y = rect.Y + 8;
        var start = _lines.Count - shown;
        for (var i = start; i < _lines.Count; i++)
        {
            Ui.Text(batch, fonts.Body, _lines[i].text, new Vector2(rect.X + 10, y), _lines[i].color);
            y += lineHeight;
        }

        if (!Typing) return;
        var inputRect = new Rectangle(rect.X + 6, rect.Bottom - lineHeight - 10, rect.Width - 12, lineHeight + 4);
        batch.Draw(pixel, inputRect, Theme.PanelRaised);
        Ui.Outline(batch, pixel, inputRect, Theme.Luna);
        var caret = DateTime.Now.Millisecond < 500 ? "|" : "";
        Ui.Text(batch, fonts.Body, $"{_input}{caret}", new Vector2(inputRect.X + 6, inputRect.Y + 2), Theme.Text);
    }
}

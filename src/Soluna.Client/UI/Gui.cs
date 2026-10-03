using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;

namespace Soluna.Client.UI;

/// <summary>
/// A small immediate-mode widget kit for the editors: each widget is drawn and handles its input in the
/// same call, during Draw, and returns the (possibly changed) value. Text fields are identified by an id
/// so focus and half-typed numbers survive between frames.
/// </summary>
internal sealed class Gui(Texture2D pixel, Fonts fonts, Input input)
{
    private const int FieldHeight = 34;

    private readonly StringBuilder _typed = new();
    private readonly Dictionary<string, string> _numberDrafts = [];
    private readonly Dictionary<string, int> _scroll = [];
    private SpriteBatch _batch = null!;
    private bool _clickTaken;
    private float _time;

    /// <summary>The text field being typed into, if any.</summary>
    public string? Focus { get; private set; }

    public bool Typing => Focus != null;

    public Fonts Fonts => fonts;

    public void OnTextInput(char c)
    {
        if (Focus != null) _typed.Append(c);
    }

    public void Begin(SpriteBatch batch, float dt)
    {
        _batch = batch;
        _clickTaken = false;
        _time += dt;
        if (input.Pressed(Keys.Escape) || input.Pressed(Keys.Enter) || input.Pressed(Keys.Tab)) Focus = null;
    }

    /// <summary>Clicking anywhere that is not a field drops the focus.</summary>
    public void End()
    {
        if (input.LeftPressed && !_clickTaken) Focus = null;
        _typed.Clear();
    }

    private Point Mouse => input.Mouse.ToPoint();

    public bool Hover(Rectangle r) => r.Contains(Mouse);

    private bool Clicked(Rectangle r)
    {
        if (!input.LeftPressed || !r.Contains(Mouse)) return false;
        _clickTaken = true;
        return true;
    }

    public void Panel(Rectangle r, Color? fill = null) => Ui.Panel(_batch, pixel, r, fill);

    public void Fill(Rectangle r, Color color) => _batch.Draw(pixel, r, color);

    public void Label(Vector2 at, string text, Color? color = null, bool small = false) =>
        Ui.Text(_batch, small ? fonts.Small : fonts.Body, text, at, color ?? Theme.Text);

    public void Title(Vector2 at, string text) => Ui.Text(_batch, fonts.Title, text, at, Theme.Luna);

    public bool Button(Rectangle r, string text, bool primary = false, bool enabled = true)
    {
        var hover = enabled && Hover(r);
        _batch.Draw(pixel, r, primary && enabled ? Theme.Luna * (hover ? 0.4f : 0.25f) : hover ? Theme.PanelRaised : Theme.Panel);
        Ui.Outline(_batch, pixel, r, primary && enabled ? Theme.Luna : hover ? Theme.TextDim : Theme.Border);
        var size = fonts.Body.MeasureString(text);
        Ui.Text(_batch, fonts.Body, text, new Vector2(r.Center.X - size.X / 2, r.Center.Y - size.Y / 2), enabled ? Theme.Text : Theme.TextDim);
        return enabled && Clicked(r);
    }

    /// <summary>A labelled box: label in small type at the top, content below.</summary>
    private void Box(Rectangle r, string label, bool focused)
    {
        _batch.Draw(pixel, r, focused ? Theme.PanelRaised : Theme.Background * 0.7f);
        Ui.Outline(_batch, pixel, r, focused ? Theme.Luna : Theme.Border);
        Ui.Text(_batch, fonts.Small, label, new Vector2(r.X + 8, r.Y + 2), Theme.TextDim);
    }

    public static Rectangle Field(int x, int y, int width) => new(x, y, width, FieldHeight);

    public string TextField(string id, Rectangle r, string label, string value, int max = 40)
    {
        if (Clicked(r)) Focus = id;
        var focused = Focus == id;
        if (focused) value = Apply(value, max);
        Box(r, label, focused);
        var caret = focused && _time % 1 < 0.5f ? "|" : "";
        Ui.Text(_batch, fonts.Body, value + caret, new Vector2(r.X + 8, r.Y + 14), Theme.Text);
        return value;
    }

    /// <summary>A whole number: typed in when focused, or nudged with the − and + buttons.</summary>
    public int NumberField(string id, Rectangle r, string label, int value, int min, int max, int step = 1)
    {
        var minus = new Rectangle(r.Right - 52, r.Y + 6, 22, r.Height - 12);
        var plus = new Rectangle(r.Right - 26, r.Y + 6, 22, r.Height - 12);
        if (Button(minus, "−")) value -= step;
        if (Button(plus, "+")) value += step;

        var textArea = new Rectangle(r.X, r.Y, r.Width - 56, r.Height);
        if (Clicked(textArea))
        {
            Focus = id;
            _numberDrafts[id] = value.ToString();
        }
        var focused = Focus == id;
        if (focused)
        {
            var draft = Apply(_numberDrafts.GetValueOrDefault(id, value.ToString()), 9);
            draft = new string(draft.Where((c, i) => char.IsDigit(c) || c == '-' && i == 0).ToArray());
            _numberDrafts[id] = draft;
            if (int.TryParse(draft, out var typed)) value = typed;
        }
        else
        {
            _numberDrafts.Remove(id);
        }

        value = Math.Clamp(value, min, max);
        Box(new Rectangle(r.X, r.Y, r.Width - 56, r.Height), label, focused);
        var shown = focused ? _numberDrafts.GetValueOrDefault(id, "") + (_time % 1 < 0.5f ? "|" : "") : value.ToString();
        Ui.Text(_batch, fonts.Body, shown, new Vector2(r.X + 8, r.Y + 14), Theme.Text);
        return value;
    }

    /// <summary>One value out of a few, stepped with the arrows or a click.</summary>
    public int Cycle(Rectangle r, string label, string[] options, int index)
    {
        if (options.Length == 0) return index;
        var left = new Rectangle(r.X + 4, r.Y + 14, 22, 18);
        var right = new Rectangle(r.Right - 26, r.Y + 14, 22, 18);
        if (Clicked(left)) index = (index - 1 + options.Length) % options.Length;
        else if (Clicked(right) || Clicked(r)) index = (index + 1) % options.Length;
        index = Math.Clamp(index, 0, options.Length - 1);

        Box(r, label, Hover(r));
        var text = options[index];
        var size = fonts.Body.MeasureString(text);
        Ui.Text(_batch, fonts.Body, text, new Vector2(r.Center.X - size.X / 2, r.Y + 14), Theme.Text);
        Ui.Text(_batch, fonts.Body, "<", new Vector2(left.X + 6, left.Y - 1), Theme.TextDim);
        Ui.Text(_batch, fonts.Body, ">", new Vector2(right.X + 6, right.Y - 1), Theme.TextDim);
        return index;
    }

    public bool Toggle(Rectangle r, string label, bool value)
    {
        if (Clicked(r)) value = !value;
        Box(r, label, Hover(r));
        var check = new Rectangle(r.X + 8, r.Y + 16, 14, 14);
        _batch.Draw(pixel, check, value ? Theme.Sol : Theme.Background);
        Ui.Outline(_batch, pixel, check, Theme.Border);
        Ui.Text(_batch, fonts.Body, value ? "Sim" : "Não", new Vector2(check.Right + 8, r.Y + 14), Theme.Text);
        return value;
    }

    /// <summary>A scrolling list. Returns the selected row, which a click changes.</summary>
    public int List(string id, Rectangle r, IReadOnlyList<string> rows, int selected, int rowHeight = 24)
    {
        _batch.Draw(pixel, r, Theme.Background * 0.8f);
        Ui.Outline(_batch, pixel, r, Theme.Border);

        var visible = Math.Max(1, (r.Height - 4) / rowHeight);
        var scroll = _scroll.GetValueOrDefault(id);
        if (Hover(r)) scroll -= input.Wheel * 3;
        scroll = Math.Clamp(scroll, 0, Math.Max(0, rows.Count - visible));
        _scroll[id] = scroll;

        for (var i = scroll; i < Math.Min(rows.Count, scroll + visible); i++)
        {
            var row = new Rectangle(r.X + 2, r.Y + 2 + (i - scroll) * rowHeight, r.Width - 4, rowHeight - 1);
            if (Clicked(row)) selected = i;
            if (i == selected) _batch.Draw(pixel, row, Theme.Luna * 0.25f);
            else if (Hover(row)) _batch.Draw(pixel, row, Theme.PanelRaised);
            Ui.Text(_batch, fonts.Small, Fit(rows[i], row.Width - 12), new Vector2(row.X + 6, row.Y + 4), i == selected ? Theme.Text : Theme.TextDim);
        }
        if (rows.Count > visible)
        {
            var track = new Rectangle(r.Right - 4, r.Y + 2, 2, r.Height - 4);
            var thumbHeight = Math.Max(12, track.Height * visible / rows.Count);
            var thumbY = track.Y + (track.Height - thumbHeight) * scroll / Math.Max(1, rows.Count - visible);
            _batch.Draw(pixel, new Rectangle(track.X, thumbY, 2, thumbHeight), Theme.Luna);
        }
        return selected;
    }

    /// <summary>Scrolls a list so a row is in view, for when the selection changes from outside.</summary>
    public void Reveal(string id, int row, int rowsVisible)
    {
        var scroll = _scroll.GetValueOrDefault(id);
        if (row < scroll) scroll = row;
        else if (row >= scroll + rowsVisible) scroll = row - rowsVisible + 1;
        _scroll[id] = Math.Max(0, scroll);
    }

    public void Image(Texture2D texture, Rectangle source, Rectangle target) =>
        _batch.Draw(texture, target, source, Color.White);

    private string Fit(string text, int width)
    {
        if (fonts.Small.MeasureString(text).X <= width) return text;
        while (text.Length > 1 && fonts.Small.MeasureString(text + "…").X > width) text = text[..^1];
        return text + "…";
    }

    private string Apply(string value, int max)
    {
        foreach (var c in _typed.ToString())
        {
            if (c == '\b')
            {
                if (value.Length > 0) value = value[..^1];
            }
            else if (!char.IsControl(c) && value.Length < max)
            {
                value += c;
            }
        }
        return value;
    }
}

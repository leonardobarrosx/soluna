using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client.UI;

internal enum CreationAction
{
    None,
    Confirm,
    Cancel,
}

/// <summary>
/// Character creation: a name plus the choices the active art set offers (body, race, skin,
/// hair, hair colour, beard, eyes), with a live walking preview wearing the starter clothes.
/// Rows with a single choice are left out.
/// </summary>
internal sealed class CreationScreen
{
    private const int RowHeight = 40;
    private const int RowGap = 5;
    private const int NameRow = 0;

    // Appearance fields, in Appearance's constructor order.
    private const int Body = 0, Skin = 1, Hair = 2, HairColor = 3, Eyes = 4, Race = 5, Beard = 6;

    private readonly Sprites _sprites;
    private readonly Equipment _preview;
    private readonly StringBuilder _name;
    private readonly int[] _values = new int[7];
    private readonly List<(string label, int field, Choice[] choices)> _rows;
    private readonly Random _rng = new();
    private int _row;
    private float _time;

    public CreationScreen(Sprites sprites, ItemCatalog items, string name)
    {
        _sprites = sprites;
        _preview = items.StarterEquipment();
        _name = new StringBuilder(name.Length > Constants.MaxNameLength ? name[..Constants.MaxNameLength] : name);

        var o = CharacterOptions.Current;
        _rows = new List<(string, int, Choice[])>
        {
            ("Nome", -1, []),
            (o.Label("bodies", "Corpo"), Body, o.Bodies),
            (o.Label("races", "Raça"), Race, o.Races),
            (o.Label("skins", "Pele"), Skin, o.Skins),
            (o.Label("hair", "Cabelo"), Hair, o.Hair),
            (o.Label("hairColors", "Cor do cabelo"), HairColor, o.HairColors),
            (o.Label("beards", "Barba"), Beard, o.Beards),
            (o.Label("eyes", "Olhos"), Eyes, o.Eyes),
        }.Where(r => r.Item2 < 0 || r.Item3.Length > 1).ToList();
        Randomize();
    }

    public string Name => _name.ToString().Trim();

    /// <summary>Why the server refused the last attempt, if it did.</summary>
    public string Message { get; set; } = "";

    public Appearance Look => new(
        (byte)_values[Body], (byte)_values[Skin], (byte)_values[Hair], (byte)_values[HairColor],
        (byte)_values[Eyes], (byte)_values[Race], (byte)_values[Beard]);

    private string ValueLabel(int row)
    {
        var (_, field, choices) = _rows[row];
        return choices.Length > 0 ? choices[_values[field] % choices.Length].Label : "";
    }

    /// <summary>A random natural look; race and beard stay as they are so R does not keep adding cat ears.</summary>
    private void Randomize()
    {
        var look = Appearance.Random(_rng);
        _values[Body] = look.Body;
        _values[Skin] = look.Skin;
        _values[Hair] = look.Hair;
        _values[HairColor] = look.HairColor;
        _values[Eyes] = look.Eyes;
    }

    private void Step(int row, int delta)
    {
        var (_, field, choices) = _rows[row];
        if (choices.Length > 0) _values[field] = (_values[field] + delta + choices.Length) % choices.Length;
    }

    public void OnTextInput(char c)
    {
        if (_row != NameRow) return;
        if (c == '\b')
        {
            if (_name.Length > 0) _name.Length--;
            return;
        }
        if (!char.IsControl(c) && _name.Length < Constants.MaxNameLength) _name.Append(c);
    }

    public CreationAction Update(Input input, Point screen, float dt)
    {
        _time += dt;
        if (input.Pressed(Keys.Escape)) return CreationAction.Cancel;

        if (input.Pressed(Keys.Down) || input.Pressed(Keys.Tab)) _row = (_row + 1) % _rows.Count;
        if (input.Pressed(Keys.Up)) _row = (_row - 1 + _rows.Count) % _rows.Count;
        if (input.Pressed(Keys.Left)) Step(_row, -1);
        if (input.Pressed(Keys.Right)) Step(_row, 1);
        if (_row != NameRow && input.Pressed(Keys.R)) Randomize();

        var layout = Layout(screen);
        var mouse = input.Mouse.ToPoint();
        if (input.LeftPressed)
        {
            for (var row = 0; row < _rows.Count; row++)
            {
                var rect = RowRect(layout, row);
                if (!rect.Contains(mouse)) continue;
                _row = row;
                if (LeftArrow(rect).Contains(mouse)) Step(row, -1);
                if (RightArrow(rect).Contains(mouse)) Step(row, 1);
            }
            if (RandomButton(layout).Contains(mouse)) Randomize();
            if (EnterButton(layout).Contains(mouse) && Name.Length > 0) return CreationAction.Confirm;
        }

        return input.Pressed(Keys.Enter) && Name.Length > 0 ? CreationAction.Confirm : CreationAction.None;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts, Point screen)
    {
        var layout = Layout(screen);
        Ui.Panel(batch, pixel, layout);

        Ui.Text(batch, fonts.Title, Constants.GameName, new Vector2(layout.X + 28, layout.Y + 20), Theme.Luna);
        Ui.Text(batch, fonts.Body, "Crie seu personagem", new Vector2(layout.X + 28, layout.Y + 50), Theme.TextDim);

        DrawPreview(batch, pixel, fonts, new Rectangle(layout.X + 28, layout.Y + 90, 260, 300));

        for (var row = 0; row < _rows.Count; row++)
        {
            var rect = RowRect(layout, row);
            var selected = row == _row;
            batch.Draw(pixel, rect, selected ? Theme.PanelRaised : Theme.Background * 0.6f);
            Ui.Outline(batch, pixel, rect, selected ? Theme.Luna : Theme.Border);
            Ui.Text(batch, fonts.Small, _rows[row].label, new Vector2(rect.X + 12, rect.Y + 3), Theme.TextDim);

            if (row == NameRow)
            {
                var caret = selected && _time % 1 < 0.5f ? "|" : "";
                Ui.Text(batch, fonts.Body, $"{_name}{caret}", new Vector2(rect.X + 12, rect.Y + 17), Theme.Text);
                continue;
            }

            var value = ValueLabel(row);
            var size = fonts.Body.MeasureString(value);
            Ui.Text(batch, fonts.Body, value, new Vector2(rect.Center.X - size.X / 2, rect.Y + 17), Theme.Text);
            Arrow(batch, fonts, LeftArrow(rect), "<", selected);
            Arrow(batch, fonts, RightArrow(rect), ">", selected);
        }

        var random = RandomButton(layout);
        batch.Draw(pixel, random, Theme.PanelRaised);
        Ui.Outline(batch, pixel, random, Theme.Border);
        Centered(batch, fonts, random, "Aleatório (R)", Theme.TextDim);

        var enter = EnterButton(layout);
        var ready = Name.Length > 0;
        batch.Draw(pixel, enter, ready ? Theme.Luna * 0.25f : Theme.PanelRaised);
        Ui.Outline(batch, pixel, enter, ready ? Theme.Luna : Theme.Border);
        Centered(batch, fonts, enter, "Criar personagem (Enter)", ready ? Theme.Text : Theme.TextDim);

        if (Message.Length > 0)
        {
            var messageSize = fonts.Body.MeasureString(Message);
            Ui.Text(batch, fonts.Body, Message, new Vector2(layout.X + 28 + 130 - messageSize.X / 2, layout.Bottom - 50), Theme.Danger);
        }

        const string help = "Setas escolhem e mudam  ·  R sorteia  ·  Esc volta";
        var helpSize = fonts.Small.MeasureString(help);
        Ui.Text(batch, fonts.Small, help, new Vector2(layout.Center.X - helpSize.X / 2, layout.Bottom + 12), Theme.TextDim);
    }

    private void DrawPreview(SpriteBatch batch, Texture2D pixel, Fonts fonts, Rectangle box)
    {
        batch.Draw(pixel, box, Theme.Background);
        Ui.Outline(batch, pixel, box, Theme.Border);

        // Walk on the spot, turning every couple of seconds.
        Direction[] turns = [Direction.Down, Direction.Left, Direction.Up, Direction.Right];
        var dir = turns[(int)(_time / 2.2f) % turns.Length];
        var cycle = _time * 1000 / Constants.WalkTimeMs;
        var sheet = _sprites.SheetFor(Look, _preview);
        var frame = SheetLayout.Frame(sheet, dir, true, cycle % 1, (int)cycle % 2 == 0);

        var scale = SheetLayout.IsLpc(sheet) ? 4f : 6f;
        var size = new Vector2(frame.Width, frame.Height) * scale;
        var pos = new Vector2(box.Center.X - size.X / 2, box.Bottom - size.Y - 14);
        batch.Draw(pixel, new Rectangle(box.Center.X - 46, box.Bottom - 30, 92, 10), Color.Black * 0.35f);
        batch.Draw(sheet, pos, frame, Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);

        var name = Name.Length > 0 ? Name : "...";
        var nameSize = fonts.Body.MeasureString(name);
        Ui.Text(batch, fonts.Body, name, new Vector2(box.Center.X - nameSize.X / 2, box.Y + 12), Theme.Sol);
    }

    private static void Arrow(SpriteBatch batch, Fonts fonts, Rectangle rect, string glyph, bool selected) =>
        Centered(batch, fonts, rect, glyph, selected ? Theme.Luna : Theme.TextDim);

    private static void Centered(SpriteBatch batch, Fonts fonts, Rectangle rect, string text, Color color)
    {
        var size = fonts.Body.MeasureString(text);
        Ui.Text(batch, fonts.Body, text, new Vector2(rect.Center.X - size.X / 2, rect.Center.Y - size.Y / 2), color);
    }

    /// <summary>Tall enough for the rows this art set needs, and never shorter than the preview.</summary>
    private Rectangle Layout(Point screen)
    {
        const int w = 720;
        var h = Math.Max(470, 90 + _rows.Count * (RowHeight + RowGap) + 80);
        return new Rectangle((screen.X - w) / 2, Math.Max(8, (screen.Y - h) / 2 - 10), w, h);
    }

    private static Rectangle RowRect(Rectangle layout, int row) =>
        new(layout.X + 316, layout.Y + 90 + row * (RowHeight + RowGap), layout.Width - 344, RowHeight);

    private static Rectangle LeftArrow(Rectangle row) => new(row.X + row.Width / 2 - 130, row.Y + 13, 28, 24);

    private static Rectangle RightArrow(Rectangle row) => new(row.X + row.Width / 2 + 102, row.Y + 13, 28, 24);

    private static Rectangle RandomButton(Rectangle layout) => new(layout.X + 316, layout.Bottom - 60, 150, 38);

    private static Rectangle EnterButton(Rectangle layout) => new(layout.X + 478, layout.Bottom - 60, layout.Width - 506, 38);
}

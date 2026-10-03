using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client.UI;

/// <summary>
/// Character creation: a name plus body, skin, hair and eyes, with a live walking preview
/// wearing the starter clothes.
/// </summary>
internal sealed class CreationScreen
{
    private const int RowHeight = 44;
    private const int NameRow = 0;

    private static readonly string[] Labels = ["Nome", "Corpo", "Pele", "Cabelo", "Cor do cabelo", "Olhos"];

    private readonly Sprites _sprites;
    private readonly Equipment _preview;
    private readonly StringBuilder _name;
    private readonly int[] _values = new int[Labels.Length];
    private readonly Random _rng = new();
    private int _row;
    private float _time;

    public CreationScreen(Sprites sprites, ItemCatalog items, string name)
    {
        _sprites = sprites;
        _preview = items.StarterEquipment();
        _name = new StringBuilder(name.Length > Constants.MaxNameLength ? name[..Constants.MaxNameLength] : name);
        Randomize();
    }

    public string Name => _name.ToString().Trim();

    public Appearance Look => new((byte)_values[1], (byte)_values[2], (byte)_values[3], (byte)_values[4], (byte)_values[5]);

    private static int Count(int row) => row switch
    {
        1 => CharacterOptions.Bodies.Length,
        2 => CharacterOptions.Skins.Length,
        3 => CharacterOptions.HairStyles.Length,
        4 => CharacterOptions.HairColors.Length,
        5 => CharacterOptions.EyeColors.Length,
        _ => 0,
    };

    private string ValueLabel(int row) => row switch
    {
        1 => CharacterOptions.Bodies[_values[1]].label,
        2 => CharacterOptions.Skins[_values[2]].label,
        3 => CharacterOptions.HairStyles[_values[3]].label,
        4 => CharacterOptions.HairColors[_values[4]].label,
        5 => CharacterOptions.EyeColors[_values[5]].label,
        _ => "",
    };

    private void Randomize()
    {
        var look = Appearance.Random(_rng);
        _values[1] = look.Body;
        _values[2] = look.Skin;
        _values[3] = look.Hair;
        _values[4] = look.HairColor;
        _values[5] = look.Eyes;
    }

    private void Step(int row, int delta)
    {
        var count = Count(row);
        if (count > 0) _values[row] = (_values[row] + delta + count) % count;
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

    /// <summary>Returns true when the player confirms the character.</summary>
    public bool Update(Input input, Point screen, float dt)
    {
        _time += dt;

        if (input.Pressed(Keys.Down) || input.Pressed(Keys.Tab)) _row = (_row + 1) % Labels.Length;
        if (input.Pressed(Keys.Up)) _row = (_row - 1 + Labels.Length) % Labels.Length;
        if (input.Pressed(Keys.Left)) Step(_row, -1);
        if (input.Pressed(Keys.Right)) Step(_row, 1);
        if (_row != NameRow && input.Pressed(Keys.R)) Randomize();

        var layout = Layout(screen);
        var mouse = input.Mouse.ToPoint();
        if (input.LeftPressed)
        {
            for (var row = 0; row < Labels.Length; row++)
            {
                var rect = RowRect(layout, row);
                if (!rect.Contains(mouse)) continue;
                _row = row;
                if (LeftArrow(rect).Contains(mouse)) Step(row, -1);
                if (RightArrow(rect).Contains(mouse)) Step(row, 1);
            }
            if (RandomButton(layout).Contains(mouse)) Randomize();
            if (EnterButton(layout).Contains(mouse)) return Name.Length > 0;
        }

        return input.Pressed(Keys.Enter) && Name.Length > 0;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts, Point screen)
    {
        var layout = Layout(screen);
        Ui.Panel(batch, pixel, layout);

        Ui.Text(batch, fonts.Title, Constants.GameName, new Vector2(layout.X + 28, layout.Y + 20), Theme.Luna);
        Ui.Text(batch, fonts.Body, "Crie seu personagem", new Vector2(layout.X + 28, layout.Y + 50), Theme.TextDim);

        DrawPreview(batch, pixel, fonts, new Rectangle(layout.X + 28, layout.Y + 90, 260, 300));

        for (var row = 0; row < Labels.Length; row++)
        {
            var rect = RowRect(layout, row);
            var selected = row == _row;
            batch.Draw(pixel, rect, selected ? Theme.PanelRaised : Theme.Background * 0.6f);
            Ui.Outline(batch, pixel, rect, selected ? Theme.Luna : Theme.Border);
            Ui.Text(batch, fonts.Small, Labels[row], new Vector2(rect.X + 12, rect.Y + 4), Theme.TextDim);

            if (row == NameRow)
            {
                var caret = selected && _time % 1 < 0.5f ? "|" : "";
                Ui.Text(batch, fonts.Body, $"{_name}{caret}", new Vector2(rect.X + 12, rect.Y + 20), Theme.Text);
                continue;
            }

            var value = ValueLabel(row);
            var size = fonts.Body.MeasureString(value);
            Ui.Text(batch, fonts.Body, value, new Vector2(rect.Center.X - size.X / 2, rect.Y + 20), Theme.Text);
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
        Centered(batch, fonts, enter, "Entrar no mundo (Enter)", ready ? Theme.Text : Theme.TextDim);

        const string help = "Setas escolhem e mudam  ·  clique nas setas  ·  o nome usa o teclado";
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

        const float scale = 4;
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

    private static Rectangle Layout(Point screen)
    {
        const int w = 720, h = 470;
        return new Rectangle((screen.X - w) / 2, (screen.Y - h) / 2 - 10, w, h);
    }

    private static Rectangle RowRect(Rectangle layout, int row) =>
        new(layout.X + 316, layout.Y + 90 + row * (RowHeight + 6), layout.Width - 344, RowHeight);

    private static Rectangle LeftArrow(Rectangle row) => new(row.X + row.Width / 2 - 130, row.Y + 16, 28, 26);

    private static Rectangle RightArrow(Rectangle row) => new(row.X + row.Width / 2 + 102, row.Y + 16, 28, 26);

    private static Rectangle RandomButton(Rectangle layout) => new(layout.X + 316, layout.Bottom - 60, 150, 38);

    private static Rectangle EnterButton(Rectangle layout) => new(layout.X + 478, layout.Bottom - 60, layout.Width - 506, 38);
}

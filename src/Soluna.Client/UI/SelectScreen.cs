using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client.UI;

internal enum SelectAction
{
    None,
    Play,
    Create,
    Delete,
    Logout,
}

/// <summary>The account's character slots: play one, create in an empty one, or delete (with a second click).</summary>
internal sealed class SelectScreen(Sprites sprites)
{
    private const int CardWidth = 220, CardHeight = 320, Gap = 24;

    private int _hover = -1;
    private int _confirmDelete = -1;
    private float _time;

    public CharacterSummary?[] Slots { get; set; } = new CharacterSummary?[Constants.MaxCharacters];

    /// <summary>The slot the last action applies to.</summary>
    public int Slot { get; private set; }

    public string Message { get; set; } = "";

    public SelectAction Update(Input input, Point screen, float dt)
    {
        _time += dt;
        if (input.Pressed(Keys.Escape)) return SelectAction.Logout;

        for (var i = 0; i < Slots.Length; i++)
        {
            if (!input.Pressed(Keys.D1 + i)) continue;
            Slot = i;
            return Slots[i] == null ? SelectAction.Create : SelectAction.Play;
        }

        var mouse = input.Mouse.ToPoint();
        _hover = -1;
        for (var i = 0; i < Slots.Length; i++)
        {
            var card = Card(screen, i);
            if (!card.Contains(mouse)) continue;
            _hover = i;
            if (!input.LeftPressed) continue;

            Slot = i;
            if (Slots[i] != null && DeleteRect(card).Contains(mouse))
            {
                if (_confirmDelete == i)
                {
                    _confirmDelete = -1;
                    return SelectAction.Delete;
                }
                _confirmDelete = i;
                return SelectAction.None;
            }
            _confirmDelete = -1;
            return Slots[i] == null ? SelectAction.Create : SelectAction.Play;
        }

        if (input.LeftPressed && LogoutRect(screen).Contains(mouse)) return SelectAction.Logout;
        return SelectAction.None;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts, Point screen)
    {
        var first = Card(screen, 0);
        var title = fonts.Title.MeasureString("Escolha seu personagem");
        Ui.Text(batch, fonts.Title, "Escolha seu personagem", new Vector2(screen.X / 2f - title.X / 2, first.Y - 56), Theme.Luna);

        for (var i = 0; i < Slots.Length; i++)
        {
            var card = Card(screen, i);
            var hovered = i == _hover;
            batch.Draw(pixel, card, hovered ? Theme.PanelRaised : Theme.Panel);
            Ui.Outline(batch, pixel, card, hovered ? Theme.Luna : Theme.Border);

            if (Slots[i] is not { } slot)
            {
                Centered(batch, fonts.Title, "+", new Vector2(card.Center.X, card.Center.Y - 20), hovered ? Theme.Luna : Theme.TextDim);
                Centered(batch, fonts.Body, "Espaço livre", new Vector2(card.Center.X, card.Center.Y + 14), Theme.TextDim);
                Centered(batch, fonts.Small, $"Clique ou tecle {i + 1} para criar", new Vector2(card.Center.X, card.Bottom - 26), Theme.TextDim);
                continue;
            }

            // Stand still; walk on the spot while hovered.
            var sheet = sprites.SheetFor(slot.Look, slot.Equipment);
            var cycle = _time * 1000 / Constants.WalkTimeMs;
            var frame = SheetLayout.Frame(sheet, Direction.Down, hovered, cycle % 1, (int)cycle % 2 == 0);
            var scale = SheetLayout.IsLpc(sheet) ? 3f : 5f;
            var size = new Vector2(frame.Width, frame.Height) * scale;
            batch.Draw(sheet, new Vector2(card.Center.X - size.X / 2, card.Y + 30), frame, Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);

            Centered(batch, fonts.Title, slot.Name, new Vector2(card.Center.X, card.Y + 236), Theme.Sol);
            var options = CharacterOptions.Current;
            var body = options.Bodies.Length > 0 ? options.Bodies[slot.Look.Body % options.Bodies.Length].Label : "";
            Centered(batch, fonts.Small, $"{body}  ·  {slot.Look.RaceChoice.Label}", new Vector2(card.Center.X, card.Y + 262), Theme.TextDim);

            var delete = DeleteRect(card);
            var confirming = _confirmDelete == i;
            Centered(batch, fonts.Small, confirming ? "Clique de novo para apagar" : "Apagar",
                delete.Center.ToVector2(), confirming ? Theme.Danger : Theme.TextDim * (hovered ? 1f : 0.6f));
        }

        var logout = LogoutRect(screen);
        Centered(batch, fonts.Small, "Sair da conta (Esc)", logout.Center.ToVector2(), Theme.TextDim);

        if (Message.Length > 0)
            Centered(batch, fonts.Body, Message, new Vector2(screen.X / 2f, first.Bottom + 30), Theme.Danger);
    }

    private static void Centered(SpriteBatch batch, FontStashSharp.SpriteFontBase font, string text, Vector2 center, Color color)
    {
        var size = font.MeasureString(text);
        Ui.Text(batch, font, text, center - size / 2, color);
    }

    private static Rectangle Card(Point screen, int index)
    {
        var total = Constants.MaxCharacters * CardWidth + (Constants.MaxCharacters - 1) * Gap;
        var x = (screen.X - total) / 2 + index * (CardWidth + Gap);
        return new Rectangle(x, (screen.Y - CardHeight) / 2 + 10, CardWidth, CardHeight);
    }

    private static Rectangle DeleteRect(Rectangle card) => new(card.X + 20, card.Bottom - 34, card.Width - 40, 24);

    private static Rectangle LogoutRect(Point screen) => new(screen.X / 2 - 90, screen.Y - 50, 180, 28);
}

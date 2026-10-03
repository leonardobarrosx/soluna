using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client.UI;

/// <summary>Equipment and inventory (I). Clicking an item wears it, or takes it off if it is on.</summary>
internal sealed class InventoryPanel(ItemCatalog items, Sprites sprites)
{
    private const int Width = 320;
    private const int RowHeight = 26;
    private const int ListTop = 232;

    private static readonly (EquipSlot slot, string label)[] AllSlots =
    [
        (EquipSlot.Head, "Cabeça"), (EquipSlot.Torso, "Torso"), (EquipSlot.Legs, "Pernas"),
        (EquipSlot.Feet, "Pés"), (EquipSlot.Neck, "Pescoço"), (EquipSlot.Arms, "Braços"),
    ];

    /// <summary>Only the slots some item can go in, so the panel never lists a slot that is always empty.</summary>
    private IEnumerable<(EquipSlot slot, string label)> Slots => AllSlots.Where(s => items.All.Any(i => i.Slot == s.slot));

    private int _hover = -1;

    public bool Open { get; set; }

    public List<int> Inventory { get; } = [];

    public Rectangle PanelRect(Point screen) => new(screen.X - Width - 12, 52, Width, screen.Y - 64);

    /// <summary>Returns the item the player clicked, if any.</summary>
    public int? Update(Input input, Point screen)
    {
        _hover = -1;
        if (!Open) return null;

        var mouse = input.Mouse.ToPoint();
        for (var i = 0; i < Inventory.Count; i++)
        {
            if (!ItemRect(screen, i).Contains(mouse)) continue;
            _hover = i;
            if (input.LeftPressed) return Inventory[i];
        }
        return null;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts, Character me, Point screen)
    {
        if (!Open) return;

        var panel = PanelRect(screen);
        Ui.Panel(batch, pixel, panel);
        var x = panel.X + 14;
        Ui.Text(batch, fonts.Title, "Equipamento", new Vector2(x, panel.Y + 10), Theme.Luna);

        // The character as it looks right now, standing facing down.
        var sheet = sprites.SheetFor(me.Look, me.Equipment);
        var frame = SheetLayout.Frame(sheet, Direction.Down, false, 1, false);
        var preview = new Rectangle(x, panel.Y + 46, 110, 150);
        batch.Draw(pixel, preview, Theme.Background);
        Ui.Outline(batch, pixel, preview, Theme.Border);
        var scale = SheetLayout.IsLpc(sheet) ? 2f : 3f;
        var size = new Vector2(frame.Width, frame.Height) * scale;
        batch.Draw(sheet, new Vector2(preview.Center.X - size.X / 2, preview.Bottom - size.Y - 4), frame, Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);

        var y = panel.Y + 50;
        foreach (var (slot, label) in Slots)
        {
            var worn = items.Get(me.Equipment[slot]);
            Ui.Text(batch, fonts.Small, label, new Vector2(x + 124, y), Theme.TextDim);
            Ui.Text(batch, fonts.Small, worn?.Name ?? "-", new Vector2(x + 124, y + 13), worn != null ? Theme.Text : Theme.TextDim);
            y += 30;
        }

        Ui.Text(batch, fonts.Small, "Inventário  ·  clique para vestir ou tirar", new Vector2(x, panel.Y + ListTop - 22), Theme.TextDim);
        for (var i = 0; i < Inventory.Count; i++)
        {
            var rect = ItemRect(screen, i);
            if (rect.Bottom > panel.Bottom - 8) break;
            if (items.Get(Inventory[i]) is not { } item) continue;

            var worn = me.Equipment[item.Slot] == item.Id;
            if (i == _hover) batch.Draw(pixel, rect, Theme.PanelRaised);
            if (worn) batch.Draw(pixel, new Rectangle(rect.X, rect.Y, 3, rect.Height), Theme.Sol);
            Ui.Text(batch, fonts.Body, item.Name, new Vector2(rect.X + 10, rect.Y + 4), worn ? Theme.Sol : Theme.Text);
            var tag = AllSlots.First(s => s.slot == item.Slot).label;
            var tagSize = fonts.Small.MeasureString(tag);
            Ui.Text(batch, fonts.Small, tag, new Vector2(rect.Right - tagSize.X - 8, rect.Y + 6), Theme.TextDim);
        }
    }

    private Rectangle ItemRect(Point screen, int index)
    {
        var panel = PanelRect(screen);
        return new Rectangle(panel.X + 8, panel.Y + ListTop + index * RowHeight, panel.Width - 16, RowHeight - 2);
    }
}

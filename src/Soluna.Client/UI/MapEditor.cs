using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client.UI;

/// <summary>
/// In-game map editor in the spirit of Crystalshire's: pick a tile from the palette,
/// paint it on a layer, mark blocked tiles, save to the server.
/// </summary>
internal sealed class MapEditor(Textures textures, MapRenderer renderer)
{
    private const int S = Constants.TileSize;
    private const int PaletteWidth = 8 * S;
    private const int PanelWidth = PaletteWidth + 24;
    private const int HeaderHeight = 92;
    private const int FooterHeight = 70;

    private int _tileset;
    private int _scroll;
    private Point? _hover;

    public bool Active { get; private set; }
    public MapLayer Layer { get; private set; } = MapLayer.Ground;
    public bool AttributeMode { get; private set; }
    public int Selected { get; private set; } = TileRef.Make(0, 0);
    public bool Dirty { get; set; }

    public void Toggle(MapData map)
    {
        Active = !Active;
        if (!Active) return;

        // Offer every PNG the user dropped into assets/tilesets alongside the ones the map already uses.
        foreach (var name in Textures.AvailableTilesets())
        {
            if (!map.Tilesets.Contains(name)) map.Tilesets.Add(name);
        }
        _tileset = Math.Clamp(_tileset, 0, map.Tilesets.Count - 1);
    }

    public Rectangle PanelRect(Point screen) => new(screen.X - PanelWidth - 12, 52, PanelWidth, screen.Y - 64);

    /// <summary>Handles editor input. Returns true when the save shortcut was pressed.</summary>
    public bool Update(Input input, MapData map, Camera camera, Point screen)
    {
        if (!Active) return false;

        for (var i = 0; i < MapData.LayerCount; i++)
        {
            if (input.Pressed(Keys.D1 + i))
            {
                Layer = (MapLayer)i;
                AttributeMode = false;
            }
        }
        if (input.Pressed(Keys.B)) AttributeMode = !AttributeMode;
        if (input.Pressed(Keys.Tab) && map.Tilesets.Count > 0)
        {
            _tileset = (_tileset + 1) % map.Tilesets.Count;
            _scroll = 0;
        }

        var panel = PanelRect(screen);
        var palette = PaletteRect(panel);
        var mouse = input.Mouse.ToPoint();
        _hover = null;

        if (panel.Contains(mouse))
        {
            var texture = textures.Tileset(map.Tilesets[_tileset]);
            var maxScroll = Math.Max(0, texture.Height - palette.Height);
            _scroll = Math.Clamp(_scroll - input.Wheel * S, 0, maxScroll);

            if (input.LeftPressed && palette.Contains(mouse))
            {
                var col = (mouse.X - palette.X) / S;
                var row = (mouse.Y - palette.Y + _scroll) / S;
                var columns = Math.Max(1, texture.Width / S);
                if (col < columns && row < texture.Height / S)
                {
                    Selected = TileRef.Make(_tileset, row * columns + col);
                    AttributeMode = false;
                }
            }
        }
        else
        {
            var world = camera.ScreenToWorld(input.Mouse);
            var tx = (int)MathF.Floor(world.X / S);
            var ty = (int)MathF.Floor(world.Y / S);
            if (map.InBounds(tx, ty))
            {
                _hover = new Point(tx, ty);
                if (input.LeftDown) Paint(map, tx, ty, erase: false);
                else if (input.RightDown) Paint(map, tx, ty, erase: true);
            }
        }

        return input.Ctrl && input.Pressed(Keys.S);
    }

    private void Paint(MapData map, int x, int y, bool erase)
    {
        if (AttributeMode)
        {
            var value = erase ? TileAttribute.None : TileAttribute.Blocked;
            if (map.GetAttribute(x, y) == value) return;
            map.SetAttribute(x, y, value);
        }
        else
        {
            var value = erase ? TileRef.Empty : Selected;
            if (map.GetTile(Layer, x, y) == value) return;
            map.SetTile(Layer, x, y, value);
        }
        Dirty = true;
    }

    /// <summary>Grid, blocked tiles and the hover preview, drawn in world space.</summary>
    public void DrawWorld(SpriteBatch batch, MapData map, Camera camera)
    {
        if (!Active) return;

        var pixel = textures.Pixel;
        var (x0, y0, x1, y1) = MapRenderer.VisibleTiles(map, camera);
        var grid = Color.White * 0.07f;
        var line = 1 / camera.Zoom;

        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var pos = new Vector2(x * S, y * S);
            batch.Draw(pixel, pos, null, grid, 0, Vector2.Zero, new Vector2(S, line), SpriteEffects.None, 0);
            batch.Draw(pixel, pos, null, grid, 0, Vector2.Zero, new Vector2(line, S), SpriteEffects.None, 0);
            if (map.GetAttribute(x, y) == TileAttribute.Blocked && (AttributeMode || Layer == MapLayer.Ground))
                batch.Draw(pixel, new Rectangle(x * S, y * S, S, S), Theme.Danger * (AttributeMode ? 0.35f : 0.15f));
        }

        if (_hover is not { } h) return;
        var rect = new Rectangle(h.X * S, h.Y * S, S, S);
        if (!AttributeMode) renderer.DrawTile(batch, map, Selected, rect.Location.ToVector2(), Color.White * 0.6f);
        var outline = AttributeMode ? Theme.Danger : Theme.Luna;
        batch.Draw(pixel, new Vector2(rect.X, rect.Y), null, outline, 0, Vector2.Zero, new Vector2(S, line * 2), SpriteEffects.None, 0);
        batch.Draw(pixel, new Vector2(rect.X, rect.Bottom - line * 2), null, outline, 0, Vector2.Zero, new Vector2(S, line * 2), SpriteEffects.None, 0);
        batch.Draw(pixel, new Vector2(rect.X, rect.Y), null, outline, 0, Vector2.Zero, new Vector2(line * 2, S), SpriteEffects.None, 0);
        batch.Draw(pixel, new Vector2(rect.Right - line * 2, rect.Y), null, outline, 0, Vector2.Zero, new Vector2(line * 2, S), SpriteEffects.None, 0);
    }

    /// <summary>The side panel with the tile palette, drawn in screen space.</summary>
    public void DrawPanel(SpriteBatch batch, Fonts fonts, MapData map, Point screen)
    {
        if (!Active) return;

        var pixel = textures.Pixel;
        var panel = PanelRect(screen);
        Ui.Panel(batch, pixel, panel);

        var x = panel.X + 12;
        var y = panel.Y + 10;
        Ui.Text(batch, fonts.Title, "Editor de mapa", new Vector2(x, y), Theme.Luna);
        y += 30;
        var mode = AttributeMode ? "Atributo: Bloqueado" : $"Camada: {Layer}";
        Ui.Text(batch, fonts.Body, mode, new Vector2(x, y), AttributeMode ? Theme.Danger : Theme.Sol);
        y += 20;
        var setName = map.Tilesets.Count > 0 ? map.Tilesets[_tileset] : "-";
        Ui.Text(batch, fonts.Small, $"Tileset {_tileset + 1}/{map.Tilesets.Count}: {setName}", new Vector2(x, y), Theme.TextDim);

        var palette = PaletteRect(panel);
        batch.Draw(pixel, palette, Theme.Background);
        if (map.Tilesets.Count > 0)
        {
            var texture = textures.Tileset(map.Tilesets[_tileset]);
            var width = Math.Min(texture.Width, palette.Width);
            var height = Math.Min(texture.Height - _scroll, palette.Height);
            if (height > 0)
                batch.Draw(texture, new Rectangle(palette.X, palette.Y, width, height), new Rectangle(0, _scroll, width, height), Color.White);

            if (!AttributeMode && TileRef.Tileset(Selected) == _tileset)
            {
                var columns = Math.Max(1, texture.Width / S);
                var index = TileRef.Index(Selected);
                var sel = new Rectangle(palette.X + index % columns * S, palette.Y + index / columns * S - _scroll, S, S);
                if (palette.Contains(sel.Center)) Ui.Outline(batch, pixel, sel, Theme.Sol, 2);
            }
        }
        Ui.Outline(batch, pixel, palette, Theme.Border);

        var help = Dirty ? "Alterações não salvas · Ctrl+S salva" : "Mapa salvo";
        var fy = panel.Bottom - FooterHeight + 8;
        Ui.Text(batch, fonts.Small, help, new Vector2(x, fy), Dirty ? Theme.Sol : Theme.System);
        Ui.Text(batch, fonts.Small, "1-5 camada · B bloqueio · Tab tileset", new Vector2(x, fy + 18), Theme.TextDim);
        Ui.Text(batch, fonts.Small, "Esq pinta · Dir apaga · Roda rola", new Vector2(x, fy + 36), Theme.TextDim);
    }

    private static Rectangle PaletteRect(Rectangle panel) =>
        new(panel.X + 12, panel.Y + HeaderHeight, PaletteWidth, panel.Height - HeaderHeight - FooterHeight);
}

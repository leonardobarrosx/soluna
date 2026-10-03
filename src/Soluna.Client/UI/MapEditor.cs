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
    private const int MinPaletteWidth = 8 * S, MaxPaletteWidth = 12 * S;
    private const int HeaderHeight = 92;
    private const int FooterHeight = 70;

    // Tilesets offered in the palette: the map's own plus everything in assets/tilesets.
    // A tileset joins the map's list only once something is painted with it.
    private readonly List<string> _palette = [];
    private int _tileset;
    private int _paletteWidth = MinPaletteWidth;
    private string _selectedSet = "";
    private int _selectedIndex;
    private int _scroll;
    private Point? _hover;

    public bool Active { get; private set; }
    public int Layer { get; private set; }
    /// <summary>The tile type being painted, or None while painting tiles. B cycles through them.</summary>
    public TileAttribute Painting { get; private set; }

    public bool AttributeMode => Painting != TileAttribute.None;

    /// <summary>Where painted warp tiles lead; set with /destino in the chat.</summary>
    public Warp? WarpTarget { get; set; }

    private static readonly TileAttribute[] Cycle =
        [TileAttribute.None, TileAttribute.Blocked, TileAttribute.Warp, TileAttribute.NpcAvoid, TileAttribute.Heal];

    private static string AttributeName(TileAttribute a) => a switch
    {
        TileAttribute.Blocked => "Bloqueado",
        TileAttribute.Warp => "Teleporte",
        TileAttribute.NpcAvoid => "NPC evita",
        TileAttribute.Heal => "Cura",
        _ => "",
    };

    private static Color AttributeColor(TileAttribute a) => a switch
    {
        TileAttribute.Blocked => Theme.Danger,
        TileAttribute.Warp => Theme.Luna,
        TileAttribute.NpcAvoid => Theme.Sol,
        TileAttribute.Heal => Theme.System,
        _ => Color.Transparent,
    };
    public bool Dirty { get; set; }

    public void Toggle(MapData map)
    {
        Active = !Active;
        if (!Active) return;

        _palette.Clear();
        _palette.AddRange(map.Tilesets);
        _palette.AddRange(Textures.AvailableTilesets().Where(name => !map.Tilesets.Contains(name)));
        _tileset = Math.Clamp(_tileset, 0, Math.Max(0, _palette.Count - 1));
        if (_selectedSet.Length == 0 && _palette.Count > 0) _selectedSet = _palette[0];
        FitPalette();
    }

    /// <summary>Widens the panel for wide tilesets, up to 12 tiles; anything wider is cut off.</summary>
    private void FitPalette()
    {
        var width = _palette.Count > 0 ? textures.Tileset(_palette[_tileset]).Width : MinPaletteWidth;
        _paletteWidth = Math.Clamp(width, MinPaletteWidth, MaxPaletteWidth);
    }

    public Rectangle PanelRect(Point screen) => new(screen.X - _paletteWidth - 36, 52, _paletteWidth + 24, screen.Y - 64);

    /// <summary>Handles editor input. Returns true when the save shortcut was pressed.</summary>
    public bool Update(Input input, MapData map, Camera camera, Point screen)
    {
        if (!Active) return false;

        for (var i = 0; i < Math.Min(9, map.Layers.Length); i++)
        {
            if (input.Pressed(Keys.D1 + i))
            {
                Layer = i;
                Painting = TileAttribute.None;
            }
        }
        Layer = Math.Clamp(Layer, 0, map.Layers.Length - 1);
        if (input.Pressed(Keys.B)) Painting = Cycle[(Array.IndexOf(Cycle, Painting) + 1) % Cycle.Length];
        if (input.Pressed(Keys.Tab) && _palette.Count > 0)
        {
            _tileset = (_tileset + (input.Down(Keys.LeftShift) ? _palette.Count - 1 : 1)) % _palette.Count;
            _scroll = 0;
            FitPalette();
        }

        var panel = PanelRect(screen);
        var palette = PaletteRect(panel);
        var mouse = input.Mouse.ToPoint();
        _hover = null;

        if (panel.Contains(mouse) && _palette.Count > 0)
        {
            var texture = textures.Tileset(_palette[_tileset]);
            var maxScroll = Math.Max(0, texture.Height - palette.Height);
            _scroll = Math.Clamp(_scroll - input.Wheel * S, 0, maxScroll);

            if (input.LeftPressed && palette.Contains(mouse))
            {
                var col = (mouse.X - palette.X) / S;
                var row = (mouse.Y - palette.Y + _scroll) / S;
                var columns = Math.Max(1, texture.Width / S);
                if (col < columns && row < texture.Height / S)
                {
                    _selectedSet = _palette[_tileset];
                    _selectedIndex = row * columns + col;
                    Painting = TileAttribute.None;
                }
            }
        }
        else if (!panel.Contains(mouse))
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
            if (erase)
            {
                if (map.GetAttribute(x, y) == TileAttribute.None) return;
                map.ClearAttribute(x, y);
            }
            else if (Painting == TileAttribute.Warp)
            {
                // A warp needs somewhere to go: /destino first.
                if (WarpTarget is not { } to || map.WarpAt(x, y) is { } w && w.Map == to.Map && w.ToX == to.ToX && w.ToY == to.ToY) return;
                map.SetWarp(x, y, to.Map, to.ToX, to.ToY);
            }
            else
            {
                if (map.GetAttribute(x, y) == Painting) return;
                map.ClearAttribute(x, y);
                map.SetAttribute(x, y, Painting);
            }
        }
        else
        {
            var value = erase ? TileRef.Empty : TileRef.Make(TilesetIndex(map, _selectedSet), _selectedIndex);
            if (map.GetTile(Layer, x, y) == value) return;
            map.SetTile(Layer, x, y, value);
        }
        Dirty = true;
    }

    /// <summary>The map's index for a tileset, adding it to the map the first time it is used.</summary>
    private static int TilesetIndex(MapData map, string name)
    {
        var index = map.Tilesets.IndexOf(name);
        if (index >= 0) return index;
        map.Tilesets.Add(name);
        return map.Tilesets.Count - 1;
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
            var attribute = map.GetAttribute(x, y);
            if (attribute != TileAttribute.None && (AttributeMode || Layer == 0))
                batch.Draw(pixel, new Rectangle(x * S, y * S, S, S), AttributeColor(attribute) * (AttributeMode ? 0.4f : 0.15f));
        }

        if (_hover is not { } h) return;
        var rect = new Rectangle(h.X * S, h.Y * S, S, S);
        if (!AttributeMode && _selectedSet.Length > 0)
            renderer.DrawTile(batch, textures.Tileset(_selectedSet), _selectedIndex, rect.Location.ToVector2(), Color.White * 0.6f);
        var outline = AttributeMode ? AttributeColor(Painting) : Theme.Luna;
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
        var above = Layer >= map.FringeFrom ? " (acima)" : "";
        var destination = Painting != TileAttribute.Warp ? ""
            : WarpTarget is { } t ? $" → mapa {t.Map} ({t.ToX}, {t.ToY})" : " (use /destino)";
        var mode = AttributeMode ? $"Atributo: {AttributeName(Painting)}{destination}" : $"Camada {Layer + 1}/{map.Layers.Length}: {map.LayerName(Layer)}{above}";
        Ui.Text(batch, fonts.Body, mode, new Vector2(x, y), AttributeMode ? AttributeColor(Painting) : Theme.Sol);
        y += 20;
        var setName = _palette.Count > 0 ? _palette[_tileset] : "-";
        Ui.Text(batch, fonts.Small, $"Tileset {_tileset + 1}/{_palette.Count}: {setName}", new Vector2(x, y), Theme.TextDim);

        var palette = PaletteRect(panel);
        batch.Draw(pixel, palette, Theme.Background);
        if (_palette.Count > 0)
        {
            var texture = textures.Tileset(_palette[_tileset]);
            var width = Math.Min(texture.Width, palette.Width);
            var height = Math.Min(texture.Height - _scroll, palette.Height);
            if (height > 0)
                batch.Draw(texture, new Rectangle(palette.X, palette.Y, width, height), new Rectangle(0, _scroll, width, height), Color.White);

            if (!AttributeMode && _selectedSet == _palette[_tileset])
            {
                var columns = Math.Max(1, texture.Width / S);
                var index = _selectedIndex;
                var sel = new Rectangle(palette.X + index % columns * S, palette.Y + index / columns * S - _scroll, S, S);
                if (palette.Contains(sel.Center)) Ui.Outline(batch, pixel, sel, Theme.Sol, 2);
            }
        }
        Ui.Outline(batch, pixel, palette, Theme.Border);

        var help = Dirty ? "Alterações não salvas · Ctrl+S salva" : "Mapa salvo";
        var fy = panel.Bottom - FooterHeight + 8;
        Ui.Text(batch, fonts.Small, help, new Vector2(x, fy), Dirty ? Theme.Sol : Theme.System);
        Ui.Text(batch, fonts.Small, "1-9 camada · B atributos · Tab/Shift+Tab tileset", new Vector2(x, fy + 18), Theme.TextDim);
        Ui.Text(batch, fonts.Small, "Esq pinta · Dir apaga · Roda rola", new Vector2(x, fy + 36), Theme.TextDim);
    }

    private Rectangle PaletteRect(Rectangle panel) =>
        new(panel.X + 12, panel.Y + HeaderHeight, _paletteWidth, panel.Height - HeaderHeight - FooterHeight);
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client.UI;

/// <summary>
/// The map editor (F1, admins), in the spirit of Crystalshire's, with six modes:
/// Tiles (paint a tile from the palette on a layer), Attributes (blocked, warp, NPC-avoid, heal),
/// NPCs (place and remove spawns by clicking), Scenery (buildings, trees and other large pieces, with
/// the tiles they block), Points (where the shrine, merchant, workbench, fishing spots and the like
/// are) and Map (name, PvP, luck, music, edge links, start point, going to and creating maps).
/// Everything ends up in the map, which Save sends to the server.
/// </summary>
internal sealed class MapEditor(Textures textures, MapRenderer renderer, Gui gui)
{
    private enum Mode { Tiles, Attributes, Npcs, Scenery, Points, Map }

    private const int S = Constants.TileSize;
    private const int MinPaletteWidth = 8 * S, MaxPaletteWidth = 12 * S;
    private const int PaletteTop = 150;

    private static readonly string[] ModeNames = ["Tiles", "Atributos", "NPCs", "Cenário", "Pontos", "Mapa"];
    private static readonly LandmarkKind[] PointKinds = Enum.GetValues<LandmarkKind>();
    private static readonly string[] PointNames =
        ["Santuário", "Mercador", "Quadro de objetivos", "Baú", "Entrada da masmorra", "Saída da masmorra", "Pesca",
         "Coleta", "Mineração", "Corte de madeira", "Extração", "Bancada", "Descida"];
    private static readonly TileAttribute[] Attributes = [TileAttribute.Blocked, TileAttribute.Warp, TileAttribute.NpcAvoid, TileAttribute.Heal];
    private static readonly string[] AttributeNames = ["Bloqueado", "Teleporte", "NPC evita", "Cura"];

    // Tilesets offered in the palette: the map's own plus everything in assets/tilesets.
    // A tileset joins the map's list only once something is painted with it.
    private readonly List<string> _palette = [];
    private Mode _mode = Mode.Tiles;
    private int _tileset;
    private int _paletteWidth = MinPaletteWidth;
    private string _selectedSet = "";
    private int _selectedIndex;
    private int _scroll;
    private Point? _hover;
    private int _attribute;
    private int _npc;
    private int _goTo;
    private string _newName = "Novo mapa";
    private int _newWidth = 40, _newHeight = 30;
    private List<(string name, MapProp prop)>? _scenery;
    private int _prop;
    private int _propWidth, _propHeight;
    private bool _propSolid = true, _propUnder;
    private int _point;
    private string _pointName = "";

    public bool Active { get; private set; }
    public int Layer { get; private set; }
    public bool Dirty { get; set; }

    /// <summary>Where painted warp tiles lead.</summary>
    public Warp? WarpTarget { get; set; }

    /// <summary>The game's maps, as the server listed them.</summary>
    public List<(int id, string name)> Maps { get; set; } = [];

    // Hooks into the game.
    public Action? SaveRequested { get; set; }
    public Action? MapsRequested { get; set; }
    public Action<int>? GoTo { get; set; }
    public Action<string, int, int>? CreateMap { get; set; }

    /// <summary>Switches mode by name ("tiles", "atributos", "npcs", "cenario", "pontos", "mapa"), for tests and shortcuts.</summary>
    public void SetMode(string name) => _mode = name switch
    {
        "atributos" => Mode.Attributes,
        "npcs" => Mode.Npcs,
        "cenario" => Mode.Scenery,
        "pontos" => Mode.Points,
        "mapa" => Mode.Map,
        _ => Mode.Tiles,
    };

    private bool PaintingAttributes => _mode == Mode.Attributes;
    private TileAttribute Painting => Attributes[_attribute];

    public void Toggle(MapData map)
    {
        Active = !Active;
        if (!Active) return;
        RefreshPalette(map);
        MapsRequested?.Invoke();
    }

    /// <summary>The map's tilesets first, then every other one in the game; again after a file is imported.</summary>
    public void RefreshPalette(MapData map)
    {
        var current = _palette.Count > 0 ? _palette[_tileset] : null;
        _palette.Clear();
        _palette.AddRange(map.Tilesets);
        _palette.AddRange(Textures.AvailableTilesets().Where(name => !map.Tilesets.Contains(name)));
        _tileset = current != null && _palette.IndexOf(current) is >= 0 and var at ? at : Math.Clamp(_tileset, 0, Math.Max(0, _palette.Count - 1));
        if (_selectedSet.Length == 0 && _palette.Count > 0) _selectedSet = _palette[0];
        FitPalette();
        _scenery = null;
    }

    /// <summary>
    /// Scenery on offer: the pieces named in assets/props/catalog.json (a crop of a bigger sheet, a size in
    /// tiles and the tiles its base blocks), then every other PNG under assets/props, whole, as imported.
    /// </summary>
    private List<(string name, MapProp prop)> Scenery()
    {
        if (_scenery != null) return _scenery;
        _scenery = [];
        var folder = Path.Combine(DataPaths.Assets, "props");
        var catalogPath = Path.Combine(folder, "catalog.json");
        try
        {
            if (File.Exists(catalogPath))
                foreach (var (name, prop) in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, MapProp>>(File.ReadAllText(catalogPath)) ?? [])
                    _scenery.Add((name, prop));
        }
        catch (System.Text.Json.JsonException) { }
        if (!Directory.Exists(folder)) return _scenery;
        foreach (var file in Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories).Order())
        {
            var sprite = Path.GetRelativePath(DataPaths.Assets, file).Replace('\\', '/');
            if (_scenery.Any(s => s.prop.Sprite == sprite) || textures.Sheet(sprite) is not { } texture) continue;
            var (w, h) = (Math.Max(1, (texture.Width + S / 2) / S), Math.Max(1, (texture.Height + S / 2) / S));
            _scenery.Add((Path.GetFileNameWithoutExtension(file), new MapProp { Sprite = sprite, Width = w, Height = h, Solid = [0, h - 1, w, 1] }));
        }
        return _scenery;
    }

    /// <summary>The piece chosen in the panel, sized and placed so its base sits on the tile under the mouse.</summary>
    private MapProp? PropAt(int tx, int ty)
    {
        var list = Scenery();
        if (list.Count == 0) return null;
        var template = list[Math.Clamp(_prop, 0, list.Count - 1)].prop;
        var (w, h) = (_propWidth > 0 ? _propWidth : template.Width, _propHeight > 0 ? _propHeight : template.Height);
        // The base keeps its place when the piece is resized: scale its offsets with the size.
        var solid = template.Solid is [var sx, var sy, var sw, var sh] && _propSolid
            ? new[] { sx * w / template.Width, sy * h / template.Height, Math.Max(1, sw * w / template.Width), Math.Max(1, sh * h / template.Height) }
            : [];
        return new MapProp
        {
            Sprite = template.Sprite, Source = template.Source, Width = w, Height = h,
            X = tx - w / 2, Y = ty - h + 1, Underfoot = _propUnder || template.Underfoot, Solid = solid,
        };
    }

    private static IEnumerable<Point> Footprint(MapProp prop) =>
        prop.Solid is [var sx, var sy, var sw, var sh]
            ? from y in Enumerable.Range(prop.Y + sy, sh) from x in Enumerable.Range(prop.X + sx, sw) select new Point(x, y)
            : [];

    private void PlaceProp(MapData map, int tx, int ty)
    {
        if (PropAt(tx, ty) is not { } prop) return;
        map.Props.Add(prop);
        foreach (var p in Footprint(prop).Where(p => map.InBounds(p.X, p.Y)))
        {
            if (map.GetAttribute(p.X, p.Y) != TileAttribute.None) continue;
            map.SetAttribute(p.X, p.Y, TileAttribute.Blocked);
        }
        Dirty = true;
    }

    /// <summary>Removes the frontmost piece covering the tile, and the blocked tiles its base made.</summary>
    private void RemoveProp(MapData map, int tx, int ty)
    {
        var prop = map.Props.LastOrDefault(p => tx >= p.X && tx < p.X + p.Width && ty >= p.Y && ty < p.Y + p.Height);
        if (prop == null) return;
        map.Props.Remove(prop);
        foreach (var p in Footprint(prop).Where(p => map.InBounds(p.X, p.Y)))
        {
            // Another piece's base may cover the same tile; leave it blocked then.
            if (map.GetAttribute(p.X, p.Y) == TileAttribute.Blocked && !map.Props.Any(o => Footprint(o).Contains(p)))
                map.ClearAttribute(p.X, p.Y);
        }
        Dirty = true;
    }

    private void PlacePoint(MapData map, int tx, int ty)
    {
        var kind = PointKinds[_point];
        var name = _pointName.Trim().Length > 0 ? _pointName.Trim() : PointNames[_point];
        if (map.Landmarks.Any(l => l.X == tx && l.Y == ty && l.Kind == kind && l.Name == name)) return;
        map.Landmarks.RemoveAll(l => l.X == tx && l.Y == ty);
        map.Landmarks.Add(new Landmark { Kind = kind, Name = name, X = tx, Y = ty });
        // Creatures keep off the spots players use, as the authored maps do.
        if (map.GetAttribute(tx, ty) == TileAttribute.None) map.SetAttribute(tx, ty, TileAttribute.NpcAvoid);
        Dirty = true;
    }

    private void RemovePoint(MapData map, int tx, int ty)
    {
        if (map.Landmarks.RemoveAll(l => l.X == tx && l.Y == ty) == 0) return;
        if (map.GetAttribute(tx, ty) == TileAttribute.NpcAvoid) map.ClearAttribute(tx, ty);
        Dirty = true;
    }

    /// <summary>Widens the panel for wide tilesets, up to 12 tiles; anything wider is cut off.</summary>
    private void FitPalette()
    {
        var width = _palette.Count > 0 ? textures.Tileset(_palette[_tileset]).Width : MinPaletteWidth;
        _paletteWidth = Math.Clamp(width, MinPaletteWidth, MaxPaletteWidth);
    }

    public Rectangle PanelRect(Point screen)
    {
        var width = Math.Max(_paletteWidth + 24, 340);
        return new Rectangle(screen.X - width - 12, 52, width, screen.Y - 64);
    }

    /// <summary>Clicks on the map and keyboard shortcuts. Returns true when Ctrl+S was pressed.</summary>
    public bool Update(Input input, MapData map, Camera camera, Point screen, NpcCatalog npcs)
    {
        if (!Active) return false;
        Layer = Math.Clamp(Layer, 0, map.Layers.Length - 1);

        if (!gui.Typing)
        {
            for (var i = 0; i < Math.Min(9, map.Layers.Length); i++)
            {
                if (!input.Pressed(Keys.D1 + i)) continue;
                Layer = i;
                _mode = Mode.Tiles;
            }
            if (input.Pressed(Keys.B))
            {
                if (_mode == Mode.Attributes) _attribute = (_attribute + 1) % Attributes.Length;
                _mode = Mode.Attributes;
            }
            if (input.Pressed(Keys.Tab) && _palette.Count > 0) StepTileset(input.Down(Keys.LeftShift) ? -1 : 1);
        }

        var panel = PanelRect(screen);
        var palette = PaletteRect(panel);
        var mouse = input.Mouse.ToPoint();
        _hover = null;

        if (panel.Contains(mouse))
        {
            // The palette is the one part of the panel the widget kit does not draw.
            if (_mode != Mode.Tiles || _palette.Count == 0 || !palette.Contains(mouse)) return input.Ctrl && input.Pressed(Keys.S);
            var texture = textures.Tileset(_palette[_tileset]);
            var columns = Math.Max(1, palette.Width / S);
            var count = Math.Max(1, texture.Width / S) * Math.Max(1, texture.Height / S);
            var rows = (count + columns - 1) / columns;
            _scroll = Math.Clamp(_scroll - input.Wheel * S, 0, Math.Max(0, rows * S - palette.Height));
            if (input.LeftPressed)
            {
                var col = (mouse.X - palette.X) / S;
                var row = (mouse.Y - palette.Y + _scroll) / S;
                if (col < columns && row * columns + col < count)
                {
                    _selectedSet = _palette[_tileset];
                    _selectedIndex = row * columns + col;
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
                if (_mode == Mode.Npcs)
                {
                    if (input.LeftPressed) PlaceNpc(map, npcs, tx, ty);
                    else if (input.RightDown) RemoveNpc(map, tx, ty);
                }
                else if (_mode == Mode.Scenery)
                {
                    if (input.LeftPressed) PlaceProp(map, tx, ty);
                    else if (input.RightPressed) RemoveProp(map, tx, ty);
                }
                else if (_mode == Mode.Points)
                {
                    if (input.LeftPressed) PlacePoint(map, tx, ty);
                    else if (input.RightPressed) RemovePoint(map, tx, ty);
                }
                else if (_mode != Mode.Map)
                {
                    if (input.LeftDown) Paint(map, tx, ty, erase: false);
                    else if (input.RightDown) Paint(map, tx, ty, erase: true);
                }
            }
        }

        return input.Ctrl && input.Pressed(Keys.S);
    }

    private void StepTileset(int delta)
    {
        _tileset = (_tileset + delta + _palette.Count) % _palette.Count;
        _scroll = 0;
        FitPalette();
    }

    private void Paint(MapData map, int x, int y, bool erase)
    {
        if (PaintingAttributes)
        {
            if (erase)
            {
                if (map.GetAttribute(x, y) == TileAttribute.None) return;
                map.ClearAttribute(x, y);
            }
            else if (Painting == TileAttribute.Warp)
            {
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

    private void PlaceNpc(MapData map, NpcCatalog npcs, int x, int y)
    {
        var def = npcs.All.ElementAtOrDefault(_npc);
        if (def == null || !map.IsWalkable(x, y)) return;
        map.Npcs.RemoveAll(s => s.X == x && s.Y == y);
        map.Npcs.Add(new NpcSpawn { NpcId = def.Id, X = x, Y = y });
        Dirty = true;
    }

    private void RemoveNpc(MapData map, int x, int y)
    {
        if (map.Npcs.RemoveAll(s => s.X == x && s.Y == y) > 0) Dirty = true;
    }

    /// <summary>The map's index for a tileset, adding it to the map the first time it is used.</summary>
    private static int TilesetIndex(MapData map, string name)
    {
        var index = map.Tilesets.IndexOf(name);
        if (index >= 0) return index;
        map.Tilesets.Add(name);
        return map.Tilesets.Count - 1;
    }

    // ---- Drawing on the map ----

    /// <summary>Grid, tile types, NPC spawns and the hover preview, drawn in world space.</summary>
    public void DrawWorld(SpriteBatch batch, MapData map, Camera camera, NpcCatalog npcs, Func<NpcDef, Texture2D> npcSheet)
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
            if (attribute != TileAttribute.None && (PaintingAttributes || _mode == Mode.Tiles && Layer == 0))
                batch.Draw(pixel, new Rectangle(x * S, y * S, S, S), AttributeColor(attribute) * (PaintingAttributes ? 0.4f : 0.15f));
        }

        // Spawns, as faded sprites, so you see who appears where.
        if (_mode == Mode.Npcs)
        {
            foreach (var spawn in map.Npcs)
            {
                if (npcs.Get(spawn.NpcId) is not { } def) continue;
                var sheet = npcSheet(def);
                var frame = SheetLayout.Frame(sheet, Direction.Down, false, 1, false);
                batch.Draw(sheet, new Vector2(spawn.X * S, spawn.Y * S) + SheetLayout.Offset(sheet), frame, Color.White * 0.55f);
                Outline(batch, new Rectangle(spawn.X * S, spawn.Y * S, S, S), def.Hostile ? Theme.Danger : Theme.System, line);
            }
        }

        // Points, as marked tiles with their name, so you see where everything is.
        if (_mode == Mode.Points)
        {
            foreach (var l in map.Landmarks)
            {
                var r = new Rectangle(l.X * S, l.Y * S, S, S);
                batch.Draw(pixel, r, Theme.Sol * 0.25f);
                Outline(batch, r, Theme.Sol, line * 2);
            }
        }

        // Scenery: every piece's base, and the piece about to be placed, see-through.
        if (_mode == Mode.Scenery)
        {
            foreach (var prop in map.Props)
            {
                Outline(batch, new Rectangle(prop.X * S, prop.Y * S, prop.Width * S, prop.Height * S), Theme.Luna * 0.5f, line);
                foreach (var p in Footprint(prop)) batch.Draw(pixel, new Rectangle(p.X * S, p.Y * S, S, S), Theme.Danger * 0.25f);
            }
            if (_hover is { } at && PropAt(at.X, at.Y) is { } ghost)
            {
                renderer.DrawProp(batch, ghost, tint: Color.White * 0.6f);
                foreach (var p in Footprint(ghost)) Outline(batch, new Rectangle(p.X * S, p.Y * S, S, S), Theme.Danger, line * 2);
            }
        }

        if (_hover is not { } h) return;
        var rect = new Rectangle(h.X * S, h.Y * S, S, S);
        if (_mode == Mode.Tiles && _selectedSet.Length > 0)
            renderer.DrawTile(batch, textures.Tileset(_selectedSet), _selectedIndex, rect.Location.ToVector2(), Color.White * 0.6f);
        Outline(batch, rect, _mode switch
        {
            Mode.Attributes => AttributeColor(Painting),
            Mode.Npcs or Mode.Points => Theme.Sol,
            _ => Theme.Luna,
        }, line * 2);
    }

    private void Outline(SpriteBatch batch, Rectangle r, Color color, float width)
    {
        var pixel = textures.Pixel;
        batch.Draw(pixel, new Vector2(r.X, r.Y), null, color, 0, Vector2.Zero, new Vector2(r.Width, width), SpriteEffects.None, 0);
        batch.Draw(pixel, new Vector2(r.X, r.Bottom - width), null, color, 0, Vector2.Zero, new Vector2(r.Width, width), SpriteEffects.None, 0);
        batch.Draw(pixel, new Vector2(r.X, r.Y), null, color, 0, Vector2.Zero, new Vector2(width, r.Height), SpriteEffects.None, 0);
        batch.Draw(pixel, new Vector2(r.Right - width, r.Y), null, color, 0, Vector2.Zero, new Vector2(width, r.Height), SpriteEffects.None, 0);
    }

    private static Color AttributeColor(TileAttribute a) => a switch
    {
        TileAttribute.Blocked => Theme.Danger,
        TileAttribute.Warp => Theme.Luna,
        TileAttribute.NpcAvoid => Theme.Sol,
        TileAttribute.Heal => Theme.System,
        _ => Color.Transparent,
    };

    // ---- The panel ----

    /// <summary>The side panel, drawn with the widget kit in screen space.</summary>
    public void DrawPanel(SpriteBatch batch, MapData map, Point screen, Character local, NpcCatalog npcs)
    {
        if (!Active) return;

        var panel = PanelRect(screen);
        gui.Panel(panel, Theme.Background);
        var x = panel.X + 12;
        var width = panel.Width - 24;
        gui.Title(new Vector2(x, panel.Y + 8), "Editor de mapa");
        gui.Label(new Vector2(x, panel.Y + 38), $"Mapa {map.Id} · {map.Name} · {map.Width}x{map.Height}", Theme.TextDim, small: true);

        // Two rows of three, so the names fit however narrow the panel.
        var tabWidth = (width - 8) / 3;
        for (var i = 0; i < ModeNames.Length; i++)
        {
            var tab = new Rectangle(x + i % 3 * (tabWidth + 4), panel.Y + 58 + i / 3 * 30, tabWidth, 26);
            if (gui.Button(tab, ModeNames[i], (int)_mode == i)) _mode = (Mode)i;
        }

        var top = panel.Y + 128;
        switch (_mode)
        {
            case Mode.Tiles: DrawTilesMode(batch, map, panel, x, width, top); break;
            case Mode.Attributes: DrawAttributesMode(map, local, x, width, top); break;
            case Mode.Npcs: DrawNpcsMode(map, npcs, panel, x, width, top); break;
            case Mode.Scenery: DrawSceneryMode(batch, map, panel, x, width, top); break;
            case Mode.Points: DrawPointsMode(map, panel, x, width, top); break;
            case Mode.Map: DrawMapMode(map, local, x, width, top); break;
        }

        var footer = panel.Bottom - 40;
        gui.Label(new Vector2(x, footer + 8), Dirty ? "Alterações não salvas" : "Mapa salvo", Dirty ? Theme.Sol : Theme.System, small: true);
        if (gui.Button(new Rectangle(panel.Right - 152, footer, 140, 30), "Salvar (Ctrl+S)", primary: true, enabled: Dirty)) SaveRequested?.Invoke();
    }

    private void DrawTilesMode(SpriteBatch batch, MapData map, Rectangle panel, int x, int width, int top)
    {
        var layerNames = Enumerable.Range(0, map.Layers.Length)
            .Select(i => $"{i + 1}. {map.LayerName(i)}{(i >= map.FringeFrom ? " (acima)" : "")}").ToArray();
        Layer = gui.Cycle(Gui.Field(x, top - 4, width), "Camada (teclas 1-9)", layerNames, Layer);
        if (_palette.Count > 0)
        {
            var names = _palette.Select(p => Path.GetFileNameWithoutExtension(p)).ToArray();
            var chosen = gui.Cycle(Gui.Field(x, top + 34, width), $"Tileset {_tileset + 1}/{_palette.Count} (Tab)", names, _tileset);
            if (chosen != _tileset) StepTileset(chosen - _tileset);
        }

        var palette = PaletteRect(panel);
        var pixel = textures.Pixel;
        batch.Draw(pixel, palette, Theme.Background);
        if (_palette.Count > 0)
        {
            var texture = textures.Tileset(_palette[_tileset]);
            var columns = Math.Max(1, palette.Width / S);
            var sourceColumns = Math.Max(1, texture.Width / S);
            var count = sourceColumns * Math.Max(1, texture.Height / S);
            // Wrap wide atlases to the panel so tiles beyond the original image's right edge remain selectable.
            for (var index = _scroll / S * columns; index < count; index++)
            {
                var destination = new Rectangle(palette.X + index % columns * S, palette.Y + index / columns * S - _scroll, S, S);
                if (destination.Top >= palette.Bottom) break;
                var clip = Rectangle.Intersect(destination, palette);
                if (clip.Width <= 0 || clip.Height <= 0) continue;
                var source = new Rectangle(index % sourceColumns * S + clip.X - destination.X,
                    index / sourceColumns * S + clip.Y - destination.Y, clip.Width, clip.Height);
                batch.Draw(texture, clip, source, Color.White);
            }
            if (_selectedSet == _palette[_tileset])
            {
                var sel = new Rectangle(palette.X + _selectedIndex % columns * S, palette.Y + _selectedIndex / columns * S - _scroll, S, S);
                if (palette.Contains(sel.Center)) Ui.Outline(batch, pixel, sel, Theme.Sol, 2);
            }
        }
        Ui.Outline(batch, pixel, palette, Theme.Border);
        gui.Label(new Vector2(x, palette.Bottom + 4), "Esquerdo pinta · direito apaga · roda rola", Theme.TextDim, small: true);
        gui.Label(new Vector2(x, palette.Bottom + 22), "Tileset novo: arraste o PNG para a janela", Theme.TextDim, small: true);
    }

    private void DrawAttributesMode(MapData map, Character local, int x, int width, int top)
    {
        _attribute = gui.Cycle(Gui.Field(x, top, width), "Tipo (tecla B)", AttributeNames, _attribute);
        gui.Label(new Vector2(x, top + 42), "Esquerdo marca · direito limpa", Theme.TextDim, small: true);
        if (Painting != TileAttribute.Warp) return;

        var y = top + 70;
        gui.Label(new Vector2(x, y), "Destino dos teleportes", Theme.Sol);
        y += 24;
        var target = WarpTarget ?? new Warp { Map = map.Id, ToX = local.TileX, ToY = local.TileY };
        target.Map = PickMap(Gui.Field(x, y, width), "Mapa", target.Map, Maps.Select(m => m.id), allowNone: false);
        y += 42;
        var half = (width - 8) / 2;
        target.ToX = gui.NumberField("warp-x", Gui.Field(x, y, half), "X", target.ToX, 0, 255);
        target.ToY = gui.NumberField("warp-y", Gui.Field(x + half + 8, y, half), "Y", target.ToY, 0, 255);
        WarpTarget = target;
        y += 42;
        if (gui.Button(new Rectangle(x, y, width, 30), "Destino = onde estou agora"))
            WarpTarget = new Warp { Map = map.Id, ToX = local.TileX, ToY = local.TileY };
        y += 38;
        gui.Label(new Vector2(x, y), "Dica: vá até o destino, marque, volte e pinte.", Theme.TextDim, small: true);
    }

    private void DrawNpcsMode(MapData map, NpcCatalog npcs, Rectangle panel, int x, int width, int top)
    {
        var all = npcs.All.ToList();
        var rows = all.Select(n => $"{n.Id} · {n.Name}{(n.Hostile ? "  ⚔" : "")}").ToList();
        _npc = gui.List("map-npcs", new Rectangle(x, top, width, panel.Bottom - 60 - top - 40), rows, Math.Clamp(_npc, 0, Math.Max(0, all.Count - 1)));
        gui.Label(new Vector2(x, panel.Bottom - 92), $"{map.Npcs.Count} NPCs neste mapa", Theme.Text, small: true);
        gui.Label(new Vector2(x, panel.Bottom - 74), "Esquerdo coloca o escolhido · direito remove", Theme.TextDim, small: true);
    }

    private void DrawSceneryMode(SpriteBatch batch, MapData map, Rectangle panel, int x, int width, int top)
    {
        var list = Scenery();
        var listHeight = Math.Max(120, panel.Bottom - top - 330);
        var picked = gui.List("map-props", new Rectangle(x, top, width, listHeight), list.Select(s => s.name).ToList(), Math.Clamp(_prop, 0, Math.Max(0, list.Count - 1)));
        if (picked != _prop) (_prop, _propWidth, _propHeight) = (picked, 0, 0);
        var y = top + listHeight + 8;
        if (list.Count == 0)
        {
            gui.Label(new Vector2(x, y), "Nenhuma peça: importe um PNG como cenário.", Theme.TextDim, small: true);
            return;
        }
        var template = list[Math.Clamp(_prop, 0, list.Count - 1)].prop;
        var preview = new Rectangle(x, y, width, 110);
        gui.Fill(preview, Theme.Panel);
        var scale = Math.Min((preview.Width - 12) / (float)(template.Width * S), (preview.Height - 12) / (float)(template.Height * S));
        var size = new Point((int)(template.Width * S * scale), (int)(template.Height * S * scale));
        renderer.DrawProp(batch, template, new Rectangle(preview.Center.X - size.X / 2, preview.Bottom - 6 - size.Y, size.X, size.Y));
        y += 118;
        var half = (width - 8) / 2;
        _propWidth = gui.NumberField("prop-w", Gui.Field(x, y, half), "Largura (tiles)", _propWidth > 0 ? _propWidth : template.Width, 1, 32);
        _propHeight = gui.NumberField("prop-h", Gui.Field(x + half + 8, y, half), "Altura (tiles)", _propHeight > 0 ? _propHeight : template.Height, 1, 32);
        y += 40;
        _propSolid = gui.Toggle(Gui.Field(x, y, half), "Base bloqueia", _propSolid);
        _propUnder = gui.Toggle(Gui.Field(x + half + 8, y, half), "Sob os personagens", _propUnder || template.Underfoot);
        y += 42;
        gui.Label(new Vector2(x, y), $"{map.Props.Count} peças · esquerdo coloca (base no cursor) · direito remove", Theme.TextDim, small: true);
        gui.Label(new Vector2(x, y + 18), "Peça nova: arraste o PNG para a janela", Theme.TextDim, small: true);
    }

    private void DrawPointsMode(MapData map, Rectangle panel, int x, int width, int top)
    {
        var y = top;
        var before = _point;
        _point = gui.Cycle(Gui.Field(x, y, width), "Tipo de ponto", PointNames, _point);
        if (before != _point) _pointName = "";
        y += 42;
        _pointName = gui.TextField("point-name", Gui.Field(x, y, width), "Nome que aparece ([E] ...)", _pointName.Length > 0 ? _pointName : PointNames[_point], 40);
        y += 46;
        gui.Label(new Vector2(x, y), "Esquerdo coloca · direito remove. O jogador\nusa o ponto com E a até 3 tiles de distância.", Theme.TextDim, small: true);
        y += 46;
        gui.Label(new Vector2(x, y), $"Pontos neste mapa ({map.Landmarks.Count})", Theme.Sol);
        y += 22;
        var rows = map.Landmarks.Select(l => $"{l.Name}  ·  {PointNames[Array.IndexOf(PointKinds, l.Kind)]}  ({l.X}, {l.Y})").ToList();
        gui.List("map-points", new Rectangle(x, y, width, Math.Max(80, panel.Bottom - 60 - y)), rows, -1);
    }

    private void DrawMapMode(MapData map, Character local, int x, int width, int top)
    {
        var y = top;
        var before = (map.Name, map.Moral, map.Music, map.Links.Up, map.Links.Down, map.Links.Left, map.Links.Right, map.SpawnX, map.SpawnY, map.Luck);
        map.Name = gui.TextField("map-name", Gui.Field(x, y, width), "Nome", map.Name);
        y += 40;
        var half = (width - 8) / 2;
        map.Moral = gui.Toggle(Gui.Field(x, y, half), "PvP (jogadores se atacam)", map.Moral == MapMoral.Pvp) ? MapMoral.Pvp : MapMoral.Safe;
        map.Music = gui.TextField("map-music", Gui.Field(x + half + 8, y, half), "Música (arquivo)", map.Music);
        y += 40;
        map.Luck = gui.NumberField("map-luck", Gui.Field(x, y, width), "Sorte do lugar (natureza)", map.Luck, -20, 20);
        y += 46;

        gui.Label(new Vector2(x, y), "Ligações nas bordas", Theme.Sol);
        y += 22;
        var others = Maps.Where(m => m.id != map.Id).Select(m => m.id).ToList();
        int Link(string label, int current, int lx, int ly) => PickMap(Gui.Field(lx, ly, half), label, current, others, allowNone: true);
        map.Links.Up = Link("Norte", map.Links.Up, x, y);
        map.Links.Down = Link("Sul", map.Links.Down, x + half + 8, y);
        y += 40;
        map.Links.Left = Link("Oeste", map.Links.Left, x, y);
        map.Links.Right = Link("Leste", map.Links.Right, x + half + 8, y);
        y += 46;

        var (sx, sy) = map.Spawn;
        gui.Label(new Vector2(x, y), $"Início dos jogadores: {sx}, {sy}", Theme.Text, small: true);
        if (gui.Button(new Rectangle(x + width - 150, y - 4, 150, 26), "Usar onde estou")) (map.SpawnX, map.SpawnY) = (local.TileX, local.TileY);
        y += 34;

        if (before != (map.Name, map.Moral, map.Music, map.Links.Up, map.Links.Down, map.Links.Left, map.Links.Right, map.SpawnX, map.SpawnY, map.Luck)) Dirty = true;

        gui.Label(new Vector2(x, y), "Ir para outro mapa", Theme.Sol);
        y += 22;
        if (Maps.Count > 0)
        {
            var names = Maps.Select(m => $"{m.id} · {m.name}").ToArray();
            _goTo = gui.Cycle(Gui.Field(x, y, width - 70), "Mapa", names, Math.Clamp(_goTo, 0, names.Length - 1));
            if (gui.Button(new Rectangle(x + width - 62, y + 2, 62, 30), "Ir", enabled: !Dirty)) GoTo?.Invoke(Maps[_goTo].id);
        }
        y += 46;

        gui.Label(new Vector2(x, y), "Novo mapa (piso: o tile onde você está)", Theme.Sol);
        y += 22;
        _newName = gui.TextField("new-map-name", Gui.Field(x, y, width), "Nome", _newName);
        y += 40;
        _newWidth = gui.NumberField("new-map-w", Gui.Field(x, y, half), "Largura", _newWidth, 10, 256, 5);
        _newHeight = gui.NumberField("new-map-h", Gui.Field(x + half + 8, y, half), "Altura", _newHeight, 10, 256, 5);
        y += 40;
        if (gui.Button(new Rectangle(x, y, width, 30), "Criar e ir para ele", enabled: !Dirty && _newName.Trim().Length > 0))
            CreateMap?.Invoke(_newName.Trim(), _newWidth, _newHeight);
        if (Dirty) gui.Label(new Vector2(x, y + 36), "Salve este mapa antes de sair dele.", Theme.Sol, small: true);
    }

    /// <summary>
    /// Picks a map id. The current value is always one of the options, even before the server's list
    /// arrives or if it is not on it, so drawing the field never changes it: only a click does.
    /// </summary>
    private int PickMap(Rectangle r, string label, int current, IEnumerable<int> ids, bool allowNone)
    {
        var options = (allowNone ? new[] { 0 } : []).Concat(ids).ToList();
        if (!options.Contains(current)) options.Add(current);
        string Name(int id) => id == 0 ? "Nenhuma" : Maps.FirstOrDefault(m => m.id == id) is { name: { } n } ? $"{id} · {n}" : $"{id}";
        var index = gui.Cycle(r, label, options.Select(Name).ToArray(), options.IndexOf(current));
        return options[index];
    }

    private Rectangle PaletteRect(Rectangle panel) =>
        new(panel.X + 12, panel.Y + PaletteTop, _paletteWidth, panel.Height - PaletteTop - 70);
}

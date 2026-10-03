using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client.UI;

/// <summary>
/// The map editor (F1, admins), in the spirit of Crystalshire's, with four modes:
/// Tiles (paint a tile from the palette on a layer), Attributes (blocked, warp, NPC-avoid, heal),
/// NPCs (place and remove spawns by clicking) and Map (name, PvP, music, edge links, start point,
/// going to and creating maps). Everything ends up in the map, which Save sends to the server.
/// </summary>
internal sealed class MapEditor(Textures textures, MapRenderer renderer, Gui gui)
{
    private enum Mode { Tiles, Attributes, Npcs, Map }

    private const int S = Constants.TileSize;
    private const int MinPaletteWidth = 8 * S, MaxPaletteWidth = 12 * S;
    private const int PaletteTop = 150;

    private static readonly string[] ModeNames = ["Tiles", "Atributos", "NPCs", "Mapa"];
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

    /// <summary>Switches mode by name ("tiles", "atributos", "npcs", "mapa"), for tests and shortcuts.</summary>
    public void SetMode(string name) => _mode = name switch
    {
        "atributos" => Mode.Attributes,
        "npcs" => Mode.Npcs,
        "mapa" => Mode.Map,
        _ => Mode.Tiles,
    };

    private bool PaintingAttributes => _mode == Mode.Attributes;
    private TileAttribute Painting => Attributes[_attribute];

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
        MapsRequested?.Invoke();
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
            _scroll = Math.Clamp(_scroll - input.Wheel * S, 0, Math.Max(0, texture.Height - palette.Height));
            if (input.LeftPressed)
            {
                var col = (mouse.X - palette.X) / S;
                var row = (mouse.Y - palette.Y + _scroll) / S;
                var columns = Math.Max(1, texture.Width / S);
                if (col < columns && row < texture.Height / S)
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

        if (_hover is not { } h) return;
        var rect = new Rectangle(h.X * S, h.Y * S, S, S);
        if (_mode == Mode.Tiles && _selectedSet.Length > 0)
            renderer.DrawTile(batch, textures.Tileset(_selectedSet), _selectedIndex, rect.Location.ToVector2(), Color.White * 0.6f);
        Outline(batch, rect, _mode switch
        {
            Mode.Attributes => AttributeColor(Painting),
            Mode.Npcs => Theme.Sol,
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

        var tabWidth = (width - 12) / 4;
        for (var i = 0; i < ModeNames.Length; i++)
        {
            if (gui.Button(new Rectangle(x + i * (tabWidth + 4), panel.Y + 60, tabWidth, 28), ModeNames[i], (int)_mode == i)) _mode = (Mode)i;
        }

        var top = panel.Y + 98;
        switch (_mode)
        {
            case Mode.Tiles: DrawTilesMode(batch, map, panel, x, width, top); break;
            case Mode.Attributes: DrawAttributesMode(map, local, x, width, top); break;
            case Mode.Npcs: DrawNpcsMode(map, npcs, panel, x, width, top); break;
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
            var w = Math.Min(texture.Width, palette.Width);
            var h = Math.Min(texture.Height - _scroll, palette.Height);
            if (h > 0) batch.Draw(texture, new Rectangle(palette.X, palette.Y, w, h), new Rectangle(0, _scroll, w, h), Color.White);
            if (_selectedSet == _palette[_tileset])
            {
                var columns = Math.Max(1, texture.Width / S);
                var sel = new Rectangle(palette.X + _selectedIndex % columns * S, palette.Y + _selectedIndex / columns * S - _scroll, S, S);
                if (palette.Contains(sel.Center)) Ui.Outline(batch, pixel, sel, Theme.Sol, 2);
            }
        }
        Ui.Outline(batch, pixel, palette, Theme.Border);
        gui.Label(new Vector2(x, palette.Bottom + 4), "Esquerdo pinta · direito apaga · roda rola", Theme.TextDim, small: true);
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

    private void DrawMapMode(MapData map, Character local, int x, int width, int top)
    {
        var y = top;
        var before = (map.Name, map.Moral, map.Music, map.Links.Up, map.Links.Down, map.Links.Left, map.Links.Right, map.SpawnX, map.SpawnY);
        map.Name = gui.TextField("map-name", Gui.Field(x, y, width), "Nome", map.Name);
        y += 40;
        var half = (width - 8) / 2;
        map.Moral = gui.Toggle(Gui.Field(x, y, half), "PvP (jogadores se atacam)", map.Moral == MapMoral.Pvp) ? MapMoral.Pvp : MapMoral.Safe;
        map.Music = gui.TextField("map-music", Gui.Field(x + half + 8, y, half), "Música (arquivo)", map.Music);
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

        if (before != (map.Name, map.Moral, map.Music, map.Links.Up, map.Links.Down, map.Links.Left, map.Links.Right, map.SpawnX, map.SpawnY)) Dirty = true;

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

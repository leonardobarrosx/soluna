using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client.UI;

/// <summary>
/// The game editor (F2, admins): items and NPCs edited in place, saved to the server, which writes the
/// game's files and pushes the change to everyone online. Works on copies until Save, so Discard is safe.
/// </summary>
internal sealed class GameEditor(Gui gui, Textures textures, Sprites sprites, ItemCatalog itemCatalog, NpcCatalog npcCatalog)
{
    private enum Tab { Items, Npcs }

    private static readonly EquipSlot[] Slots = Enum.GetValues<EquipSlot>();
    private static readonly string[] SlotNames = ["Cabeça", "Torso", "Pernas", "Pés", "Pescoço", "Braços", "Arma", "Costas", "Rosto"];
    private static readonly string[] BehaviourNames = ["Amigável", "Passivo", "Agressivo"];

    private Tab _tab = Tab.Items;
    private List<ItemDef> _items = [];
    private List<NpcDef> _npcs = [];
    private int _item, _npc;
    private bool _itemsDirty, _npcsDirty;
    private string _itemFilter = "", _npcFilter = "", _partFilter = "", _spriteFilter = "";
    private string _status = "";
    private float _time;
    private List<string>? _parts;
    private List<string>? _sprites;

    public bool Open { get; private set; }

    /// <summary>Set by the game: sends one kind of content to the server.</summary>
    public Action<ContentKind, string>? Save { get; set; }

    public void Toggle()
    {
        Open = !Open;
        if (!Open) return;
        if (!_itemsDirty) _items = itemCatalog.All.Select(i => i.Clone()).OrderBy(i => i.Id).ToList();
        if (!_npcsDirty) _npcs = npcCatalog.All.Select(n => n.Clone()).ToList();
        _parts = null;
        _sprites = null;
    }

    /// <summary>Opens straight on a tab ("items" or "npcs"), for tests and shortcuts.</summary>
    public void OpenOn(string tab)
    {
        if (!Open) Toggle();
        _tab = tab == "npcs" ? Tab.Npcs : Tab.Items;
    }

    /// <summary>The server sent a new catalog: take it, unless there are edits here not saved yet.</summary>
    public void CatalogChanged(ContentKind kind)
    {
        if (kind == ContentKind.Items && !_itemsDirty) _items = itemCatalog.All.Select(i => i.Clone()).OrderBy(i => i.Id).ToList();
        if (kind == ContentKind.Npcs && !_npcsDirty) _npcs = npcCatalog.All.Select(n => n.Clone()).ToList();
    }

    public void Message(string text) => _status = text;

    public void Draw(Point screen, Appearance previewLook, float dt)
    {
        if (!Open) return;
        _time += dt;

        var panel = new Rectangle(24, 24, screen.X - 48, screen.Y - 48);
        gui.Panel(panel, Theme.Background);
        gui.Title(new Vector2(panel.X + 20, panel.Y + 14), "Editor do jogo");
        gui.Label(new Vector2(panel.Right - 150, panel.Y + 20), "F2 ou Esc fecha", Theme.TextDim, small: true);

        if (gui.Button(new Rectangle(panel.X + 220, panel.Y + 14, 110, 30), "Itens" + (_itemsDirty ? " *" : ""), _tab == Tab.Items)) _tab = Tab.Items;
        if (gui.Button(new Rectangle(panel.X + 338, panel.Y + 14, 110, 30), "NPCs" + (_npcsDirty ? " *" : ""), _tab == Tab.Npcs)) _tab = Tab.Npcs;

        var body = new Rectangle(panel.X + 16, panel.Y + 60, panel.Width - 32, panel.Height - 112);
        if (_tab == Tab.Items) DrawItems(body, previewLook);
        else DrawNpcs(body);

        var dirty = _tab == Tab.Items ? _itemsDirty : _npcsDirty;
        var footer = panel.Bottom - 44;
        gui.Label(new Vector2(panel.X + 20, footer + 8), _status, Theme.TextDim, small: true);
        if (gui.Button(new Rectangle(panel.Right - 330, footer, 140, 32), "Descartar", enabled: dirty))
        {
            if (_tab == Tab.Items) _itemsDirty = false; else _npcsDirty = false;
            CatalogChanged(_tab == Tab.Items ? ContentKind.Items : ContentKind.Npcs);
            _status = "Alterações descartadas.";
        }
        if (gui.Button(new Rectangle(panel.Right - 180, footer, 160, 32), "Salvar no servidor", primary: true, enabled: dirty))
        {
            if (_tab == Tab.Items)
            {
                Save?.Invoke(ContentKind.Items, ItemCatalog.ToJson(_items));
                _itemsDirty = false;
            }
            else
            {
                Save?.Invoke(ContentKind.Npcs, NpcCatalog.ToJson(_npcs));
                _npcsDirty = false;
            }
            _status = "Enviado ao servidor...";
        }
    }

    // ---- Items ----

    private void DrawItems(Rectangle body, Appearance previewLook)
    {
        var listColumn = new Rectangle(body.X, body.Y, 270, body.Height);
        var visible = _items.Where(i => Matches(i.Name, _itemFilter) || i.Id.ToString() == _itemFilter).ToList();
        _itemFilter = gui.TextField("item-filter", Gui.Field(listColumn.X, listColumn.Y, listColumn.Width), "Buscar", _itemFilter);

        var rows = visible.Select(i => $"{i.Id} · {i.Name}  ({SlotNames[(int)i.Slot]})").ToList();
        var selectedRow = visible.FindIndex(i => i.Id == (_items.ElementAtOrDefault(_item)?.Id ?? -1));
        var picked = gui.List("items", new Rectangle(listColumn.X, listColumn.Y + 42, listColumn.Width, listColumn.Height - 90), rows, selectedRow);
        if (picked >= 0 && picked < visible.Count) _item = _items.IndexOf(visible[picked]);

        var buttons = listColumn.Bottom - 40;
        if (gui.Button(new Rectangle(listColumn.X, buttons, 86, 32), "Novo"))
        {
            _items.Add(new ItemDef { Id = NextId(_items.Select(i => i.Id)), Name = "Novo item", Slot = EquipSlot.Torso });
            _item = _items.Count - 1;
            _itemsDirty = true;
        }
        if (gui.Button(new Rectangle(listColumn.X + 92, buttons, 86, 32), "Duplicar", enabled: _items.Count > 0))
        {
            var copy = _items[_item].Clone();
            copy.Id = NextId(_items.Select(i => i.Id));
            copy.Name += " (cópia)";
            _items.Add(copy);
            _item = _items.Count - 1;
            _itemsDirty = true;
        }
        if (gui.Button(new Rectangle(listColumn.X + 184, buttons, 86, 32), "Excluir", enabled: _items.Count > 0))
        {
            _items.RemoveAt(_item);
            _item = Math.Clamp(_item, 0, Math.Max(0, _items.Count - 1));
            _itemsDirty = true;
        }

        if (_items.Count == 0) return;
        _item = Math.Clamp(_item, 0, _items.Count - 1);
        var item = _items[_item];
        var before = ContentJson.Array([item]);

        var x = listColumn.Right + 20;
        var width = 320;
        var y = body.Y;
        gui.Label(new Vector2(x, y), $"Item nº {item.Id}", Theme.Sol);
        y += 26;
        item.Name = gui.TextField($"item-name-{item.Id}", Gui.Field(x, y, width), "Nome", item.Name);
        y += 42;
        item.Slot = Slots[gui.Cycle(Gui.Field(x, y, width), "Onde é usado", SlotNames, Array.IndexOf(Slots, item.Slot))];
        y += 42;
        item.Attack = gui.NumberField($"item-atk-{item.Id}", Gui.Field(x, y, width), "Ataque", item.Attack, 0, 999);
        y += 42;
        item.Defense = gui.NumberField($"item-def-{item.Id}", Gui.Field(x, y, width), "Defesa", item.Defense, 0, 999);
        y += 42;
        item.Price = gui.NumberField($"item-price-{item.Id}", Gui.Field(x, y, width), "Preço", item.Price, 0, 9_999_999, 10);
        y += 42;
        var color = gui.TextField($"item-color-{item.Id}", Gui.Field(x, y, width - 44), "Cor (#rrggbb, vazio mantém a original)", item.Colors.FirstOrDefault() ?? "", 7);
        item.Colors = color.Length > 0 ? [color] : [];
        if (TryColor(color) is { } swatch) gui.Fill(new Rectangle(x + width - 36, y + 4, 34, 26), swatch);
        y += 42;
        item.Starter = gui.Toggle(Gui.Field(x, y, width), "Personagens novos começam com ele", item.Starter);

        if (ContentJson.Array([item]) != before) _itemsDirty = true;

        // The look: pick a part of the art set, previewed on your own character.
        var right = new Rectangle(x + width + 20, body.Y, body.Right - (x + width + 20), body.Height);
        _parts ??= Parts();
        _partFilter = gui.TextField("part-filter", Gui.Field(right.X, right.Y, right.Width - 190), "Visual (parte do personagem)", _partFilter);
        var parts = _parts.Where(p => Matches(p, _partFilter)).ToList();
        var partRow = parts.IndexOf(item.Sheet);
        var newRow = gui.List("parts", new Rectangle(right.X, right.Y + 42, right.Width - 190, right.Height - 42), parts, partRow);
        if (newRow != partRow && newRow >= 0 && newRow < parts.Count)
        {
            item.Sheet = parts[newRow];
            _itemsDirty = true;
        }

        var previewBox = new Rectangle(right.Right - 176, right.Y, 176, 220);
        var empty = new Equipment();
        var sheet = sprites.Preview(previewLook, empty, item.Sheet, item.Colors.FirstOrDefault());
        DrawWalking(previewBox, sheet, $"{item.Sheet}");
    }

    // ---- NPCs ----

    private void DrawNpcs(Rectangle body)
    {
        var listColumn = new Rectangle(body.X, body.Y, 270, body.Height);
        _npcFilter = gui.TextField("npc-filter", Gui.Field(listColumn.X, listColumn.Y, listColumn.Width), "Buscar", _npcFilter);
        var visible = _npcs.Where(n => Matches(n.Name, _npcFilter) || n.Id.ToString() == _npcFilter).ToList();
        var rows = visible.Select(n => $"{n.Id} · {n.Name}  ({BehaviourNames[(int)n.Behaviour]})").ToList();
        var selectedRow = visible.FindIndex(n => n.Id == (_npcs.ElementAtOrDefault(_npc)?.Id ?? -1));
        var picked = gui.List("npcs", new Rectangle(listColumn.X, listColumn.Y + 42, listColumn.Width, listColumn.Height - 90), rows, selectedRow);
        if (picked >= 0 && picked < visible.Count) _npc = _npcs.IndexOf(visible[picked]);

        var buttons = listColumn.Bottom - 40;
        if (gui.Button(new Rectangle(listColumn.X, buttons, 86, 32), "Novo"))
        {
            _npcs.Add(new NpcDef { Id = NextId(_npcs.Select(n => n.Id)), Name = "Novo NPC", Sprite = _sprites?.FirstOrDefault() ?? "" });
            _npc = _npcs.Count - 1;
            _npcsDirty = true;
        }
        if (gui.Button(new Rectangle(listColumn.X + 92, buttons, 86, 32), "Duplicar", enabled: _npcs.Count > 0))
        {
            var copy = _npcs[_npc].Clone();
            copy.Id = NextId(_npcs.Select(n => n.Id));
            copy.Name += " (cópia)";
            _npcs.Add(copy);
            _npc = _npcs.Count - 1;
            _npcsDirty = true;
        }
        if (gui.Button(new Rectangle(listColumn.X + 184, buttons, 86, 32), "Excluir", enabled: _npcs.Count > 0))
        {
            _npcs.RemoveAt(_npc);
            _npc = Math.Clamp(_npc, 0, Math.Max(0, _npcs.Count - 1));
            _npcsDirty = true;
        }

        if (_npcs.Count == 0) return;
        _npc = Math.Clamp(_npc, 0, _npcs.Count - 1);
        var npc = _npcs[_npc];
        var before = ContentJson.Array([npc]);

        var x = listColumn.Right + 20;
        var width = 320;
        var y = body.Y;
        gui.Label(new Vector2(x, y), $"NPC nº {npc.Id}", Theme.Sol);
        y += 26;
        npc.Name = gui.TextField($"npc-name-{npc.Id}", Gui.Field(x, y, width), "Nome", npc.Name);
        y += 42;
        npc.Behaviour = (NpcBehaviour)gui.Cycle(Gui.Field(x, y, width), "Comportamento", BehaviourNames, (int)npc.Behaviour);
        y += 42;
        var half = (width - 8) / 2;
        npc.Hp = gui.NumberField($"npc-hp-{npc.Id}", Gui.Field(x, y, half), "Vida", npc.Hp, 1, 99999, 5);
        npc.Exp = gui.NumberField($"npc-exp-{npc.Id}", Gui.Field(x + half + 8, y, half), "Experiência", npc.Exp, 0, 99999, 5);
        y += 42;
        npc.Attack = gui.NumberField($"npc-atk-{npc.Id}", Gui.Field(x, y, half), "Ataque", npc.Attack, 0, 9999);
        npc.Defense = gui.NumberField($"npc-def-{npc.Id}", Gui.Field(x + half + 8, y, half), "Defesa", npc.Defense, 0, 9999);
        y += 42;
        npc.Range = gui.NumberField($"npc-range-{npc.Id}", Gui.Field(x, y, half), "Alcance (quadros)", npc.Range, 0, 30);
        npc.RespawnSeconds = gui.NumberField($"npc-respawn-{npc.Id}", Gui.Field(x + half + 8, y, half), "Renasce em (s)", npc.RespawnSeconds, 0, 86400, 5);
        y += 42;
        npc.MoveMs = gui.NumberField($"npc-move-{npc.Id}", Gui.Field(x, y, half), "Passo a cada (ms)", npc.MoveMs, 100, 10000, 50);
        npc.AttackMs = gui.NumberField($"npc-hit-{npc.Id}", Gui.Field(x + half + 8, y, half), "Ataca a cada (ms)", npc.AttackMs, 100, 10000, 50);
        y += 50;
        if (npc.Look != null)
            gui.Label(new Vector2(x, y), "Usa um boneco montado. Escolha um sprite para trocar.", Theme.TextDim, small: true);

        if (ContentJson.Array([npc]) != before) _npcsDirty = true;

        var right = new Rectangle(x + width + 20, body.Y, body.Right - (x + width + 20), body.Height);
        _sprites ??= SpriteSheets();
        _spriteFilter = gui.TextField("sprite-filter", Gui.Field(right.X, right.Y, right.Width - 190), "Sprite (folha 3x4 em assets)", _spriteFilter);
        var sheets = _sprites.Where(s => Matches(s, _spriteFilter)).ToList();
        var row = sheets.IndexOf(npc.Sprite);
        var newRow = gui.List("sprites", new Rectangle(right.X, right.Y + 42, right.Width - 190, right.Height - 42), sheets, row);
        if (newRow != row && newRow >= 0 && newRow < sheets.Count)
        {
            npc.Sprite = sheets[newRow];
            npc.Look = null;
            npc.Wears = [];
            _npcsDirty = true;
        }

        var previewBox = new Rectangle(right.Right - 176, right.Y, 176, 220);
        var sheetTexture = npc.Look != null ? sprites.SheetFor(npc.Look, new Equipment()) : textures.Sheet(npc.Sprite);
        if (sheetTexture != null) DrawWalking(previewBox, sheetTexture, Path.GetFileName(npc.Sprite));
        gui.Label(new Vector2(previewBox.X, previewBox.Bottom + 10), "Sprite novo: arraste\num PNG 3x4 para a\njanela do jogo.", Theme.TextDim, small: true);
    }

    // ---- Helpers ----

    /// <summary>A sheet walking on the spot and turning, scaled up, in a framed box.</summary>
    private void DrawWalking(Rectangle box, Texture2D sheet, string caption)
    {
        gui.Fill(box, Theme.Background);
        Direction[] turns = [Direction.Down, Direction.Left, Direction.Up, Direction.Right];
        var dir = turns[(int)(_time / 1.6f) % turns.Length];
        var cycle = _time * 1000 / Constants.WalkTimeMs;
        var frame = SheetLayout.Frame(sheet, dir, true, cycle % 1, (int)cycle % 2 == 0);
        var scale = Math.Max(1, Math.Min((box.Width - 20) / frame.Width, (box.Height - 50) / frame.Height));
        var size = new Point(frame.Width * scale, frame.Height * scale);
        gui.Image(sheet, frame, new Rectangle(box.Center.X - size.X / 2, box.Bottom - 30 - size.Y, size.X, size.Y));
        gui.Label(new Vector2(box.X + 8, box.Bottom - 24), caption, Theme.TextDim, small: true);
    }

    /// <summary>Parts an item can wear: everything in the art set except bodies, eyes, hair and the like.</summary>
    private List<string> Parts()
    {
        string[] skip = ["body_", "eyes", "hair_", "tail_", "ears_", "whiskers", "beard_", "cheeks"];
        return sprites.PartIds
            .Where(id => !skip.Any(id.StartsWith))
            .Select(id => id.EndsWith("_m") || id.EndsWith("_f") || id.EndsWith("_c") ? id[..^2] : id)
            .Distinct()
            .Order()
            .ToList();
    }

    /// <summary>Files arrived or were imported: look for sprite sheets again next time the list shows.</summary>
    public void AssetsChanged() => _sprites = null;

    /// <summary>Every 3x4 character sheet under the game's assets, except the paper-doll parts and raw packs.</summary>
    private static List<string> SpriteSheets()
    {
        var root = DataPaths.Assets;
        if (!Directory.Exists(root)) return [];
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("sources/") || relative.Contains("/parts/") || relative.StartsWith("characters/chibi/") || relative.StartsWith("tilesets/lpc/")) continue;
            if (PngSize(file) is not { } size || size.w * 4 != size.h * 3) continue;
            found.Add(relative);
        }
        return found.Order().ToList();
    }

    /// <summary>Width and height from a PNG's header, without decoding it.</summary>
    private static (int w, int h)? PngSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[24];
            if (stream.Read(header, 0, 24) < 24) return null;
            int Be(int at) => header[at] << 24 | header[at + 1] << 16 | header[at + 2] << 8 | header[at + 3];
            return (Be(16), Be(20));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static Color? TryColor(string hex) =>
        hex.Length == 7 && hex[0] == '#' && uint.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : null;

    private static bool Matches(string text, string filter) =>
        filter.Length == 0 || text.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static int NextId(IEnumerable<int> ids) => ids.DefaultIfEmpty(0).Max() + 1;
}

using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

/// <summary>
/// Builds character sheets paper-doll style: body, head, hair and every equipped item are
/// separate LPC layers, each recoloured through its palette and stacked by z order.
/// The result is one 9x4 walk sheet per look, cached until the look changes.
/// </summary>
internal sealed class CharacterSprites
{
    public const int Frame = 64;
    public const int Columns = 9;
    public const int Rows = 4;
    private const int MaxCached = 96;

    // Shown when a slot is empty, so taking clothes off leaves plain linen rather than bare skin.
    private const string Undergarment = "white";

    private readonly GraphicsDevice _device;
    private readonly ItemCatalog _items;
    private readonly string _folder = Path.Combine(DataPaths.Assets, "characters", "lpc");
    private readonly Catalog? _catalog;
    private readonly Dictionary<string, Color[]> _raw = [];
    private readonly Dictionary<string, Texture2D> _sheets = [];

    public CharacterSprites(GraphicsDevice device, ItemCatalog items)
    {
        _device = device;
        _items = items;
        var path = Path.Combine(_folder, "catalog.json");
        if (File.Exists(path))
            _catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    /// <summary>False when assets/characters/lpc is missing; callers fall back to placeholder art.</summary>
    public bool Available => _catalog != null;

    public Texture2D Get(Appearance look, Equipment equipment)
    {
        var key = $"{look}|{equipment}";
        if (_sheets.TryGetValue(key, out var cached)) return cached;

        var sheet = Compose(look, equipment);
        _sheets[key] = sheet;
        return sheet;
    }

    /// <summary>Drops cached sheets once there are too many. Call outside Draw, never while a batch is open.</summary>
    public void Trim()
    {
        if (_sheets.Count <= MaxCached) return;
        foreach (var sheet in _sheets.Values) sheet.Dispose();
        _sheets.Clear();
    }

    private IEnumerable<(string sheet, string[] colors)> Parts(Appearance look, Equipment equipment)
    {
        yield return ("body", [look.SkinId]);
        yield return (CharacterOptions.HeadFor(look.BodyId), [look.SkinId, look.EyesId]);
        yield return (look.HairId, [look.HairColorId]);

        foreach (var id in equipment.Items)
        {
            if (_items.Get(id) is { } item) yield return (item.Sheet, item.Colors);
        }

        if (equipment[EquipSlot.Legs] == 0) yield return ("legs_pants", [Undergarment]);
        if (equipment[EquipSlot.Torso] == 0 && look.BodyId == "female") yield return ("torso_clothes_shortsleeve", [Undergarment]);
    }

    private Texture2D Compose(Appearance look, Equipment equipment)
    {
        const int width = Columns * Frame, height = Rows * Frame;
        var output = new Color[width * height];
        var catalog = _catalog!;

        var layers = new List<(int z, string path, Dictionary<uint, Color> recolor)>();
        foreach (var (sheetId, colors) in Parts(look, equipment))
        {
            if (!catalog.Sheets.TryGetValue(sheetId, out var sheet)) continue;
            var recolor = RecolorMap(sheet.Materials, colors);
            foreach (var layer in sheet.Layers)
            {
                if (layer.Paths.TryGetValue(look.BodyId, out var folder))
                    layers.Add((layer.Z, Path.Combine(_folder, folder, "walk.png"), recolor));
            }
        }

        // Stable sort keeps the order of Parts() for layers that share a z.
        foreach (var (_, path, recolor) in layers.OrderBy(l => l.z))
        {
            var source = Raw(path);
            if (source.Length != output.Length) continue;
            for (var i = 0; i < source.Length; i++)
            {
                var src = source[i];
                if (src.A == 0) continue;
                if (recolor.TryGetValue(Rgb(src), out var swap)) src = new Color(swap.R, swap.G, swap.B, src.A);
                output[i] = Over(src, output[i]);
            }
        }

        // SpriteBatch expects premultiplied alpha.
        for (var i = 0; i < output.Length; i++)
        {
            var c = output[i];
            output[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
        }

        var texture = new Texture2D(_device, width, height);
        texture.SetData(output);
        return texture;
    }

    /// <summary>Maps each colour of a material's base palette to the chosen variant, channel by channel.</summary>
    private Dictionary<uint, Color> RecolorMap(string[] materials, string[] colors)
    {
        var map = new Dictionary<uint, Color>();
        for (var channel = 0; channel < materials.Length && channel < colors.Length; channel++)
        {
            if (!_catalog!.Palettes.TryGetValue(materials[channel], out var palette)) continue;
            if (!palette.Variants.TryGetValue(palette.Base, out var from)) continue;
            if (!palette.Variants.TryGetValue(colors[channel], out var to)) continue;
            for (var i = 0; i < from.Length && i < to.Length; i++)
                map[Rgb(Hex(from[i]))] = Hex(to[i]);
        }
        return map;
    }

    /// <summary>Layer pixels with straight alpha, as stored in the PNG.</summary>
    private Color[] Raw(string path)
    {
        if (_raw.TryGetValue(path, out var cached)) return cached;

        Color[] data = [];
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            using var texture = Texture2D.FromStream(_device, stream);
            data = new Color[texture.Width * texture.Height];
            texture.GetData(data);
        }
        _raw[path] = data;
        return data;
    }

    private static Color Over(Color src, Color dst)
    {
        if (src.A == 255 || dst.A == 0) return src;
        var sa = src.A / 255f;
        var da = dst.A / 255f * (1 - sa);
        var a = sa + da;
        return new Color(
            (byte)((src.R * sa + dst.R * da) / a),
            (byte)((src.G * sa + dst.G * da) / a),
            (byte)((src.B * sa + dst.B * da) / a),
            (byte)(a * 255));
    }

    private static uint Rgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    private static Color Hex(string hex)
    {
        var v = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private sealed class Catalog
    {
        public Dictionary<string, PaletteDef> Palettes { get; init; } = [];
        public Dictionary<string, SheetDef> Sheets { get; init; } = [];
    }

    private sealed class PaletteDef
    {
        public string Base { get; init; } = "";
        public Dictionary<string, string[]> Variants { get; init; } = [];
    }

    private sealed class SheetDef
    {
        public string[] Materials { get; init; } = [];
        public LayerDef[] Layers { get; init; } = [];
    }

    private sealed class LayerDef
    {
        public int Z { get; init; }
        public Dictionary<string, string> Paths { get; init; } = [];
    }
}

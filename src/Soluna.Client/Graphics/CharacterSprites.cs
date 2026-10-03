using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

/// <summary>
/// Builds character sheets paper-doll style from the active art set (<see cref="CharacterArt"/>):
/// body, race parts, eyes, hair, beard and every equipped item are separate layers, some painted
/// in a chosen colour, stacked by z order. The result is one 3x4 RPG Maker sheet per look,
/// cached until the look changes.
/// </summary>
internal sealed class CharacterSprites
{
    private const int MaxCached = 96;

    // How far the brightest pixels of a layer may move toward white when painted.
    // Below 1 keeps light clothes from washing out.
    private const float HighlightStrength = 0.45f;

    private readonly GraphicsDevice _device;
    private readonly ItemCatalog _items;
    private string _folder = CharacterArt.Folder;
    private Catalog? _catalog;
    private readonly Dictionary<string, Layer> _layers = [];
    private readonly Dictionary<string, Texture2D> _sheets = [];

    public CharacterSprites(GraphicsDevice device, ItemCatalog items)
    {
        _device = device;
        _items = items;
        Reload();
    }

    /// <summary>Reads the active art set again, after its files changed. Same rule as <see cref="Trim"/>.</summary>
    public void Reload()
    {
        Clear();
        _layers.Clear();
        CharacterArt.Refresh();
        _folder = CharacterArt.Folder;
        var path = Path.Combine(_folder, "catalog.json");
        _catalog = File.Exists(path)
            ? JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;
    }

    /// <summary>False when no character art is installed; callers fall back to placeholder art.</summary>
    public bool Available => _catalog != null;

    public Texture2D Get(Appearance look, Equipment equipment) => Get(look, equipment, null, null);

    /// <summary>
    /// A look wearing one more part that is not an item yet (the editor's preview of an item being made).
    /// Cached like the rest, so it is cheap to ask every frame.
    /// </summary>
    public Texture2D Get(Appearance look, Equipment equipment, string? extraSheet, string? extraColor)
    {
        var key = $"{look}|{equipment}|{extraSheet}|{extraColor}";
        if (_sheets.TryGetValue(key, out var cached)) return cached;

        var sheet = Compose(look, equipment, extraSheet, extraColor);
        _sheets[key] = sheet;
        return sheet;
    }

    /// <summary>Every part the active art set has, for the editors to choose from.</summary>
    public IEnumerable<string> SheetIds => _catalog?.Sheets.Keys.Order() ?? Enumerable.Empty<string>();

    /// <summary>Drops cached sheets once there are too many. Call outside Draw, never while a batch is open.</summary>
    public void Trim()
    {
        if (_sheets.Count > MaxCached) Clear();
    }

    /// <summary>Forgets every composed sheet, for when the item catalog changes. Same rule as <see cref="Trim"/>.</summary>
    public void Clear()
    {
        foreach (var sheet in _sheets.Values) sheet.Dispose();
        _sheets.Clear();
    }

    /// <summary>Layers for a look, each with the colour to paint it (null keeps the drawn colours).</summary>
    private IEnumerable<(string sheet, string? color)> Parts(Appearance look, Equipment equipment)
    {
        var options = CharacterOptions.Current;
        var skin = look.SkinId;
        // Skins are either colours to paint one body with, or ids of pre-coloured bodies.
        var paintedSkin = skin.StartsWith('#');
        string Fill(string template) => template.Replace("{body}", look.BodyId).Replace("{skin}", skin);

        yield return (Fill(options.Body), paintedSkin ? skin : null);
        foreach (var part in look.RaceChoice.Parts ?? []) yield return (Fill(part), null);
        yield return (look.EyesId, null);

        // Under a hat, use the flattened version of the hair when the art set has one.
        var hat = equipment[EquipSlot.Head] != 0 && _catalog!.Sheets.ContainsKey(look.HairId + "_hat");
        yield return (hat ? look.HairId + "_hat" : look.HairId, look.HairColorId);
        if (look.BeardId.Length > 0) yield return (look.BeardId, look.HairColorId);

        foreach (var id in equipment.Items)
        {
            if (_items.Get(id) is { } item) yield return (item.Sheet, item.Colors.FirstOrDefault());
        }

        foreach (var (slotName, sheet) in options.Defaults)
        {
            if (Enum.TryParse<EquipSlot>(slotName, out var slot) && equipment[slot] == 0) yield return (sheet, null);
        }
    }

    /// <summary>
    /// The sheet to draw for a part: the version for this body ("<id>_m" / "<id>_f"), the shared one,
    /// or, for a piece drawn only for the other body, that one rather than nothing.
    /// </summary>
    private string? Resolve(string sheetId, string body)
    {
        var sheets = _catalog!.Sheets;
        if (sheets.ContainsKey($"{sheetId}_{body}")) return $"{sheetId}_{body}";
        if (sheets.ContainsKey(sheetId)) return sheetId;
        return CharacterOptions.Current.Bodies
            .Select(b => $"{sheetId}_{b.Id}")
            .FirstOrDefault(sheets.ContainsKey);
    }

    private Texture2D Compose(Appearance look, Equipment equipment, string? extraSheet, string? extraColor)
    {
        var catalog = _catalog!;
        var width = catalog.Frame * 3;
        var height = catalog.Frame * 4;
        var output = new Color[width * height];

        var layers = new List<(int z, Layer layer, string? color)>();
        var parts = Parts(look, equipment);
        if (!string.IsNullOrEmpty(extraSheet)) parts = parts.Append((extraSheet, extraColor));
        foreach (var (sheetId, color) in parts)
        {
            // Body-specific pieces exist as "<id>_m" / "<id>_f"; everything else fits both bodies.
            if (Resolve(sheetId, look.BodyId) is not { } id || !catalog.Sheets.TryGetValue(id, out var def)) continue;
            foreach (var (file, z) in def.AllLayers)
            {
                if (Load(file) is { } layer) layers.Add((z, layer, color));
            }
        }

        // Stable sort keeps the order of Parts() for layers that share a z.
        foreach (var (_, layer, color) in layers.OrderBy(l => l.z))
        {
            if (layer.Pixels.Length != output.Length) continue;
            // No colour (or one the set leaves empty, like hair colour for cats) keeps the drawn colours.
            var tint = Hex(color);
            for (var i = 0; i < output.Length; i++)
            {
                var src = layer.Pixels[i];
                if (src.A == 0) continue;
                if (tint is { } t) src = Colorize(src, t, layer.MedianLuminance);
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

    /// <summary>
    /// Repaints a pixel in <paramref name="target"/> while keeping its shading: the layer's typical
    /// brightness becomes the target colour, darker pixels scale down toward black (so outlines stay
    /// outlines) and brighter ones move part of the way toward white.
    /// </summary>
    private static Color Colorize(Color src, Color target, float median)
    {
        var ratio = Luminance(src) / median;
        Vector3 rgb;
        if (ratio <= 1)
        {
            rgb = target.ToVector3() * ratio;
        }
        else
        {
            var t = Math.Clamp((ratio - 1) / (1 / median - 1 + 1e-4f), 0, 1) * HighlightStrength;
            rgb = Vector3.Lerp(target.ToVector3(), Vector3.One, t);
        }
        return new Color(rgb.X, rgb.Y, rgb.Z) with { A = src.A };
    }

    private static float Luminance(Color c) => (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;

    /// <summary>Layer pixels with straight alpha, as stored in the PNG, plus the median brightness used to colorize them.</summary>
    private Layer? Load(string file)
    {
        if (_layers.TryGetValue(file, out var cached)) return cached;

        Layer? layer = null;
        var path = Path.Combine(_folder, file);
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            using var texture = Texture2D.FromStream(_device, stream);
            var pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);

            var lums = pixels.Where(p => p.A > 0).Select(Luminance).Order().ToArray();
            var median = lums.Length > 0 ? Math.Max(0.05f, lums[lums.Length / 2]) : 0.5f;
            layer = new Layer(pixels, median);
        }
        _layers[file] = layer!;
        return layer;
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

    private static Color? Hex(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || !uint.TryParse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out var v))
            return null;
        return new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private sealed record Layer(Color[] Pixels, float MedianLuminance);

    private sealed class Catalog
    {
        public int Frame { get; init; } = 32;
        public Dictionary<string, SheetDef> Sheets { get; init; } = [];
    }

    /// <summary>A sheet is one layer (file and z), or several, such as hair drawn in front of and behind the body.</summary>
    private sealed class SheetDef
    {
        public string File { get; init; } = "";
        public int Z { get; init; }
        public LayerDef[] Layers { get; init; } = [];

        public IEnumerable<(string file, int z)> AllLayers =>
            Layers.Length > 0 ? Layers.Select(l => (l.File, l.Z)) : [(File, Z)];
    }

    private sealed class LayerDef
    {
        public string File { get; init; } = "";
        public int Z { get; init; }
    }
}

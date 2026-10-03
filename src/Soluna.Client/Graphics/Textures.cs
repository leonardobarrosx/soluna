using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

/// <summary>
/// Loads tilesets and character sheets from assets/, falling back to the painted placeholders.
/// </summary>
internal sealed class Textures(GraphicsDevice device)
{
    private readonly Dictionary<string, Texture2D> _tilesets = [];
    private readonly Dictionary<int, Texture2D> _characters = [];
    private Texture2D? _missing;

    public Texture2D Pixel { get; } = CreatePixel(device);

    public Texture2D Shadow { get; } = PlaceholderArt.Shadow(device);

    public static string TilesetFolder => Path.Combine(DataPaths.Assets, "tilesets");

    public static string CharacterFolder => Path.Combine(DataPaths.Assets, "characters");

    public Texture2D Tileset(string name)
    {
        if (_tilesets.TryGetValue(name, out var cached)) return cached;

        Texture2D texture;
        if (name == PlaceholderTiles.TilesetName)
        {
            texture = PlaceholderArt.Tileset(device);
        }
        else
        {
            var path = Path.Combine(TilesetFolder, name);
            texture = File.Exists(path) ? Load(path) : _missing ??= PlaceholderArt.Missing(device);
        }
        _tilesets[name] = texture;
        return texture;
    }

    /// <summary>Every PNG under assets/tilesets, as a path relative to it with forward slashes.</summary>
    public static IEnumerable<string> AvailableTilesets() =>
        Directory.Exists(TilesetFolder)
            ? Directory.EnumerateFiles(TilesetFolder, "*.png", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(TilesetFolder, f).Replace('\\', '/'))
                .Order()
            : [];

    /// <summary>
    /// Fallback character sheet when the LPC layers are missing: assets/characters/{n}.png
    /// (3x4 frames, RPG Maker layout) if present, otherwise a painted placeholder.
    /// </summary>
    public Texture2D Character(int sprite)
    {
        if (_characters.TryGetValue(sprite, out var cached)) return cached;

        var path = Path.Combine(CharacterFolder, $"{sprite}.png");
        var texture = File.Exists(path) ? Load(path) : PlaceholderArt.Character(device, sprite);
        _characters[sprite] = texture;
        return texture;
    }

    private readonly Dictionary<string, Texture2D?> _sheets = [];

    /// <summary>A sprite sheet by path under assets/, or null when the file is missing.</summary>
    public Texture2D? Sheet(string assetPath)
    {
        if (_sheets.TryGetValue(assetPath, out var cached)) return cached;
        var path = Path.Combine(DataPaths.Assets, assetPath);
        var sheet = assetPath.Length > 0 && File.Exists(path) ? Load(path) : null;
        _sheets[assetPath] = sheet;
        return sheet;
    }

    private Texture2D Load(string path)
    {
        using var stream = File.OpenRead(path);
        var texture = Texture2D.FromStream(device, stream);

        // FromStream gives straight alpha; SpriteBatch blends premultiplied.
        var data = new Color[texture.Width * texture.Height];
        texture.GetData(data);
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            data[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
        }
        texture.SetData(data);
        return texture;
    }

    private static Texture2D CreatePixel(GraphicsDevice device)
    {
        var pixel = new Texture2D(device, 1, 1);
        pixel.SetData([Color.White]);
        return pixel;
    }
}

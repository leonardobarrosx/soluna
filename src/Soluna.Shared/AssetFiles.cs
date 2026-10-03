using System.Security.Cryptography;

namespace Soluna.Shared;

/// <summary>What kind of file an admin imports; decides where it goes and how it is checked.</summary>
public enum AssetKind : byte
{
    /// <summary>A tileset: width and height multiples of 32. Goes to tilesets/imported.</summary>
    Tileset = 1,

    /// <summary>A character sheet: 3 frames by 4 directions. Goes to characters/sprites.</summary>
    Sprite = 2,
}

/// <summary>
/// The game files players need: everything under the game's assets except assets/sources (raw packs
/// kept only as material for the importers), and only the kinds the client reads.
/// </summary>
public static class AssetFiles
{
    public const int MaxUploadBytes = 8 * 1024 * 1024;

    private static readonly string[] Extensions = [".png", ".json", ".ttf"];

    public static bool IsDistributed(string relative) =>
        !relative.StartsWith("sources/", StringComparison.OrdinalIgnoreCase)
        // characters/private/active names the art set in use; clients need it to pick the same one.
        && (Extensions.Contains(Path.GetExtension(relative).ToLowerInvariant()) || Path.GetFileName(relative) == "active");

    /// <summary>Every distributed file under a game's assets, as forward-slash paths with size and hash.</summary>
    public static IEnumerable<(string path, long size, string hash)> Scan(string assetsRoot)
    {
        if (!Directory.Exists(assetsRoot)) yield break;
        foreach (var file in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(assetsRoot, file).Replace('\\', '/');
            if (!IsDistributed(relative)) continue;
            yield return (relative, new FileInfo(file).Length, Hash(file));
        }
    }

    /// <summary>A short content hash: the first 16 hex digits of SHA-256, plenty to tell versions apart.</summary>
    public static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream))[..16];
    }

    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..16];

    /// <summary>The full path of an asset in the current game, refusing anything that would escape its folder.</summary>
    public static string? Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(DataPaths.Assets, relative));
        var root = Path.GetFullPath(DataPaths.Assets) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>Width and height from a PNG's header, or null if the bytes are not a PNG.</summary>
    public static (int w, int h)? PngSize(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 24 || !data[..8].SequenceEqual(signature)) return null;
        int Be(ReadOnlySpan<byte> d, int at) => d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3];
        return (Be(data, 16), Be(data, 20));
    }

    /// <summary>Why a PNG cannot be imported as this kind, or null if it can.</summary>
    public static string? Check(AssetKind kind, (int w, int h) size) => kind switch
    {
        AssetKind.Tileset when size.w % Constants.TileSize != 0 || size.h % Constants.TileSize != 0 =>
            $"Um tileset precisa ter largura e altura múltiplas de {Constants.TileSize} (este tem {size.w}x{size.h}).",
        AssetKind.Sprite when size.w * 4 != size.h * 3 =>
            $"Um sprite de personagem precisa de 3 quadros por 4 direções, proporção 3:4 (este tem {size.w}x{size.h}).",
        _ => null,
    };

    /// <summary>Where an imported file goes, relative to the game's assets.</summary>
    public static string Destination(AssetKind kind, string name, bool shareable)
    {
        var folder = kind == AssetKind.Tileset ? "tilesets" : "characters";
        var sub = kind == AssetKind.Tileset ? "imported" : "sprites";
        return shareable ? $"{folder}/{sub}/{name}.png" : $"{folder}/private/{sub}/{name}.png";
    }
}

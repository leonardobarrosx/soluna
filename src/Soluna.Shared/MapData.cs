using System.Text.Json;

namespace Soluna.Shared;

/// <summary>
/// Layers in draw order. Ground, Mask and Mask2 are drawn under characters,
/// Fringe and Fringe2 over them, as in Crystalshire.
/// </summary>
public enum MapLayer
{
    Ground = 0,
    Mask = 1,
    Mask2 = 2,
    Fringe = 3,
    Fringe2 = 4,
}

public enum TileAttribute : byte
{
    None = 0,
    Blocked = 1,
}

public sealed class MapData
{
    public const int LayerCount = 5;
    public const int FirstFringeLayer = (int)MapLayer.Fringe;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Tileset file names, indexed by the tileset part of a tile ref.</summary>
    public List<string> Tilesets { get; set; } = [];

    /// <summary>One array per layer, Width * Height cells, each a <see cref="TileRef"/> value.</summary>
    public int[][] Layers { get; set; } = [];

    public byte[] Attributes { get; set; } = [];

    public static MapData CreateEmpty(int id, string name, int width, int height)
    {
        var map = new MapData { Id = id, Name = name, Width = width, Height = height };
        map.Layers = new int[LayerCount][];
        for (var i = 0; i < LayerCount; i++)
        {
            map.Layers[i] = new int[width * height];
            Array.Fill(map.Layers[i], TileRef.Empty);
        }
        map.Attributes = new byte[width * height];
        return map;
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Index(int x, int y) => y * Width + x;

    public int GetTile(MapLayer layer, int x, int y) => Layers[(int)layer][Index(x, y)];

    public void SetTile(MapLayer layer, int x, int y, int tile) => Layers[(int)layer][Index(x, y)] = tile;

    public TileAttribute GetAttribute(int x, int y) => (TileAttribute)Attributes[Index(x, y)];

    public void SetAttribute(int x, int y, TileAttribute attribute) => Attributes[Index(x, y)] = (byte)attribute;

    public bool IsWalkable(int x, int y) => InBounds(x, y) && GetAttribute(x, y) != TileAttribute.Blocked;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    public static MapData FromBytes(ReadOnlySpan<byte> bytes) =>
        JsonSerializer.Deserialize<MapData>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty map.");
}

/// <summary>
/// A tile reference packed into an int: the tileset index in the high 16 bits,
/// the tile index inside that tileset (row * columns + column) in the low 16.
/// </summary>
public static class TileRef
{
    public const int Empty = -1;

    public static int Make(int tileset, int index) => (tileset << 16) | (index & 0xFFFF);

    public static int Tileset(int tile) => tile >> 16;

    public static int Index(int tile) => tile & 0xFFFF;
}

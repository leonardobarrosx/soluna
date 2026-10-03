using System.Text.Json;

namespace Soluna.Shared;

/// <summary>
/// The five layers of a Crystalshire-style map, in draw order: Ground, Mask and Mask2 under
/// characters, Fringe and Fringe2 over them. Imported maps may have more layers; see
/// <see cref="MapData.FringeFrom"/>.
/// </summary>
public enum MapLayer
{
    Ground = 0,
    Mask = 1,
    Mask2 = 2,
    Fringe = 3,
    Fringe2 = 4,
}

/// <summary>What a tile does, besides how it looks. Mirrors Crystalshire's tile types.</summary>
public enum TileAttribute : byte
{
    None = 0,
    Blocked = 1,

    /// <summary>Stepping on it moves the player to the destination in <see cref="MapData.Warps"/>.</summary>
    Warp = 2,

    /// <summary>Walkable for players, never entered by NPCs (doorways, shop counters).</summary>
    NpcAvoid = 3,

    /// <summary>Restores health while standing on it (takes effect once vitals exist).</summary>
    Heal = 4,
}

/// <summary>Whether players may fight on a map.</summary>
public enum MapMoral : byte
{
    Safe = 0,
    Pvp = 1,
}

/// <summary>A warp tile and where it leads.</summary>
public sealed class Warp
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Map { get; set; }
    public int ToX { get; set; }
    public int ToY { get; set; }
}

/// <summary>The maps beyond each edge; walking off an edge with a link carries on into that map. 0 means none.</summary>
public sealed class MapLinks
{
    public int Up { get; set; }
    public int Down { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }

    public int this[Direction dir] => dir switch
    {
        Direction.Up => Up,
        Direction.Down => Down,
        Direction.Left => Left,
        Direction.Right => Right,
        _ => 0,
    };

    public void Set(Direction dir, int map)
    {
        switch (dir)
        {
            case Direction.Up: Up = map; break;
            case Direction.Down: Down = map; break;
            case Direction.Left: Left = map; break;
            case Direction.Right: Right = map; break;
        }
    }
}

public sealed class MapData
{
    public const int DefaultLayerCount = 5;
    public const int MaxLayers = 16;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Goes up on every save; clients keep maps in a cache and fetch them again only when it changes.</summary>
    public int Revision { get; set; } = 1;

    public MapMoral Moral { get; set; }

    /// <summary>Music file under assets/music, or empty for silence.</summary>
    public string Music { get; set; } = "";

    public MapLinks Links { get; set; } = new();

    public List<Warp> Warps { get; set; } = [];

    public List<NpcSpawn> Npcs { get; set; } = [];

    /// <summary>Tileset file names, indexed by the tileset part of a tile ref.</summary>
    public List<string> Tilesets { get; set; } = [];

    /// <summary>One array per layer, Width * Height cells, each a <see cref="TileRef"/> value.</summary>
    public int[][] Layers { get; set; } = [];

    /// <summary>Layers from this index up are drawn over characters.</summary>
    public int FringeFrom { get; set; } = (int)MapLayer.Fringe;

    /// <summary>Optional names shown in the editor, one per layer.</summary>
    public string[]? LayerNames { get; set; }

    /// <summary>Where new players appear; -1 means the middle of the map.</summary>
    public int SpawnX { get; set; } = -1;
    public int SpawnY { get; set; } = -1;

    public (int x, int y) Spawn => SpawnX >= 0 && SpawnY >= 0 ? (SpawnX, SpawnY) : (Width / 2, Height / 2);

    public string LayerName(int layer) =>
        LayerNames != null && layer < LayerNames.Length ? LayerNames[layer]
        : layer < DefaultLayerCount ? ((MapLayer)layer).ToString()
        : $"Layer {layer + 1}";

    public byte[] Attributes { get; set; } = [];

    public static MapData CreateEmpty(int id, string name, int width, int height, int layers = DefaultLayerCount)
    {
        var map = new MapData { Id = id, Name = name, Width = width, Height = height };
        map.Layers = new int[layers][];
        for (var i = 0; i < layers; i++)
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

    public int GetTile(int layer, int x, int y) => Layers[layer][Index(x, y)];

    public void SetTile(int layer, int x, int y, int tile) => Layers[layer][Index(x, y)] = tile;

    public TileAttribute GetAttribute(int x, int y) => (TileAttribute)Attributes[Index(x, y)];

    public void SetAttribute(int x, int y, TileAttribute attribute) => Attributes[Index(x, y)] = (byte)attribute;

    public bool IsWalkable(int x, int y) => InBounds(x, y) && GetAttribute(x, y) != TileAttribute.Blocked;

    public Warp? WarpAt(int x, int y) =>
        InBounds(x, y) && GetAttribute(x, y) == TileAttribute.Warp ? Warps.FirstOrDefault(w => w.X == x && w.Y == y) : null;

    /// <summary>Marks a warp tile, replacing any warp already there.</summary>
    public void SetWarp(int x, int y, int map, int toX, int toY)
    {
        Warps.RemoveAll(w => w.X == x && w.Y == y);
        Warps.Add(new Warp { X = x, Y = y, Map = map, ToX = toX, ToY = toY });
        SetAttribute(x, y, TileAttribute.Warp);
    }

    /// <summary>Clears a tile's attribute, and its warp if it had one.</summary>
    public void ClearAttribute(int x, int y)
    {
        Warps.RemoveAll(w => w.X == x && w.Y == y);
        SetAttribute(x, y, TileAttribute.None);
    }

    /// <summary>The walkable tile nearest to (x, y), searching outward; (x, y) itself if nothing is found.</summary>
    public (int x, int y) NearestWalkable(int x, int y, Func<int, int, bool>? free = null)
    {
        for (var radius = 0; radius < Math.Max(Width, Height); radius++)
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
            var (tx, ty) = (x + dx, y + dy);
            if (IsWalkable(tx, ty) && GetAttribute(tx, ty) != TileAttribute.Warp && (free == null || free(tx, ty)))
                return (tx, ty);
        }
        return (x, y);
    }

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

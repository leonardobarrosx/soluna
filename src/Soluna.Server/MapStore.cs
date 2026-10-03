using Soluna.Shared;
using T = Soluna.Shared.PlaceholderTiles;

namespace Soluna.Server;

/// <summary>Loads maps from data/maps/{id}.json, creating the starter map on first run.</summary>
internal sealed class MapStore
{
    public const int StartMapId = 1;

    private readonly string _folder;
    private readonly Dictionary<int, MapData> _maps = [];

    public MapStore(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public MapData Get(int id)
    {
        if (_maps.TryGetValue(id, out var cached)) return cached;

        var path = PathFor(id);
        MapData map;
        if (File.Exists(path))
        {
            map = MapData.FromBytes(File.ReadAllBytes(path));
            Log.Info($"Loaded map {id} '{map.Name}' ({map.Width}x{map.Height}).");
        }
        else
        {
            // Real art when the LPC tiles are present, painted placeholders otherwise.
            var lpc = File.Exists(Path.Combine(DataPaths.Assets, "tilesets", LpcStarterMap.ProbeFile));
            map = lpc ? LpcStarterMap.Build(id) : StarterMap.Build(id);
            Save(map);
            Log.Info($"Created starter map at {path}.");
        }
        _maps[id] = map;
        return map;
    }

    public void Save(MapData map)
    {
        _maps[map.Id] = map;
        File.WriteAllBytes(PathFor(map.Id), map.ToBytes());
    }

    private string PathFor(int id) => Path.Combine(_folder, $"{id}.json");
}

/// <summary>A small walled glade painted with the placeholder tileset.</summary>
internal static class StarterMap
{
    public static MapData Build(int id)
    {
        const int w = 40, h = 30;
        var map = MapData.CreateEmpty(id, "Clareira", w, h);
        map.Tilesets.Add(T.TilesetName);
        var rng = new Random(7);

        int Tile(int index) => TileRef.Make(0, index);

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            map.SetTile(MapLayer.Ground, x, y, Tile(rng.Next(5) == 0 ? T.GrassDark : T.Grass));
            if (rng.Next(25) == 0) map.SetTile(MapLayer.Mask, x, y, Tile(T.Flowers));
        }

        // Paths crossing the glade.
        for (var x = 1; x < w - 1; x++) map.SetTile(MapLayer.Ground, x, 14, Tile(T.Path));
        for (var y = 1; y < h - 1; y++) map.SetTile(MapLayer.Ground, 19, y, Tile(T.Path));

        // A pond.
        for (var y = 4; y < 10; y++)
        for (var x = 26; x < 34; x++)
        {
            var dx = (x - 29.5) / 4.0;
            var dy = (y - 6.5) / 3.0;
            if (dx * dx + dy * dy > 1) continue;
            map.SetTile(MapLayer.Ground, x, y, Tile(T.Water));
            map.SetTile(MapLayer.Mask, x, y, TileRef.Empty);
            map.SetAttribute(x, y, TileAttribute.Blocked);
        }

        // Stone wall around the edge.
        for (var x = 0; x < w; x++) { Wall(x, 0); Wall(x, h - 1); }
        for (var y = 0; y < h; y++) { Wall(0, y); Wall(w - 1, y); }

        // Trees: the trunk blocks and sits under characters, the canopy hangs over them.
        (int x, int y)[] trees = [(4, 4), (7, 6), (5, 9), (11, 3), (12, 20), (15, 24), (8, 22), (30, 20), (33, 24), (25, 23), (35, 12), (23, 5)];
        foreach (var (tx, ty) in trees)
        {
            map.SetTile(MapLayer.Mask, tx, ty, Tile(T.TreeTrunk));
            map.SetAttribute(tx, ty, TileAttribute.Blocked);
            map.SetTile(MapLayer.Fringe, tx, ty - 1, Tile(T.TreeCanopy));
        }

        (int x, int y)[] rocks = [(9, 15), (27, 13), (16, 8), (31, 27)];
        foreach (var (rx, ry) in rocks)
        {
            map.SetTile(MapLayer.Mask, rx, ry, Tile(T.Rock));
            map.SetAttribute(rx, ry, TileAttribute.Blocked);
        }

        return map;

        void Wall(int x, int y)
        {
            map.SetTile(MapLayer.Ground, x, y, Tile(T.StoneWall));
            map.SetTile(MapLayer.Mask, x, y, TileRef.Empty);
            map.SetAttribute(x, y, TileAttribute.Blocked);
        }
    }
}

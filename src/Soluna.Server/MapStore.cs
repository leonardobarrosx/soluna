using Soluna.Shared;
using T = Soluna.Shared.PlaceholderTiles;

namespace Soluna.Server;

/// <summary>
/// Loads maps by id. data/maps/private/{id}.json wins over data/maps/{id}.json: private maps use
/// art that cannot be committed (data/maps/private is ignored by git), so a server with that art
/// plays it while a fresh clone still gets the committed map. A map saves back to where it came from.
/// </summary>
internal sealed class MapStore
{
    public const int StartMapId = 1;

    /// <summary>Pipoya's sample map, imported as the start map when the pack is in assets/tilesets/private.</summary>
    private static readonly string PipoyaSample = Path.Combine(
        DataPaths.Assets, "tilesets", "private", "Pipoya RPG Tileset 32x32", "Pipoya RPG Tileset 32x32", "SampleMap", "samplemap.tmx");

    private readonly string _folder;
    private readonly string _privateFolder;
    private readonly Dictionary<int, MapData> _maps = [];
    private readonly Dictionary<int, string> _paths = [];

    public MapStore(string folder)
    {
        _folder = folder;
        _privateFolder = Path.Combine(folder, "private");
        Directory.CreateDirectory(folder);
    }

    public MapData Get(int id)
    {
        if (_maps.TryGetValue(id, out var cached)) return cached;

        var privatePath = Path.Combine(_privateFolder, $"{id}.json");
        var publicPath = Path.Combine(_folder, $"{id}.json");

        if (!File.Exists(privatePath) && id == StartMapId && File.Exists(PipoyaSample))
        {
            var imported = TmxImporter.Import(PipoyaSample, id, "Vila de Soluna", Path.Combine(DataPaths.Assets, "tilesets"));
            // The road just east of the river's main bridge.
            imported.SpawnX = 24;
            imported.SpawnY = 30;
            Store(imported, privatePath);
            Log.Info($"Imported Pipoya's sample map as map {id} into {privatePath}.");
        }
        if (id != StartMapId && !Exists(id)) throw new KeyNotFoundException($"Map {id} does not exist.");

        MapData map;
        var path = File.Exists(privatePath) ? privatePath : publicPath;
        if (File.Exists(path))
        {
            map = MapData.FromBytes(File.ReadAllBytes(path));
            Log.Info($"Loaded map {id} '{map.Name}' ({map.Width}x{map.Height}) from {Path.GetRelativePath(DataPaths.Root, path)}.");
        }
        else
        {
            // Real art when the LPC tiles are present, painted placeholders otherwise.
            var lpc = File.Exists(Path.Combine(DataPaths.Assets, "tilesets", LpcStarterMap.ProbeFile));
            map = lpc ? LpcStarterMap.Build(id) : StarterMap.Build(id);
            Log.Info($"Created starter map at {path}.");
        }
        Store(map, path);
        if (id == StartMapId) EnsureForest(map);
        return map;
    }

    public bool Exists(int id) =>
        _maps.ContainsKey(id)
        || File.Exists(Path.Combine(_privateFolder, $"{id}.json"))
        || File.Exists(Path.Combine(_folder, $"{id}.json"))
        || id == StartMapId;

    /// <summary>Every map id on disk, in order.</summary>
    public IEnumerable<int> Ids() =>
        new[] { _folder, _privateFolder }
            .Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.json"))
            .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : 0)
            .Where(id => id > 0)
            .Concat(_maps.Keys)
            .Distinct()
            .Order();

    /// <summary>Saves a map with the next revision, so clients know their cached copy is stale.</summary>
    public void Save(MapData map)
    {
        map.Revision++;
        Store(map, _paths.GetValueOrDefault(map.Id) ?? Path.Combine(_folder, $"{map.Id}.json"));
    }

    /// <summary>
    /// A new map floored with one tile, using the given tilesets. It is private when they are, like
    /// any map that depends on art the repository cannot hold.
    /// </summary>
    public MapData Create(string name, int width, int height, List<string> tilesets, int floor, int layers, int fringeFrom)
    {
        var id = Ids().DefaultIfEmpty(0).Max() + 1;
        var map = MapData.CreateEmpty(id, name, width, height, layers);
        map.Tilesets.AddRange(tilesets);
        map.FringeFrom = fringeFrom;
        if (floor != TileRef.Empty) Array.Fill(map.Layers[0], floor);
        var isPrivate = tilesets.Any(t => t.StartsWith("private/", StringComparison.OrdinalIgnoreCase));
        Store(map, Path.Combine(isPrivate ? _privateFolder : _folder, $"{id}.json"));
        Log.Info($"Created map {id} '{name}' ({width}x{height}).");
        return map;
    }

    /// <summary>
    /// With Pipoya's village as the start map, adds a forest north of it (map 2) the first time,
    /// linked to the village's north road, so there is a second map to walk into.
    /// </summary>
    private void EnsureForest(MapData village)
    {
        if (village.Name != "Vila de Soluna" || Exists(2)) return;

        var forest = ForestMap.Build(village, 2);
        forest.Links.Down = village.Id;
        Store(forest, Path.Combine(_privateFolder, "2.json"));
        village.Links.Up = forest.Id;
        Save(village);
        Log.Info("Created the forest north of the village as map 2.");
    }

    /// <summary>Imports a Tiled map and saves it, as private if any of its tilesets is.</summary>
    public MapData Import(string tmxPath, int id, string name)
    {
        var map = TmxImporter.Import(tmxPath, id, name, Path.Combine(DataPaths.Assets, "tilesets"));
        var isPrivate = map.Tilesets.Any(t => t.StartsWith("private/", StringComparison.OrdinalIgnoreCase));
        var path = Path.Combine(isPrivate ? _privateFolder : _folder, $"{id}.json");
        Store(map, path);
        Log.Info($"Saved map {id} to {Path.GetRelativePath(DataPaths.Root, path)}.");
        return map;
    }

    private void Store(MapData map, string path)
    {
        _maps[map.Id] = map;
        _paths[map.Id] = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, map.ToBytes());
    }
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

using Soluna.Shared;

namespace Soluna.Server;

/// <summary>
/// The starter glade painted with the LPC tiles in assets/tilesets/lpc: grass, dirt paths
/// meeting in a small square, a pond, and a ring of pines around the edge.
/// Layers: Ground grass, Mask terrain overlays, Mask2 trunks and rocks, Fringe/Fringe2 canopies.
/// </summary>
internal static class LpcStarterMap
{
    public const string ProbeFile = "lpc/grass.png";

    private const int Grass = 0, Dirt = 1, Water = 2, Treetop = 3, Trunk = 4, Rock = 5;
    private static readonly string[] Tilesets = ["lpc/grass.png", "lpc/dirt.png", "lpc/water.png", "lpc/treetop.png", "lpc/trunk.png", "lpc/rock.png"];

    // treetop.png and trunk.png are 6 tiles wide: a round tree in rows 0-2, a pine in rows 3-6,
    // each 3 tiles wide; the matching trunk is the 3x3 block below.
    private const int SheetColumns = 6;

    public static MapData Build(int id)
    {
        const int w = 40, h = 30;
        var map = MapData.CreateEmpty(id, "Clareira", w, h);
        map.Tilesets.AddRange(Tilesets);
        var rng = new Random(7);

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            map.SetTile(MapLayer.Ground, x, y, TileRef.Make(Grass, Autotile.Fills[rng.Next(Autotile.Fills.Length)]));

        // Dirt: a square in the middle and two paths, all two or more tiles thick for the autotiler.
        var dirt = new bool[w, h];
        Fill(dirt, 16, 11, 24, 18);
        Fill(dirt, 4, 13, 36, 15);
        Fill(dirt, 19, 4, 21, 26);
        Paint(map, dirt, Dirt, MapLayer.Mask, rng);

        // A pond in the north-east.
        var water = new bool[w, h];
        for (var y = 4; y <= 10; y++)
        for (var x = 25; x <= 34; x++)
        {
            var dx = (x - 29.5) / 5.0;
            var dy = (y - 7.0) / 3.6;
            if (dx * dx + dy * dy <= 1) water[x, y] = true;
        }
        Fill(water, 27, 4, 32, 10);
        Paint(map, water, Water, MapLayer.Mask, rng);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            if (water[x, y]) map.SetAttribute(x, y, TileAttribute.Blocked);

        // Pines around the edge, two staggered rows so the forest looks thick.
        for (var x = -1; x < w; x += 3)
        {
            Tree(map, x, 0, pine: true, MapLayer.Fringe);
            Tree(map, x + 1, h - 3, pine: true, MapLayer.Fringe2);
        }
        for (var y = 2; y < h - 3; y += 3)
        {
            Tree(map, -1, y, pine: true, y % 2 == 0 ? MapLayer.Fringe : MapLayer.Fringe2);
            Tree(map, w - 2, y + 1, pine: true, y % 2 == 0 ? MapLayer.Fringe2 : MapLayer.Fringe);
        }
        for (var x = 0; x < w; x++)
        for (var y = 0; y < h; y++)
            if (x < 1 || y < 2 || x >= w - 1 || y >= h - 1) map.SetAttribute(x, y, TileAttribute.Blocked);

        // A few round trees and rocks inside the glade.
        (int x, int y)[] trees = [(5, 4), (10, 6), (6, 18), (12, 22), (26, 19), (31, 22), (14, 2), (34, 15)];
        foreach (var (tx, ty) in trees) Tree(map, tx, ty, pine: false, MapLayer.Fringe);

        (int x, int y)[] rocks = [(9, 11), (24, 9), (28, 25), (15, 17)];
        foreach (var (rx, ry) in rocks)
        {
            map.SetTile(MapLayer.Mask2, rx, ry, TileRef.Make(Rock, 0));
            map.SetAttribute(rx, ry, TileAttribute.Blocked);
        }

        return map;
    }

    private static void Fill(bool[,] grid, int x0, int y0, int x1, int y1)
    {
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
            grid[x, y] = true;
    }

    /// <summary>Lays an autotiled terrain over the cells marked in <paramref name="grid"/>.</summary>
    private static void Paint(MapData map, bool[,] grid, int tileset, MapLayer layer, Random rng)
    {
        bool Has(int x, int y) => !map.InBounds(x, y) || grid[x, y];

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
            if (grid[x, y]) map.SetTile(layer, x, y, TileRef.Make(tileset, Autotile.Resolve(Has, x, y, rng)));
    }

    /// <summary>
    /// A 3x3 trunk block with its top-left at (x, y) and the canopy above it, overlapping the trunk
    /// by one row. The cells under the trunk are blocked.
    /// </summary>
    private static void Tree(MapData map, int x, int y, bool pine, MapLayer canopyLayer)
    {
        var canopyRows = pine ? 4 : 3;
        var canopyRow0 = pine ? 3 : 0;
        var column0 = pine ? 0 : 3;

        for (var row = 0; row < 3; row++)
        for (var col = 0; col < 3; col++)
            Set(map, MapLayer.Mask2, x + col, y + row, TileRef.Make(Trunk, row * SheetColumns + column0 + col));

        for (var row = 0; row < canopyRows; row++)
        for (var col = 0; col < 3; col++)
            Set(map, canopyLayer, x + col, y - canopyRows + 1 + row, TileRef.Make(Treetop, (canopyRow0 + row) * SheetColumns + column0 + col));

        if (map.InBounds(x + 1, y + 1)) map.SetAttribute(x + 1, y + 1, TileAttribute.Blocked);
        if (map.InBounds(x + 1, y + 2)) map.SetAttribute(x + 1, y + 2, TileAttribute.Blocked);
    }

    private static void Set(MapData map, MapLayer layer, int x, int y, int tile)
    {
        if (map.InBounds(x, y)) map.SetTile(layer, x, y, tile);
    }
}

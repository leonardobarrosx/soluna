using Soluna.Shared;

namespace Soluna.Server;

/// <summary>
/// Builds a forest map out of pieces of an existing map (Pipoya's village): dense blocks of its forest
/// line the edges and its small trees are scattered over grass, leaving a clear corridor up the middle
/// that lines up with the village's north road. Pieces are copied with their collision, so the result
/// looks and behaves like the source art.
/// </summary>
internal static class ForestMap
{
    private const int Width = 40, Height = 30;
    private const int Block = 6;

    // A corridor with no trees, centred on the village's north road (x 23-25), up to a clearing.
    private const int RoadX = 21, RoadWidth = 7;

    public static MapData Build(MapData village, int id)
    {
        var map = MapData.CreateEmpty(id, "Bosque ao Norte", Width, Height, village.Layers.Length);
        map.Tilesets.AddRange(village.Tilesets);
        map.FringeFrom = village.FringeFrom;
        map.LayerNames = village.LayerNames;
        var rng = new Random(11);

        var floor = MostCommon(village.Layers[0]);
        Array.Fill(map.Layers[0], floor);

        var tree = Array.FindIndex(village.LayerNames ?? [], n => n.Contains("tree", StringComparison.OrdinalIgnoreCase));
        var reserved = new bool[Width * Height];

        Reserve(reserved, RoadX, 8, RoadWidth, Height - 8);
        Reserve(reserved, RoadX - 6, 3, RoadWidth + 12, 7);

        // Dense forest around the edges, from blocks of the village that are mostly trees.
        var blocks = DenseBlocks(village, tree).ToList();
        if (blocks.Count > 0)
        {
            for (var x = 0; x < Width; x += Block)
            for (var y = 0; y < Height; y += Block)
            {
                var edge = x < Block || y < Block || x >= Width - Block || y >= Height - Block;
                if (!edge || Overlaps(reserved, x, y, Block, Block)) continue;
                var (bx, by) = blocks[rng.Next(blocks.Count)];
                Stamp(village, map, bx, by, Block, Block, x, y);
                Reserve(reserved, x, y, Block, Block);
            }
        }

        // Single trees scattered inside, with a tile of space around each.
        var singles = SmallTrees(village, tree).ToList();
        for (var attempt = 0; attempt < 400 && singles.Count > 0; attempt++)
        {
            var (sx, sy, w, h) = singles[rng.Next(singles.Count)];
            var x = rng.Next(1, Width - w - 1);
            var y = rng.Next(1, Height - h - 1);
            if (Overlaps(reserved, x - 1, y - 1, w + 2, h + 2)) continue;
            Stamp(village, map, sx, sy, w, h, x, y, onlyLayer: tree);
            Reserve(reserved, x, y, w, h);
        }

        (map.SpawnX, map.SpawnY) = (RoadX + RoadWidth / 2, Height - 2);
        return map;
    }

    /// <summary>Copies a rectangle of every layer (or one), with its collision, from one map into another.</summary>
    private static void Stamp(MapData from, MapData to, int sx, int sy, int w, int h, int dx, int dy, int onlyLayer = -1)
    {
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (!from.InBounds(sx + x, sy + y) || !to.InBounds(dx + x, dy + y)) continue;
            var src = from.Index(sx + x, sy + y);
            var dst = to.Index(dx + x, dy + y);
            if (onlyLayer >= 0)
            {
                if (from.Layers[onlyLayer][src] == TileRef.Empty) continue;
                to.Layers[onlyLayer][dst] = from.Layers[onlyLayer][src];
            }
            else
            {
                for (var layer = 0; layer < from.Layers.Length; layer++) to.Layers[layer][dst] = from.Layers[layer][src];
            }
            to.Attributes[dst] = from.GetAttribute(sx + x, sy + y) == TileAttribute.Blocked ? (byte)TileAttribute.Blocked : (byte)0;
        }
    }

    /// <summary>Block-sized pieces of the source that are at least 70% trees, with no buildings or water.</summary>
    private static IEnumerable<(int x, int y)> DenseBlocks(MapData village, int tree)
    {
        if (tree < 0) yield break;
        var other = Enumerable.Range(0, village.Layers.Length)
            .Where(l => l != 0 && l != tree && village.LayerNames?[l] is { } n && !n.Contains("grass", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        for (var y = 0; y + Block <= village.Height; y += 2)
        for (var x = 0; x + Block <= village.Width; x += 2)
        {
            int trees = 0, clutter = 0;
            for (var j = 0; j < Block; j++)
            for (var i = 0; i < Block; i++)
            {
                var c = village.Index(x + i, y + j);
                if (village.Layers[tree][c] != TileRef.Empty) trees++;
                if (other.Any(l => village.Layers[l][c] != TileRef.Empty)) clutter++;
            }
            if (clutter == 0 && trees >= Block * Block * 7 / 10) yield return (x, y);
        }
    }

    /// <summary>Lone trees in the source: small groups of tree tiles with nothing of the tree layer around them.</summary>
    private static IEnumerable<(int x, int y, int w, int h)> SmallTrees(MapData village, int tree)
    {
        if (tree < 0) yield break;
        var seen = new bool[village.Width * village.Height];
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || village.Layers[tree][start] == TileRef.Empty) continue;
            var cells = new List<int>();
            var stack = new Stack<int>([start]);
            seen[start] = true;
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                cells.Add(c);
                var (x, y) = (c % village.Width, c / village.Width);
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                {
                    if (!village.InBounds(nx, ny)) continue;
                    var n = village.Index(nx, ny);
                    if (seen[n] || village.Layers[tree][n] == TileRef.Empty) continue;
                    seen[n] = true;
                    stack.Push(n);
                }
            }
            var xs = cells.Select(c => c % village.Width).ToArray();
            var ys = cells.Select(c => c / village.Width).ToArray();
            var (w, h) = (xs.Max() - xs.Min() + 1, ys.Max() - ys.Min() + 1);
            if (cells.Count >= 2 && w <= 3 && h <= 4) yield return (xs.Min(), ys.Min(), w, h);
        }
    }

    private static void Reserve(bool[] reserved, int x, int y, int w, int h)
    {
        for (var j = y; j < y + h; j++)
        for (var i = x; i < x + w; i++)
            if (i >= 0 && j >= 0 && i < Width && j < Height) reserved[j * Width + i] = true;
    }

    private static bool Overlaps(bool[] reserved, int x, int y, int w, int h)
    {
        for (var j = y; j < y + h; j++)
        for (var i = x; i < x + w; i++)
            if (i >= 0 && j >= 0 && i < Width && j < Height && reserved[j * Width + i]) return true;
        return false;
    }

    private static int MostCommon(int[] tiles) =>
        tiles.Where(t => t != TileRef.Empty).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(TileRef.Empty);
}

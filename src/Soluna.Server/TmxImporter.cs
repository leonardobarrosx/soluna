using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Soluna.Shared;

namespace Soluna.Server;

/// <summary>
/// Turns a Tiled map (.tmx, orthogonal, 32x32 tiles) into a Soluna map. Tilesets may be embedded
/// or external (.tsx) and must sit under assets/tilesets so clients can load them by path.
///
/// Layers keep their order, except that layers meant to be drawn over characters move to the end:
/// those with a custom property above=true, or whose name says so (up, above, fringe, roof, top, tree).
///
/// Collision comes from layers with a custom property collision=true when there are any; otherwise
/// a heuristic: water blocks unless something is built over it (a bridge), buildings and trees block.
/// Fix the rest in the editor with B.
/// </summary>
internal static partial class TmxImporter
{
    private const uint FlipFlags = 0xF0000000;

    [GeneratedRegex(@"(^|[_\s-])(up|above|fringe|roof|top)$|tree", RegexOptions.IgnoreCase)]
    private static partial Regex AboveName();

    [GeneratedRegex("water", RegexOptions.IgnoreCase)]
    private static partial Regex WaterName();

    [GeneratedRegex("^(building|house|wall|obstacle)", RegexOptions.IgnoreCase)]
    private static partial Regex BuildingName();

    [GeneratedRegex("tree", RegexOptions.IgnoreCase)]
    private static partial Regex TreeName();

    private sealed record Layer(string Name, int[] Gids, bool Above, bool Collision);

    public static MapData Import(string tmxPath, int id, string name, string tilesetRoot)
    {
        var tmxDir = Path.GetDirectoryName(Path.GetFullPath(tmxPath))!;
        var doc = XDocument.Load(tmxPath).Root ?? throw new InvalidDataException("Empty TMX.");
        if ((string?)doc.Attribute("orientation") != "orthogonal")
            throw new InvalidDataException("Only orthogonal maps are supported.");
        if ((int?)doc.Attribute("tilewidth") != Constants.TileSize || (int?)doc.Attribute("tileheight") != Constants.TileSize)
            throw new InvalidDataException($"Tiles must be {Constants.TileSize}x{Constants.TileSize}.");

        var width = (int)doc.Attribute("width")!;
        var height = (int)doc.Attribute("height")!;

        // Tilesets: first gid and the image path relative to assets/tilesets.
        var tilesets = new List<(int firstGid, string image)>();
        foreach (var ts in doc.Elements("tileset"))
        {
            var firstGid = (int)ts.Attribute("firstgid")!;
            var element = ts;
            var baseDir = tmxDir;
            if (ts.Attribute("source") is { } source)
            {
                var tsxPath = Path.Combine(tmxDir, source.Value);
                element = XDocument.Load(tsxPath).Root!;
                baseDir = Path.GetDirectoryName(tsxPath)!;
            }
            var image = element.Element("image") ?? throw new InvalidDataException($"Tileset at gid {firstGid} has no single image.");
            var full = Path.GetFullPath(Path.Combine(baseDir, (string)image.Attribute("source")!));
            var relative = Path.GetRelativePath(tilesetRoot, full);
            if (relative.StartsWith("..")) throw new InvalidDataException($"Tileset image {full} is outside {tilesetRoot}.");

            var columns = (int?)element.Attribute("columns");
            var imageWidth = (int?)image.Attribute("width");
            if (columns != null && imageWidth != null && imageWidth / Constants.TileSize != columns)
                Log.Warn($"{relative}: {columns} columns declared, image is {imageWidth / Constants.TileSize} tiles wide.");

            tilesets.Add((firstGid, relative.Replace('\\', '/')));
        }
        tilesets.Sort((a, b) => a.firstGid.CompareTo(b.firstGid));

        // Tile layers in document order, groups flattened.
        var layers = new List<Layer>();
        foreach (var element in doc.Descendants("layer"))
        {
            var layerName = (string?)element.Attribute("name") ?? $"layer{layers.Count + 1}";
            var props = Properties(element);
            var above = props.TryGetValue("above", out var a) ? a == "true" : AboveName().IsMatch(layerName);
            var collision = props.TryGetValue("collision", out var c) && c == "true";
            layers.Add(new Layer(layerName, ReadData(element.Element("data")!, width * height), above, collision));
        }
        if (layers.Count == 0) throw new InvalidDataException("The map has no tile layers.");
        if (layers.Count > MapData.MaxLayers) throw new InvalidDataException($"At most {MapData.MaxLayers} layers.");

        var ordered = layers.Where(l => !l.Above).Concat(layers.Where(l => l.Above)).ToList();
        var map = MapData.CreateEmpty(id, name, width, height, ordered.Count);
        map.Tilesets.AddRange(tilesets.Select(t => t.image));
        map.FringeFrom = ordered.Count(l => !l.Above);
        map.LayerNames = ordered.Select(l => l.Name).ToArray();

        var flipped = 0;
        for (var layer = 0; layer < ordered.Count; layer++)
        {
            var gids = ordered[layer].Gids;
            for (var i = 0; i < gids.Length; i++)
            {
                var raw = (uint)gids[i];
                if ((raw & FlipFlags) != 0) flipped++;
                var gid = (int)(raw & ~FlipFlags);
                if (gid == 0) continue;

                var set = tilesets.FindLastIndex(t => t.firstGid <= gid);
                if (set < 0) continue;
                map.Layers[layer][i] = TileRef.Make(set, gid - tilesets[set].firstGid);
            }
        }
        if (flipped > 0) Log.Warn($"{flipped} flipped or rotated tiles were imported unflipped.");

        ApplyCollision(map, ordered);

        var props2 = Properties(doc);
        if (props2.TryGetValue("spawn", out var spawn) && spawn.Split(',') is [var sx, var sy]
            && int.TryParse(sx, out var x) && int.TryParse(sy, out var y))
        {
            map.SpawnX = x;
            map.SpawnY = y;
        }

        Log.Info($"Imported {Path.GetFileName(tmxPath)}: {width}x{height}, {ordered.Count} layers ({ordered.Count - map.FringeFrom} above), {tilesets.Count} tilesets.");
        return map;
    }

    private static void ApplyCollision(MapData map, List<Layer> layers)
    {
        bool Has(Func<Layer, bool> which, int i) => layers.Any(l => which(l) && l.Gids[i] != 0);

        var cells = map.Width * map.Height;
        var marked = layers.Any(l => l.Collision);
        var water = new bool[cells];
        var built = new bool[cells];
        var tree = new bool[cells];
        for (var i = 0; i < cells; i++)
        {
            water[i] = Has(l => WaterName().IsMatch(l.Name) && !l.Name.Contains("grass", StringComparison.OrdinalIgnoreCase), i);
            built[i] = Has(l => BuildingName().IsMatch(l.Name) && !l.Above, i);
            tree[i] = Has(l => TreeName().IsMatch(l.Name), i);
        }

        // Something built over water is a bridge or a pier, and so are the built tiles that lead
        // onto it from the shore (a couple of steps out), so those stay walkable.
        var bridge = new bool[cells];
        var frontier = new Queue<(int i, int depth)>();
        for (var i = 0; i < cells; i++)
        {
            if (!water[i] || !built[i]) continue;
            bridge[i] = true;
            frontier.Enqueue((i, 0));
        }
        while (frontier.Count > 0)
        {
            var (i, depth) = frontier.Dequeue();
            if (depth == 2) continue;
            var (x, y) = (i % map.Width, i / map.Width);
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (!map.InBounds(nx, ny)) continue;
                var n = map.Index(nx, ny);
                if (bridge[n] || !built[n]) continue;
                bridge[n] = true;
                frontier.Enqueue((n, depth + 1));
            }
        }

        var blocked = 0;
        for (var i = 0; i < cells; i++)
        {
            var block = marked
                ? Has(l => l.Collision, i)
                : !bridge[i] && (water[i] || built[i] || tree[i]);
            if (!block) continue;
            map.Attributes[i] = (byte)TileAttribute.Blocked;
            blocked++;
        }
        Log.Info($"Collision: {blocked} blocked tiles ({(marked ? "from collision layers" : "heuristic")}).");
    }

    private static Dictionary<string, string> Properties(XElement element) =>
        element.Element("properties")?.Elements("property")
            .ToDictionary(p => (string)p.Attribute("name")!, p => ((string?)p.Attribute("value") ?? p.Value).Trim().ToLowerInvariant())
        ?? [];

    private static int[] ReadData(XElement data, int cells)
    {
        var encoding = (string?)data.Attribute("encoding");
        var compression = (string?)data.Attribute("compression");
        int[] gids;

        if (encoding == "csv")
        {
            gids = data.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => (int)uint.Parse(v)).ToArray();
        }
        else if (encoding == "base64")
        {
            var bytes = Convert.FromBase64String(data.Value.Trim());
            if (compression is "zlib" or "gzip")
            {
                using var input = new MemoryStream(bytes);
                using Stream unpack = compression == "zlib" ? new ZLibStream(input, CompressionMode.Decompress) : new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                unpack.CopyTo(output);
                bytes = output.ToArray();
            }
            else if (compression != null)
            {
                throw new InvalidDataException($"Unsupported layer compression '{compression}'.");
            }
            gids = new int[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, gids, 0, gids.Length * 4);
        }
        else
        {
            throw new InvalidDataException("Layer data must be CSV or base64 (Tiled's defaults).");
        }

        if (gids.Length != cells) throw new InvalidDataException($"Layer has {gids.Length} tiles, expected {cells}.");
        return gids;
    }
}

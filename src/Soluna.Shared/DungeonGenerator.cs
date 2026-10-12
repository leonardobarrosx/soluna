namespace Soluna.Shared;

public sealed class DungeonSettings
{
    public int FirstMapId { get; set; } = 1001;
    public string Name { get; set; } = "Masmorra";
    public string Tileset { get; set; } = "";
    public int FloorTile { get; set; }
    public int WallTile { get; set; } = -1;
    public int WallFaceTile { get; set; } = -1;
    public int[] FloorVariants { get; set; } = [];
    public int ExitTile { get; set; } = -1;
    public int DescentTile { get; set; } = -1;
    public int ChestTile { get; set; } = -1;
    public int ExtractionTile { get; set; } = -1;
    public List<DungeonRoomStyle> RoomStyles { get; set; } = [];
    public int[] Enemies { get; set; } = [];
    public int Boss { get; set; }
    public int BossEvery { get; set; } = 3;
    public string ExitName { get; set; } = "Saída";
    public string DescentName { get; set; } = "Escada";
    public string ChestName { get; set; } = "Baú";
    public string ExtractionName { get; set; } = "Veio";

    public List<string> Problems()
    {
        var errors = new List<string>();
        static bool Tile(int tile) => tile is >= -1 and <= 65535;
        if (FirstMapId < 1 || FirstMapId == int.MaxValue || BossEvery < 1 || Boss < 0
            || Enemies is not { Length: > 0 and <= 32 } || Enemies.Any(n => n < 1))
            errors.Add("A masmorra precisa de IDs válidos, inimigos e intervalo de chefe positivo.");
        if (string.IsNullOrWhiteSpace(Tileset) || FloorTile < 0 || !new[] { FloorTile, WallTile, WallFaceTile, ExitTile, DescentTile, ChestTile, ExtractionTile }.All(Tile)
            || FloorVariants is not { Length: <= 16 } || FloorVariants.Any(t => t < 0 || !Tile(t)))
            errors.Add("O tileset e os pisos da masmorra precisam ser válidos; use -1 para omitir uma decoração.");
        if (RoomStyles is not { Count: <= 16 }) { errors.Add("Use no máximo 16 estilos de sala."); return errors; }
        foreach (var style in RoomStyles)
        {
            if (style == null || style.MinDepth < 1 || !Tile(style.FloorTile) || !Tile(style.BorderTile)
                || style.Decorations is not { Count: <= 16 })
            { errors.Add("Estilo de sala inválido (profundidade, pisos ou quantidade de peças)."); continue; }
            foreach (var d in style.Decorations)
            {
                var p = d?.Prop;
                if (d == null || !Enum.IsDefined(d.Anchor) || d.OffsetX is < -32 or > 32 || d.OffsetY is < -32 or > 32
                    || p == null || string.IsNullOrWhiteSpace(p.Sprite) || p.Width is < 1 or > 12 || p.Height is < 1 or > 12
                    || p.Source == null || !(p.Source.Length == 0 || p.Source is [>= 0, >= 0, > 0, > 0])
                    || p.Solid == null || !(p.Solid.Length == 0 || p.Solid is [>= 0, >= 0, > 0, > 0]
                        && p.Solid[0] <= p.Width - p.Solid[2] && p.Solid[1] <= p.Height - p.Solid[3]))
                    errors.Add($"O estilo '{style.Name}' tem uma peça ou colisão inválida.");
            }
        }
        return errors;
    }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<RoomAnchor>))]
public enum RoomAnchor { NorthWest, NorthEast, SouthWest, SouthEast, Center }

public sealed class DungeonDecoration
{
    public RoomAnchor Anchor { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public MapProp Prop { get; set; } = new();
}

public sealed class DungeonRoomStyle
{
    public string Name { get; set; } = "Sala";
    public int MinDepth { get; set; } = 1;
    public int FloorTile { get; set; } = -1;
    public int BorderTile { get; set; } = -1;
    public List<DungeonDecoration> Decorations { get; set; } = [];
}

/// <summary>Connected rooms, seeded encounters and escalating depth, authored through each game's dungeon settings.</summary>
public static class DungeonGenerator
{
    public static MapData Build(DungeonSettings settings, int seed, int depth)
    {
        if (depth < 1 || depth > int.MaxValue - settings.FirstMapId)
            throw new ArgumentOutOfRangeException(nameof(depth));
        if (settings.Problems() is { Count: > 0 } errors)
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        var rng = new Random(unchecked(seed * 397 ^ depth * 7919));
        var map = MapData.CreateEmpty(settings.FirstMapId + depth - 1, $"{settings.Name} · {depth}", 60, 48);
        map.Depth = depth; map.Seed = seed;
        map.Tilesets.Add(settings.Tileset);
        Array.Fill(map.Attributes, (byte)TileAttribute.Blocked);
        Array.Fill(map.Layers[0], settings.WallTile);
        var rooms = new List<(int x, int y, int w, int h)>();
        for (var attempt = 0; rooms.Count < 9 && attempt < 500; attempt++)
        {
            var w = rng.Next(8, 12); var h = rng.Next(7, 11);
            var x = rng.Next(2, map.Width - w - 2); var y = rng.Next(2, map.Height - h - 2);
            if (rooms.Any(r => x < r.x + r.w + 2 && x + w + 2 > r.x && y < r.y + r.h + 2 && y + h + 2 > r.y)) continue;
            for (var ty = y; ty < y + h; ty++) for (var tx = x; tx < x + w; tx++) Carve(tx, ty);
            if (rooms.Count > 0)
            {
                var previous = rooms[^1];
                var (cx, cy) = (previous.x + previous.w / 2, previous.y + previous.h / 2);
                var (nx, ny) = (x + w / 2, y + h / 2);
                while (cx != nx) { Carve(cx, cy); Carve(cx, cy + 1); cx += Math.Sign(nx - cx); }
                while (cy != ny) { Carve(cx, cy); Carve(cx + 1, cy); cy += Math.Sign(ny - cy); }
                Carve(nx, ny);
            }
            rooms.Add((x, y, w, h));
        }
        var first = rooms[0]; var last = rooms[^1];
        (map.SpawnX, map.SpawnY) = (first.x + first.w / 2, first.y + first.h / 2);
        Add(LandmarkKind.Exit, settings.ExitName, map.SpawnX - 1, map.SpawnY, settings.ExitTile);
        Add(LandmarkKind.Descent, settings.DescentName, last.x + last.w / 2, last.y + last.h / 2, settings.DescentTile);
        for (var i = 1; i < rooms.Count; i++)
        {
            var r = rooms[i];
            var npc = i == rooms.Count - 1 && settings.Boss > 0 && depth % Math.Max(1, settings.BossEvery) == 0 ? settings.Boss : settings.Enemies[rng.Next(settings.Enemies.Length)];
            map.Npcs.Add(new NpcSpawn { NpcId = npc, X = r.x + r.w / 2, Y = r.y + r.h / 2 + 1 });
            if (i % 3 == 1) Add(LandmarkKind.Chest, settings.ChestName, r.x + r.w - 2, r.y + 1, settings.ChestTile);
            if (i == 2) Add(LandmarkKind.Extraction, settings.ExtractionName, r.x + r.w - 2, r.y + r.h - 2, settings.ExtractionTile);
        }
        // Composition uses a separate stream: changing scenery never moves encounters or corridors.
        var decor = new Random(unchecked(seed * 991 ^ depth * 313));
        var styles = settings.RoomStyles.Where(s => s.MinDepth <= depth).ToArray();
        var protectedTiles = map.Landmarks.Select(l => (l.X, l.Y)).Concat(map.Npcs.Select(n => (n.X, n.Y))).Append(map.Spawn).ToHashSet();
        foreach (var r in rooms)
        {
            if (styles.Length == 0) break;
            var style = styles[decor.Next(styles.Length)];
            for (var y = r.y; y < r.y + r.h; y++) for (var x = r.x; x < r.x + r.w; x++)
            {
                var border = x == r.x || y == r.y || x == r.x + r.w - 1 || y == r.y + r.h - 1;
                var tile = border && style.BorderTile >= 0 ? style.BorderTile : style.FloorTile;
                if (tile >= 0) map.SetTile(MapLayer.Ground, x, y, tile);
            }
            foreach (var placement in style.Decorations)
            {
                var p = placement.Prop;
                var x = placement.Anchor switch
                {
                    RoomAnchor.NorthEast or RoomAnchor.SouthEast => r.x + r.w - p.Width,
                    RoomAnchor.Center => r.x + (r.w - p.Width) / 2,
                    _ => r.x,
                };
                var y = placement.Anchor switch
                {
                    RoomAnchor.SouthWest or RoomAnchor.SouthEast => r.y + r.h - p.Height,
                    RoomAnchor.Center => r.y + (r.h - p.Height) / 2,
                    _ => r.y,
                };
                x += placement.OffsetX; y += placement.OffsetY;
                if (x < r.x || y < r.y || x + p.Width > r.x + r.w || y + p.Height > r.y + r.h) continue;
                var cells = Enumerable.Range(y, p.Height).SelectMany(yy => Enumerable.Range(x, p.Width).Select(xx => (xx, yy))).ToArray();
                if (cells.Any(c => !map.IsWalkable(c.xx, c.yy) || !p.Underfoot && protectedTiles.Contains(c))) continue;
                if (!p.Underfoot && map.Props.Any(q => !q.Underfoot && x < q.X + q.Width && x + p.Width > q.X && y < q.Y + q.Height && y + p.Height > q.Y)) continue;
                var solid = p.Solid.Length == 0 ? Array.Empty<(int x, int y)>() :
                    Enumerable.Range(y + p.Solid[1], p.Solid[3]).SelectMany(yy => Enumerable.Range(x + p.Solid[0], p.Solid[2]).Select(xx => (xx, yy))).ToArray();
                if (solid.Any(protectedTiles.Contains)) continue;
                foreach (var c in solid) map.SetAttribute(c.Item1, c.Item2, TileAttribute.Blocked);
                // Reject a footprint that closes a corridor or strands any walkable pocket.
                if (solid.Length > 0 && !Connected(map))
                {
                    foreach (var c in solid) map.SetAttribute(c.Item1, c.Item2, TileAttribute.None);
                    continue;
                }
                map.Props.Add(new MapProp { Sprite = p.Sprite, Source = [..p.Source], X = x, Y = y,
                    Width = p.Width, Height = p.Height, Solid = [..p.Solid], Underfoot = p.Underfoot });
            }
        }
        if (settings.WallFaceTile >= 0)
            for (var y = 0; y < map.Height - 1; y++) for (var x = 0; x < map.Width; x++)
                if (!map.IsWalkable(x, y) && map.IsWalkable(x, y + 1) && map.GetTile(MapLayer.Ground, x, y) == settings.WallTile)
                    map.SetTile(MapLayer.Ground, x, y, settings.WallFaceTile);
        return map;
        void Carve(int x, int y)
        {
            if (!map.InBounds(x, y)) return;
            map.SetAttribute(x, y, TileAttribute.None);
            var variant = rng.Next(8) == 0;
            map.SetTile(MapLayer.Ground, x, y, variant && settings.FloorVariants.Length > 0
                ? settings.FloorVariants[(x + y) % settings.FloorVariants.Length] : settings.FloorTile);
        }
        void Add(LandmarkKind kind, string name, int x, int y, int tile)
        {
            map.Landmarks.Add(new Landmark { Kind = kind, Name = name, X = x, Y = y });
            map.SetTile(MapLayer.Mask, x, y, tile);
            map.SetAttribute(x, y, TileAttribute.NpcAvoid);
        }
    }

    private static bool Connected(MapData map)
    {
        var seen = new HashSet<(int, int)>(); var queue = new Queue<(int x, int y)>(); queue.Enqueue(map.Spawn);
        while (queue.TryDequeue(out var p))
        {
            if (!map.IsWalkable(p.x, p.y) || !seen.Add(p)) continue;
            foreach (var dir in Enum.GetValues<Direction>()) { var d = dir.Delta(); queue.Enqueue((p.x + d.dx, p.y + d.dy)); }
        }
        return seen.Count == map.Attributes.Count(a => a != (byte)TileAttribute.Blocked);
    }

    public static NpcDef Scale(NpcDef source, int depth)
    {
        if (depth <= 0) return source;
        var scaled = source.Clone();
        var factor = 1 + (depth - 1) * .12;
        static int Stat(int n, double factor) => (int)Math.Clamp(n * factor, 1, 1_000_000);
        scaled.Hp = Stat(source.Hp, factor);
        scaled.Attack = Stat(source.Attack, 1 + (depth - 1) * .07);
        scaled.Defense = (int)Math.Min(500_000, source.Defense + (depth - 1) * .5);
        scaled.Exp = Stat(source.Exp, 1 + (depth - 1) * .1);
        return scaled;
    }
}

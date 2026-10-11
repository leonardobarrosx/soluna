namespace Soluna.Shared;

public sealed class DungeonSettings
{
    public int FirstMapId { get; set; } = 1001;
    public string Name { get; set; } = "Ruínas do Eclipse";
    public string Tileset { get; set; } = "soluna/atlas.png";
    public int FloorTile { get; set; } = 5;
    public int WallTile { get; set; } = 7;
    public int[] Enemies { get; set; } = [10, 11, 12];
    public int Boss { get; set; } = 99;
    public int BossEvery { get; set; } = 3;
}

/// <summary>Connected rooms, seeded encounters and escalating depth, authored through each game's dungeon settings.</summary>
public static class DungeonGenerator
{
    public static MapData Build(DungeonSettings settings, int seed, int depth)
    {
        if (depth < 1 || depth > int.MaxValue - settings.FirstMapId || settings.Enemies.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(depth));
        var rng = new Random(unchecked(seed * 397 ^ depth * 7919));
        var map = MapData.CreateEmpty(settings.FirstMapId + depth - 1, $"{settings.Name} · {depth}", 52, 42);
        map.Depth = depth; map.Seed = seed;
        map.Tilesets.Add(settings.Tileset);
        Array.Fill(map.Attributes, (byte)TileAttribute.Blocked);
        Array.Fill(map.Layers[0], settings.WallTile);
        var rooms = new List<(int x, int y, int w, int h)>();
        for (var attempt = 0; rooms.Count < 9 && attempt < 500; attempt++)
        {
            var w = rng.Next(6, 10); var h = rng.Next(5, 9);
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
        Add(LandmarkKind.Exit, "Fio de retorno", map.SpawnX - 1, map.SpawnY, 102);
        Add(LandmarkKind.Descent, "Escada selada", last.x + last.w / 2, last.y + last.h / 2, 103);
        for (var i = 1; i < rooms.Count; i++)
        {
            var r = rooms[i];
            var npc = i == rooms.Count - 1 && depth % Math.Max(1, settings.BossEvery) == 0 ? settings.Boss : settings.Enemies[rng.Next(settings.Enemies.Length)];
            map.Npcs.Add(new NpcSpawn { NpcId = npc, X = r.x + 2, Y = r.y + 2 });
            if (i % 3 == 1) Add(LandmarkKind.Chest, "Relicário esquecido", r.x + r.w - 2, r.y + 1, 97);
            if (i == 2) Add(LandmarkKind.Extraction, "Veio de ecos", r.x + r.w - 2, r.y + r.h - 2, 105);
        }
        return map;
        void Carve(int x, int y)
        {
            if (!map.InBounds(x, y)) return;
            map.SetAttribute(x, y, TileAttribute.None);
            map.SetTile(MapLayer.Ground, x, y, rng.Next(8) == 0 ? 6 : settings.FloorTile);
        }
        void Add(LandmarkKind kind, string name, int x, int y, int tile)
        {
            map.Landmarks.Add(new Landmark { Kind = kind, Name = name, X = x, Y = y });
            map.SetTile(MapLayer.Mask, x, y, tile);
            map.SetAttribute(x, y, TileAttribute.NpcAvoid);
        }
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

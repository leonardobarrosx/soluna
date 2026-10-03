using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;

namespace Soluna.Server;

/// <summary>An NPC alive (or waiting to respawn) on a map.</summary>
internal sealed class NpcInstance
{
    public required int Index { get; init; }
    public required NpcDef Def { get; init; }
    public required NpcSpawn Spawn { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public Direction Dir { get; set; }
    public int Hp { get; set; }
    public bool Alive { get; set; }
    public long RespawnAtMs { get; set; }
    public long NextMoveMs { get; set; }
    public long NextAttackMs { get; set; }

    /// <summary>Id of the player being chased, 0 for none.</summary>
    public int Target { get; set; }
}

/// <summary>What is happening on a map right now: its NPCs. Built when a player first arrives.</summary>
internal sealed class MapState
{
    public List<NpcInstance> Npcs { get; } = [];
}

internal sealed partial class GameServer
{
    private const int RegenEveryMs = 2000;
    private const int OutOfCombatMs = 5000;
    private const int WanderRadius = 6;

    private readonly Dictionary<int, MapState> _states = [];
    private readonly Random _rng = new();
    private long _lastRegenMs;

    // ---- Map state ----

    private MapState StateFor(MapData map)
    {
        if (_states.TryGetValue(map.Id, out var state)) return state;
        state = new MapState();
        foreach (var spawn in map.Npcs)
        {
            if (_npcs.Get(spawn.NpcId) is not { } def) continue;
            var npc = new NpcInstance { Index = state.Npcs.Count, Def = def, Spawn = spawn };
            Respawn(map, state, npc, announce: false);
            state.Npcs.Add(npc);
        }
        _states[map.Id] = state;
        return state;
    }

    /// <summary>Throws the map's NPCs away and spawns them again, after its spawn list changed.</summary>
    private void ResetState(MapData map)
    {
        if (_states.Remove(map.Id, out var old))
        {
            foreach (var npc in old.Npcs.Where(n => n.Alive)) Broadcast(map.Id, NpcRemoved(npc));
        }
        var state = StateFor(map);
        foreach (var npc in state.Npcs.Where(n => n.Alive)) Broadcast(map.Id, NpcSpawned(npc));
    }

    private void Respawn(MapData map, MapState state, NpcInstance npc, bool announce)
    {
        var (x, y) = map.NearestWalkable(npc.Spawn.X, npc.Spawn.Y, (tx, ty) => NpcCanStand(map, state, tx, ty, npc));
        (npc.X, npc.Y, npc.Dir) = (x, y, Direction.Down);
        npc.Hp = npc.Def.Hp;
        npc.Alive = true;
        npc.Target = 0;
        npc.NextMoveMs = _clock.ElapsedMilliseconds + _rng.Next(npc.Def.MoveMs);
        if (announce) Broadcast(map.Id, NpcSpawned(npc));
    }

    private void SendNpcs(Player player, MapData map)
    {
        foreach (var npc in StateFor(map).Npcs.Where(n => n.Alive)) player.Peer.Send(NpcSpawned(npc), DeliveryMethod.ReliableOrdered);
    }

    private bool NpcCanStand(MapData map, MapState state, int x, int y, NpcInstance? self = null)
    {
        if (!map.IsWalkable(x, y)) return false;
        var attribute = map.GetAttribute(x, y);
        if (attribute is TileAttribute.NpcAvoid or TileAttribute.Warp) return false;
        if (state.Npcs.Any(n => n.Alive && n != self && n.X == x && n.Y == y)) return false;
        return !PlayersOn(map.Id).Any(p => p.X == x && p.Y == y);
    }

    // ---- The world over time ----

    /// <summary>NPC thinking on every map someone is on, respawns and regeneration. Runs each tick.</summary>
    private void TickWorld()
    {
        var now = _clock.ElapsedMilliseconds;
        foreach (var mapId in Players.Select(p => p.MapId).Distinct().ToList())
        {
            var map = _maps.Get(mapId);
            var state = StateFor(map);
            foreach (var npc in state.Npcs)
            {
                if (!npc.Alive)
                {
                    if (now >= npc.RespawnAtMs) Respawn(map, state, npc, announce: true);
                    continue;
                }
                Think(map, state, npc, now);
            }
        }

        if (now - _lastRegenMs < RegenEveryMs) return;
        _lastRegenMs = now;
        foreach (var player in Players.ToList()) Regenerate(player, now);
    }

    private void Think(MapData map, MapState state, NpcInstance npc, long now)
    {
        var target = npc.Target != 0 ? PlayersOn(map.Id).FirstOrDefault(p => p.Id == npc.Target) : null;
        if (target != null && Distance(npc, target) > npc.Def.Range * 2) target = null;

        if (target == null && npc.Def.Behaviour == NpcBehaviour.Aggressive)
        {
            target = PlayersOn(map.Id)
                .Where(p => Distance(npc, p) <= npc.Def.Range)
                .OrderBy(p => Distance(npc, p))
                .FirstOrDefault();
        }
        npc.Target = target?.Id ?? 0;

        if (target != null && Distance(npc, target) == 1)
        {
            npc.Dir = Toward(npc.X, npc.Y, target.X, target.Y);
            if (now < npc.NextAttackMs) return;
            npc.NextAttackMs = now + npc.Def.AttackMs;
            Broadcast(map.Id, Attacked(UnitKind.Npc, npc.Index, npc.Dir));
            HurtPlayer(target, Formulas.Damage(npc.Def.Attack, target.DefensePower(_items), _rng), npc.Def.Name);
            return;
        }

        if (now < npc.NextMoveMs) return;
        Direction? step = null;
        if (target != null)
        {
            npc.NextMoveMs = now + npc.Def.MoveMs * 3 / 4;
            step = StepToward(map, state, npc, target.X, target.Y);
        }
        else
        {
            npc.NextMoveMs = now + npc.Def.MoveMs + _rng.Next(npc.Def.MoveMs);
            if (_rng.Next(3) == 0)
            {
                var dir = (Direction)_rng.Next(4);
                var (dx, dy) = dir.Delta();
                var home = Math.Abs(npc.X + dx - npc.Spawn.X) + Math.Abs(npc.Y + dy - npc.Spawn.Y) <= WanderRadius;
                if (home && NpcCanStand(map, state, npc.X + dx, npc.Y + dy, npc)) step = dir;
            }
        }

        if (step is not { } go) return;
        var (sx, sy) = go.Delta();
        (npc.X, npc.Y, npc.Dir) = (npc.X + sx, npc.Y + sy, go);
        var moved = PacketIO.Begin(PacketType.NpcMoved);
        moved.Put(npc.Index);
        moved.Put(npc.X);
        moved.Put(npc.Y);
        moved.Put((byte)npc.Dir);
        Broadcast(map.Id, moved);
    }

    /// <summary>A step that closes the distance, along the longer axis first, or null when boxed in.</summary>
    private Direction? StepToward(MapData map, MapState state, NpcInstance npc, int tx, int ty)
    {
        var dx = Math.Sign(tx - npc.X);
        var dy = Math.Sign(ty - npc.Y);
        var horizontal = dx != 0 ? (dx > 0 ? Direction.Right : Direction.Left) : (Direction?)null;
        var vertical = dy != 0 ? (dy > 0 ? Direction.Down : Direction.Up) : (Direction?)null;
        var order = Math.Abs(tx - npc.X) >= Math.Abs(ty - npc.Y) ? new[] { horizontal, vertical } : [vertical, horizontal];
        foreach (var dir in order)
        {
            if (dir is not { } d) continue;
            var (sx, sy) = d.Delta();
            if (NpcCanStand(map, state, npc.X + sx, npc.Y + sy, npc)) return d;
        }
        return null;
    }

    private void Regenerate(Player player, long now)
    {
        var map = _maps.Get(player.MapId);
        var onHeal = map.GetAttribute(player.X, player.Y) == TileAttribute.Heal;
        var resting = now - player.LastCombatMs > OutOfCombatMs;
        if (!onHeal && !resting) return;

        var share = onHeal ? 0.2 : 0.05;
        var hp = Math.Min(player.MaxHp, player.Hp + Math.Max(1, (int)(player.MaxHp * share)));
        var mp = Math.Min(player.MaxMp, player.Mp + Math.Max(1, (int)(player.MaxMp * share)));
        if (hp == player.Hp && mp == player.Mp) return;

        var gained = hp - player.Hp;
        (player.Hp, player.Mp) = (hp, mp);
        SendVitals(player);
        if (gained > 0) Broadcast(player.MapId, HpChanged(UnitKind.Player, player.Id, player.Hp, player.MaxHp, gained));
    }

    // ---- Fighting ----

    private void HandleAttack(Player player, NetDataReader reader)
    {
        var dir = (Direction)reader.GetByte();
        if (!Enum.IsDefined(dir)) return;
        var now = _clock.ElapsedMilliseconds;
        if (now < player.NextAttackMs) return;
        player.NextAttackMs = now + Formulas.PlayerAttackMs - 50; // a little slack for jitter
        player.Dir = dir;
        player.LastCombatMs = now;
        Broadcast(player.MapId, Attacked(UnitKind.Player, player.Id, dir), except: player);

        var map = _maps.Get(player.MapId);
        var (dx, dy) = dir.Delta();
        var (tx, ty) = (player.X + dx, player.Y + dy);

        if (StateFor(map).Npcs.FirstOrDefault(n => n.Alive && n.X == tx && n.Y == ty) is { } npc)
        {
            if (!npc.Def.Hostile) return;
            npc.Target = player.Id;
            HurtNpc(map, npc, Formulas.Damage(player.AttackPower(_items), npc.Def.Defense, _rng), player);
            return;
        }

        if (map.Moral == MapMoral.Pvp && PlayersOn(map.Id).FirstOrDefault(p => p.X == tx && p.Y == ty) is { } victim)
        {
            victim.LastCombatMs = now;
            HurtPlayer(victim, Formulas.Damage(player.AttackPower(_items), victim.DefensePower(_items), _rng), player.Name);
        }
    }

    private void HurtNpc(MapData map, NpcInstance npc, int damage, Player by)
    {
        npc.Hp = Math.Max(0, npc.Hp - damage);
        Broadcast(map.Id, HpChanged(UnitKind.Npc, npc.Index, npc.Hp, npc.Def.Hp, -damage));
        if (npc.Hp > 0) return;

        npc.Alive = false;
        npc.Target = 0;
        npc.RespawnAtMs = _clock.ElapsedMilliseconds + npc.Def.RespawnSeconds * 1000L;
        Broadcast(map.Id, NpcRemoved(npc));
        if (npc.Def.Exp > 0)
        {
            Tell(by, $"Você derrotou {npc.Def.Name}. +{npc.Def.Exp} XP");
            GainExp(by, npc.Def.Exp);
        }
    }

    private void HurtPlayer(Player player, int damage, string by)
    {
        player.Hp = Math.Max(0, player.Hp - damage);
        player.LastCombatMs = _clock.ElapsedMilliseconds;
        Broadcast(player.MapId, HpChanged(UnitKind.Player, player.Id, player.Hp, player.MaxHp, -damage));
        SendVitals(player);
        if (player.Hp > 0) return;

        BroadcastChat(player.MapId, "", $"{player.Name} foi derrotado por {by}.");
        Log.Info($"{player.Name} was defeated by {by}.");
        (player.Hp, player.Mp) = (player.MaxHp, player.MaxMp);
        var home = _maps.Get(MapStore.StartMapId);
        Transfer(player, home.Id, home.Spawn.x, home.Spawn.y);
        SendVitals(player);
        Tell(player, "Você acordou na vila.");
    }

    private void GainExp(Player player, int exp)
    {
        player.Exp += exp;
        while (player.Level < Formulas.MaxLevel && player.Exp >= Formulas.ExpToNext(player.Level))
        {
            player.Exp -= Formulas.ExpToNext(player.Level);
            player.Level++;
            (player.Hp, player.Mp) = (player.MaxHp, player.MaxMp);
            BroadcastChat(player.MapId, "", $"{player.Name} chegou ao nível {player.Level}!");
            Log.Info($"{player.Name} reached level {player.Level}.");
        }
        if (player.Level >= Formulas.MaxLevel) player.Exp = 0;
        SendVitals(player);
    }

    private static void SendVitals(Player player)
    {
        var w = PacketIO.Begin(PacketType.Vitals);
        w.Put(player.Hp);
        w.Put(player.MaxHp);
        w.Put(player.Mp);
        w.Put(player.MaxMp);
        w.Put(player.Level);
        w.Put(player.Exp);
        w.Put(Formulas.ExpToNext(player.Level));
        player.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    // ---- Admin: NPC spawns ----

    /// <summary>/npcs lists definitions · /npc &lt;id&gt; spawns one where you stand · /npc remover takes away the nearest spawn.</summary>
    private void NpcCommand(Player player, string[] parts)
    {
        var map = _maps.Get(player.MapId);
        if (parts.Length > 1 && parts[1] == "remover")
        {
            var nearest = map.Npcs.OrderBy(s => Math.Abs(s.X - player.X) + Math.Abs(s.Y - player.Y)).FirstOrDefault();
            if (nearest == null || Math.Abs(nearest.X - player.X) + Math.Abs(nearest.Y - player.Y) > 3)
            {
                Tell(player, "Nenhum ponto de NPC perto de você.");
                return;
            }
            map.Npcs.Remove(nearest);
        }
        else if (parts.Length > 1 && int.TryParse(parts[1], out var id) && _npcs.Get(id) is { } def)
        {
            map.Npcs.Add(new NpcSpawn { NpcId = id, X = player.X, Y = player.Y });
            Tell(player, $"{def.Name} vai aparecer aqui.");
        }
        else
        {
            Tell(player, "Use /npc <id> ou /npc remover. NPCs: " + string.Join(", ", _npcs.All.Select(n => $"{n.Id} {n.Name}")));
            return;
        }
        _maps.Save(map);
        AnnounceRevision(map);
        ResetState(map);
    }

    /// <summary>
    /// First run with Pipoya's maps: villagers in the village, monsters in the forest. Only when the map
    /// has no NPC spawns yet and the NPCs exist, so nothing an admin placed is touched.
    /// </summary>
    private void SeedDemoNpcs(MapData map)
    {
        if (map.Npcs.Count > 0) return;
        (int id, int count)[] plan = map.Name switch
        {
            "Vila de Soluna" => [(1, 3), (2, 3), (3, 2), (4, 1), (5, 1), (6, 1), (7, 1), (8, 1)],
            "Bosque ao Norte" => [(10, 4), (11, 3), (12, 2), (13, 2), (14, 2), (15, 1), (16, 1)],
            _ => [],
        };
        var rng = new Random(map.Id * 31);
        var (sx, sy) = map.Spawn;
        foreach (var (id, count) in plan)
        {
            if (_npcs.Get(id) is not { } def) continue;
            for (var i = 0; i < count; i++)
            {
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    // Friendly folk stay near the spawn; monsters keep away from where players arrive.
                    var (x, y) = def.Hostile
                        ? (rng.Next(2, map.Width - 2), rng.Next(2, map.Height * 2 / 3))
                        : (sx + rng.Next(-12, 13), sy + rng.Next(-10, 11));
                    if (!map.IsWalkable(x, y) || map.GetAttribute(x, y) != TileAttribute.None) continue;
                    if (map.Npcs.Any(n => n.X == x && n.Y == y) || Math.Abs(x - sx) + Math.Abs(y - sy) < 3) continue;
                    map.Npcs.Add(new NpcSpawn { NpcId = id, X = x, Y = y });
                    break;
                }
            }
        }
        if (map.Npcs.Count == 0) return;
        _maps.Save(map);
        Log.Info($"Placed {map.Npcs.Count} NPCs on map {map.Id} '{map.Name}'.");
    }

    // ---- Packets ----

    private static int Distance(NpcInstance npc, Player p) => Math.Abs(npc.X - p.X) + Math.Abs(npc.Y - p.Y);

    private static Direction Toward(int x, int y, int tx, int ty) =>
        Math.Abs(tx - x) >= Math.Abs(ty - y)
            ? (tx > x ? Direction.Right : Direction.Left)
            : (ty > y ? Direction.Down : Direction.Up);

    private static NetDataWriter NpcSpawned(NpcInstance npc)
    {
        var w = PacketIO.Begin(PacketType.NpcSpawned);
        w.Put(npc.Index);
        w.Put(npc.Def.Id);
        w.Put(npc.X);
        w.Put(npc.Y);
        w.Put((byte)npc.Dir);
        w.Put(npc.Hp);
        w.Put(npc.Def.Hp);
        return w;
    }

    private static NetDataWriter NpcRemoved(NpcInstance npc)
    {
        var w = PacketIO.Begin(PacketType.NpcRemoved);
        w.Put(npc.Index);
        return w;
    }

    private static NetDataWriter HpChanged(UnitKind kind, int id, int hp, int maxHp, int change)
    {
        var w = PacketIO.Begin(PacketType.HpChanged);
        w.Put((byte)kind);
        w.Put(id);
        w.Put(hp);
        w.Put(maxHp);
        w.Put(change);
        return w;
    }

    private static NetDataWriter Attacked(UnitKind kind, int id, Direction dir)
    {
        var w = PacketIO.Begin(PacketType.Attacked);
        w.Put((byte)kind);
        w.Put(id);
        w.Put((byte)dir);
        return w;
    }
}

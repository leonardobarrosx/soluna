using System.Diagnostics;
using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;

namespace Soluna.Server;

internal sealed class Player
{
    public required NetPeer Peer { get; init; }
    public required int Id { get; init; }
    public required string Name { get; init; }
    public int MapId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public Direction Dir { get; set; }
    public required Appearance Look { get; init; }
    public required Equipment Equipment { get; init; }
    public required List<int> Inventory { get; init; }

    /// <summary>Milliseconds of walking this player may still spend; refills with real time.</summary>
    public float MoveBudgetMs { get; set; } = GameServer.MoveBudgetCapMs;

    public long LastBudgetCheckMs { get; set; }

    public PlayerInfo Info => new(Id, Name, X, Y, Dir, Look, Equipment);
}

/// <summary>
/// Authoritative server: owns the maps and every player's position. Clients move
/// on their own for responsiveness and the server confirms or snaps them back.
/// </summary>
internal sealed class GameServer
{
    private const int SpawnX = 19, SpawnY = 14;

    // Each step costs a bit less than a walk so jitter never rejects an honest client,
    // and the cap stops a client from banking time and sprinting with it.
    public const float MoveBudgetCapMs = Constants.WalkTimeMs * 2;
    private const float StepCostMs = Constants.WalkTimeMs * 0.85f;

    private readonly MapStore _maps;
    private readonly ItemCatalog _items;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<NetPeer, Player> _players = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _nextId = 1;

    public GameServer(MapStore maps, ItemCatalog items)
    {
        _maps = maps;
        _items = items;
        _net = new NetManager(_listener) { AutoRecycle = true };

        _listener.ConnectionRequestEvent += request => request.AcceptIfKey(Constants.ConnectionKey);
        _listener.PeerDisconnectedEvent += OnDisconnected;
        _listener.NetworkReceiveEvent += OnReceive;
    }

    public void Start(int port)
    {
        _maps.Get(MapStore.StartMapId);
        if (!_net.Start(port)) throw new InvalidOperationException($"Could not bind UDP port {port}.");
    }

    public void Poll() => _net.PollEvents();

    public void Stop() => _net.Stop();

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        try
        {
            var type = (PacketType)reader.GetByte();
            if (type == PacketType.Login)
            {
                HandleLogin(peer, reader);
                return;
            }
            if (!_players.TryGetValue(peer, out var player)) return;

            switch (type)
            {
                case PacketType.MoveRequest: HandleMove(player, reader); break;
                case PacketType.ChatSend: HandleChat(player, reader); break;
                case PacketType.MapSave: HandleMapSave(player, reader); break;
                case PacketType.EquipToggle: HandleEquip(player, reader); break;
                default: Log.Warn($"Unknown packet {type} from {peer}."); break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Bad packet from {peer}: {ex.Message}");
        }
    }

    private void HandleLogin(NetPeer peer, NetDataReader reader)
    {
        if (_players.ContainsKey(peer)) return;

        var name = Clean(reader.GetString(), Constants.MaxNameLength);
        if (name.Length == 0) name = $"Viajante{_nextId}";
        var look = Appearance.Read(reader);
        if (!look.IsValid) look = Appearance.Random(Random.Shared);

        var map = _maps.Get(MapStore.StartMapId);
        var (x, y) = FindSpawn(map, PlayersOn(map.Id).ToList());
        var player = new Player
        {
            Peer = peer,
            Id = _nextId++,
            Name = name,
            MapId = map.Id,
            X = x,
            Y = y,
            Dir = Direction.Down,
            Look = look,
            // No accounts yet: every new character gets the whole wardrobe to try on.
            Equipment = _items.StarterEquipment(),
            Inventory = _items.All.Select(i => i.Id).ToList(),
            LastBudgetCheckMs = _clock.ElapsedMilliseconds,
        };

        var ok = PacketIO.Begin(PacketType.LoginOk);
        ok.Put(player.Info);
        ok.Put(player.Inventory.Count);
        foreach (var item in player.Inventory) ok.Put(item);
        peer.Send(ok, DeliveryMethod.ReliableOrdered);

        SendMap(peer, map);

        foreach (var other in PlayersOn(map.Id))
        {
            var existing = PacketIO.Begin(PacketType.PlayerJoined);
            existing.Put(other.Info);
            peer.Send(existing, DeliveryMethod.ReliableOrdered);
        }

        _players[peer] = player;
        Broadcast(player.MapId, Joined(player), except: player);
        BroadcastChat(player.MapId, "", $"{player.Name} chegou.");
        Log.Info($"{player.Name} (#{player.Id}) joined from {peer}.");
    }

    private void HandleMove(Player player, NetDataReader reader)
    {
        var dir = (Direction)reader.GetByte();
        var fromX = reader.GetInt();
        var fromY = reader.GetInt();
        if (!Enum.IsDefined(dir)) return;

        var map = _maps.Get(player.MapId);
        var (dx, dy) = dir.Delta();
        var now = _clock.ElapsedMilliseconds;
        player.MoveBudgetMs = Math.Min(MoveBudgetCapMs, player.MoveBudgetMs + (now - player.LastBudgetCheckMs));
        player.LastBudgetCheckMs = now;

        var tooFast = player.MoveBudgetMs < StepCostMs;
        var desynced = fromX != player.X || fromY != player.Y;
        var blocked = !map.IsWalkable(player.X + dx, player.Y + dy);

        player.Dir = dir;
        if (tooFast || desynced || blocked)
        {
            var fix = PacketIO.Begin(PacketType.PlayerPosition);
            fix.Put(player.X);
            fix.Put(player.Y);
            fix.Put((byte)player.Dir);
            player.Peer.Send(fix, DeliveryMethod.ReliableOrdered);
            return;
        }

        player.X += dx;
        player.Y += dy;
        player.MoveBudgetMs -= StepCostMs;

        var moved = PacketIO.Begin(PacketType.PlayerMoved);
        moved.Put(player.Id);
        moved.Put(player.X);
        moved.Put(player.Y);
        moved.Put((byte)player.Dir);
        Broadcast(player.MapId, moved, except: player);
    }

    private void HandleChat(Player player, NetDataReader reader)
    {
        var text = Clean(reader.GetString(), Constants.MaxChatLength);
        if (text.Length == 0) return;
        BroadcastChat(player.MapId, player.Name, text);
        Log.Info($"[chat] {player.Name}: {text}");
    }

    private void HandleMapSave(Player player, NetDataReader reader)
    {
        // Development build: every player is an editor. Gate this behind an access level later.
        var map = MapData.FromBytes(reader.GetBlob());
        var current = _maps.Get(player.MapId);
        if (map.Id != current.Id || !IsWellFormed(map))
        {
            Log.Warn($"Rejected malformed map from {player.Name}.");
            return;
        }

        _maps.Save(map);
        foreach (var other in PlayersOn(map.Id))
        {
            if (other != player) SendMap(other.Peer, map);
        }
        BroadcastChat(map.Id, "", $"{player.Name} salvou o mapa.");
        Log.Info($"{player.Name} saved map {map.Id}.");
    }

    /// <summary>Wears an item from the inventory, or takes it off if it is already worn.</summary>
    private void HandleEquip(Player player, NetDataReader reader)
    {
        var id = reader.GetInt();
        if (!player.Inventory.Contains(id) || _items.Get(id) is not { } item) return;

        player.Equipment[item.Slot] = player.Equipment[item.Slot] == id ? 0 : id;

        var look = PacketIO.Begin(PacketType.PlayerLook);
        look.Put(player.Id);
        look.Put(player.Equipment);
        Broadcast(player.MapId, look);
    }

    private void OnDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (!_players.Remove(peer, out var player)) return;

        var left = PacketIO.Begin(PacketType.PlayerLeft);
        left.Put(player.Id);
        Broadcast(player.MapId, left);
        BroadcastChat(player.MapId, "", $"{player.Name} saiu.");
        Log.Info($"{player.Name} left ({info.Reason}).");
    }

    private static bool IsWellFormed(MapData map)
    {
        var cells = map.Width * map.Height;
        return map.Width is > 0 and <= 256
            && map.Height is > 0 and <= 256
            && map.Layers.Length == MapData.LayerCount
            && map.Layers.All(l => l.Length == cells)
            && map.Attributes.Length == cells;
    }

    /// <summary>The walkable tile nearest the spawn point that nobody is standing on.</summary>
    private static (int x, int y) FindSpawn(MapData map, List<Player> others)
    {
        for (var radius = 0; radius < 8; radius++)
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
            var (x, y) = (SpawnX + dx, SpawnY + dy);
            if (map.IsWalkable(x, y) && !others.Any(p => p.X == x && p.Y == y)) return (x, y);
        }
        return (SpawnX, SpawnY);
    }

    private static void SendMap(NetPeer peer, MapData map)
    {
        var w = PacketIO.Begin(PacketType.MapLoad);
        w.PutBlob(map.ToBytes());
        peer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    private static NetDataWriter Joined(Player player)
    {
        var w = PacketIO.Begin(PacketType.PlayerJoined);
        w.Put(player.Info);
        return w;
    }

    private void BroadcastChat(int mapId, string from, string text)
    {
        var w = PacketIO.Begin(PacketType.ChatMessage);
        w.Put(from);
        w.Put(text);
        Broadcast(mapId, w);
    }

    private void Broadcast(int mapId, NetDataWriter writer, Player? except = null)
    {
        foreach (var player in PlayersOn(mapId))
        {
            if (player != except) player.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    private IEnumerable<Player> PlayersOn(int mapId) => _players.Values.Where(p => p.MapId == mapId);

    private static string Clean(string text, int max)
    {
        var chars = text.Where(c => !char.IsControl(c)).Take(max).ToArray();
        return new string(chars).Trim();
    }
}

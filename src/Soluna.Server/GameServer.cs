using System.Diagnostics;
using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;

namespace Soluna.Server;

internal sealed class Player
{
    public required NetPeer Peer { get; init; }
    public required int Id { get; init; }
    public required Account Account { get; init; }
    public required int Slot { get; init; }
    public required string Name { get; init; }
    public int MapId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public Direction Dir { get; set; }
    public required Appearance Look { get; init; }
    public required Equipment Equipment { get; init; }
    public required List<int> Inventory { get; init; }

    public bool IsAdmin => Account.Access > 0;

    /// <summary>Milliseconds of walking this player may still spend; refills with real time.</summary>
    public float MoveBudgetMs { get; set; } = GameServer.MoveBudgetCapMs;

    public long LastBudgetCheckMs { get; set; }

    public PlayerInfo Info => new(Id, Name, X, Y, Dir, Look, Equipment);

    /// <summary>Writes the live state back into the account's character slot.</summary>
    public void Store()
    {
        Account.Characters[Slot] = new CharacterSave
        {
            Name = Name,
            Look = Look,
            Equipment = Equipment.Items.ToArray(),
            Inventory = [.. Inventory],
            MapId = MapId,
            X = X,
            Y = Y,
            Dir = Dir,
        };
    }
}

/// <summary>A connection: logged out, logged in at the character screen, or playing.</summary>
internal sealed class Session(NetPeer peer)
{
    public NetPeer Peer { get; } = peer;
    public Account? Account { get; set; }
    public Player? Player { get; set; }
}

/// <summary>
/// Authoritative server: owns accounts, maps and every player's position. Clients move
/// on their own for responsiveness and the server confirms or snaps them back.
/// </summary>
internal sealed class GameServer
{
    private const int SpawnX = 19, SpawnY = 14;
    private const long AutosaveMs = 60_000;

    // Each step costs a bit less than a walk so jitter never rejects an honest client,
    // and the cap stops a client from banking time and sprinting with it.
    public const float MoveBudgetCapMs = Constants.WalkTimeMs * 2;
    private const float StepCostMs = Constants.WalkTimeMs * 0.85f;

    private readonly MapStore _maps;
    private readonly ItemCatalog _items;
    private readonly AccountStore _accounts;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<NetPeer, Session> _sessions = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _nextId = 1;
    private long _lastAutosave;

    public GameServer(MapStore maps, ItemCatalog items, AccountStore accounts)
    {
        _maps = maps;
        _items = items;
        _accounts = accounts;
        _net = new NetManager(_listener) { AutoRecycle = true };

        _listener.ConnectionRequestEvent += request => request.AcceptIfKey(Constants.ConnectionKey);
        _listener.PeerConnectedEvent += peer => _sessions[peer] = new Session(peer);
        _listener.PeerDisconnectedEvent += OnDisconnected;
        _listener.NetworkReceiveEvent += OnReceive;
    }

    public void Start(int port)
    {
        _maps.Get(MapStore.StartMapId);
        if (!_net.Start(port)) throw new InvalidOperationException($"Could not bind UDP port {port}.");
    }

    public void Poll()
    {
        _net.PollEvents();
        if (_clock.ElapsedMilliseconds - _lastAutosave < AutosaveMs) return;
        _lastAutosave = _clock.ElapsedMilliseconds;
        SaveAll();
    }

    public void Stop()
    {
        SaveAll();
        _net.Stop();
    }

    private IEnumerable<Player> Players => _sessions.Values.Select(s => s.Player).OfType<Player>();

    private void SaveAll()
    {
        var count = 0;
        foreach (var player in Players)
        {
            player.Store();
            _accounts.Save(player.Account);
            count++;
        }
        if (count > 0) Log.Info($"Saved {count} characters.");
    }

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (!_sessions.TryGetValue(peer, out var session)) return;
        try
        {
            var type = (PacketType)reader.GetByte();
            if (session.Player is { } player)
            {
                switch (type)
                {
                    case PacketType.MoveRequest: HandleMove(player, reader); break;
                    case PacketType.ChatSend: HandleChat(player, reader); break;
                    case PacketType.MapSave: HandleMapSave(player, reader); break;
                    case PacketType.EquipToggle: HandleEquip(player, reader); break;
                    default: Log.Warn($"Unexpected {type} from {player.Name}."); break;
                }
                return;
            }

            switch (type)
            {
                case PacketType.Login: HandleLogin(session, reader, register: false); break;
                case PacketType.Register: HandleLogin(session, reader, register: true); break;
                case PacketType.CreateCharacter: HandleCreate(session, reader); break;
                case PacketType.DeleteCharacter: HandleDelete(session, reader); break;
                case PacketType.PlayCharacter: HandlePlay(session, reader); break;
                default: Log.Warn($"Unexpected {type} from {peer} before entering the world."); break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Bad packet from {peer}: {ex.Message}");
        }
    }

    // ---- Account and characters ----

    private void HandleLogin(Session session, NetDataReader reader, bool register)
    {
        if (session.Account != null) return;
        var username = reader.GetString().Trim();
        var password = reader.GetString();

        if ((register ? AccountStore.CheckUsername(username) ?? AccountStore.CheckPassword(password) : null) is { } problem)
        {
            Refuse(session.Peer, problem);
            return;
        }

        Account? account;
        if (register)
        {
            if (_accounts.Exists(username))
            {
                Refuse(session.Peer, "Esse usuário já existe.");
                return;
            }
            account = _accounts.Create(username, password);
            Log.Info($"Account '{username}' registered from {session.Peer}.");
        }
        else
        {
            account = _accounts.Authenticate(username, password);
            if (account == null)
            {
                Refuse(session.Peer, "Usuário ou senha incorretos.");
                return;
            }
        }

        if (_sessions.Values.Any(s => s.Account?.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase) == true))
        {
            Refuse(session.Peer, "Essa conta já está conectada.");
            return;
        }

        session.Account = account;
        var catalog = PacketIO.Begin(PacketType.ItemCatalog);
        catalog.Put(_items.Json);
        session.Peer.Send(catalog, DeliveryMethod.ReliableOrdered);
        SendCharacterList(session);
        Log.Info($"'{account.Username}' logged in.");
    }

    private void HandleCreate(Session session, NetDataReader reader)
    {
        if (session.Account is not { } account) return;
        var slot = reader.GetByte();
        var name = Clean(reader.GetString(), Constants.MaxNameLength);
        var look = Appearance.Read(reader);

        string? problem = slot >= Constants.MaxCharacters ? "Espaço inválido."
            : account.Characters[slot] != null ? "Esse espaço já tem um personagem."
            : name.Length < 2 ? "O nome precisa ter pelo menos 2 letras."
            : !look.IsValid ? "Aparência inválida."
            : _accounts.NameTaken(name) ? "Já existe um personagem com esse nome."
            : null;
        if (problem != null)
        {
            Refuse(session.Peer, problem);
            return;
        }

        account.Characters[slot] = new CharacterSave
        {
            Name = name,
            Look = look,
            Equipment = _items.StarterEquipment().Items.ToArray(),
            Inventory = _items.StarterItems.ToList(),
        };
        _accounts.ClaimName(name);
        _accounts.Save(account);
        SendCharacterList(session);
        Log.Info($"'{account.Username}' created {name}.");
    }

    private void HandleDelete(Session session, NetDataReader reader)
    {
        if (session.Account is not { } account) return;
        var slot = reader.GetByte();
        if (slot >= Constants.MaxCharacters || account.Characters[slot] is not { } character) return;

        account.Characters[slot] = null;
        _accounts.ReleaseName(character.Name);
        _accounts.Save(account);
        SendCharacterList(session);
        Log.Info($"'{account.Username}' deleted {character.Name}.");
    }

    private void HandlePlay(Session session, NetDataReader reader)
    {
        if (session.Account is not { } account) return;
        var slot = reader.GetByte();
        if (slot >= Constants.MaxCharacters || account.Characters[slot] is not { } save) return;

        var map = _maps.Get(save.MapId);
        var others = PlayersOn(map.Id).ToList();
        var (x, y) = map.IsWalkable(save.X, save.Y) && !others.Any(p => p.X == save.X && p.Y == save.Y)
            ? (save.X, save.Y)
            : FindSpawn(map, others);

        // Drop anything the catalog no longer has, so a removed item cannot linger on a save.
        var equipment = save.ToEquipment();
        for (var s = 0; s < Equipment.SlotCount; s++)
        {
            if (_items.Get(equipment[(EquipSlot)s]) == null) equipment[(EquipSlot)s] = 0;
        }

        var player = new Player
        {
            Peer = session.Peer,
            Id = _nextId++,
            Account = account,
            Slot = slot,
            Name = save.Name,
            MapId = map.Id,
            X = x,
            Y = y,
            Dir = save.Dir,
            Look = save.Look,
            Equipment = equipment,
            Inventory = save.Inventory.Where(id => _items.Get(id) != null).ToList(),
            LastBudgetCheckMs = _clock.ElapsedMilliseconds,
        };

        var ok = PacketIO.Begin(PacketType.LoginOk);
        ok.Put(player.Info);
        ok.Put(account.Access);
        session.Peer.Send(ok, DeliveryMethod.ReliableOrdered);
        SendInventory(player);
        SendMap(session.Peer, map);

        foreach (var other in others)
        {
            var existing = PacketIO.Begin(PacketType.PlayerJoined);
            existing.Put(other.Info);
            session.Peer.Send(existing, DeliveryMethod.ReliableOrdered);
        }

        session.Player = player;
        Broadcast(player.MapId, Joined(player), except: player);
        BroadcastChat(player.MapId, "", $"{player.Name} chegou.");
        Log.Info($"{player.Name} (#{player.Id}, '{account.Username}') entered at {x},{y}.");
    }

    private void SendCharacterList(Session session)
    {
        var w = PacketIO.Begin(PacketType.CharacterList);
        foreach (var character in session.Account!.Characters)
        {
            w.Put(character != null);
            if (character == null) continue;
            w.Put(character.Name);
            character.Look.Write(w);
            w.Put(character.ToEquipment());
        }
        session.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    private static void Refuse(NetPeer peer, string message)
    {
        var w = PacketIO.Begin(PacketType.Refused);
        w.Put(message);
        peer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    // ---- In the world ----

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
        if (text.StartsWith('/'))
        {
            HandleCommand(player, text);
            return;
        }
        BroadcastChat(player.MapId, player.Name, text);
        Log.Info($"[chat] {player.Name}: {text}");
    }

    /// <summary>Slash commands. /item is admin-only until items can be found or traded.</summary>
    private void HandleCommand(Player player, string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0].ToLowerInvariant())
        {
            case "/item" when !player.IsAdmin:
                Tell(player, "Só administradores podem criar itens.");
                break;

            case "/item" when parts.Length > 1 && int.TryParse(parts[1], out var id):
                if (_items.Get(id) is not { } item)
                {
                    Tell(player, $"Não existe item {id}.");
                }
                else if (player.Inventory.Contains(id))
                {
                    Tell(player, $"Você já tem {item.Name}.");
                }
                else
                {
                    player.Inventory.Add(id);
                    SendInventory(player);
                    Tell(player, $"Recebeu {item.Name}.");
                }
                break;

            case "/item":
            case "/itens":
                Tell(player, "Itens: " + string.Join(", ", _items.All.Select(i => $"{i.Id} {i.Name}")));
                break;

            case "/online":
                Tell(player, "Online: " + string.Join(", ", Players.Select(p => p.Name)));
                break;

            default:
                Tell(player, "Comandos: /online, /itens, /item <id> (admin).");
                break;
        }
    }

    private void HandleMapSave(Player player, NetDataReader reader)
    {
        var blob = reader.GetBlob();
        if (!player.IsAdmin)
        {
            Tell(player, "Só administradores podem salvar mapas.");
            return;
        }

        var map = MapData.FromBytes(blob);
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
        if (!_sessions.Remove(peer, out var session)) return;
        if (session.Player is not { } player) return;

        player.Store();
        _accounts.Save(player.Account);

        var left = PacketIO.Begin(PacketType.PlayerLeft);
        left.Put(player.Id);
        Broadcast(player.MapId, left);
        BroadcastChat(player.MapId, "", $"{player.Name} saiu.");
        Log.Info($"{player.Name} left ({info.Reason}), saved at {player.X},{player.Y}.");
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

    private static void SendInventory(Player player)
    {
        var w = PacketIO.Begin(PacketType.InventoryUpdate);
        w.Put(player.Inventory.Count);
        foreach (var item in player.Inventory) w.Put(item);
        player.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    private static NetDataWriter Joined(Player player)
    {
        var w = PacketIO.Begin(PacketType.PlayerJoined);
        w.Put(player.Info);
        return w;
    }

    /// <summary>A system line in one player's chat only.</summary>
    private static void Tell(Player player, string text)
    {
        var w = PacketIO.Begin(PacketType.ChatMessage);
        w.Put("");
        w.Put(text);
        player.Peer.Send(w, DeliveryMethod.ReliableOrdered);
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

    private IEnumerable<Player> PlayersOn(int mapId) => Players.Where(p => p.MapId == mapId);

    private static string Clean(string text, int max)
    {
        var chars = text.Where(c => !char.IsControl(c)).Take(max).ToArray();
        return new string(chars).Trim();
    }
}

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

    /// <summary>Development: new characters start with every item, to try the wardrobe without /item.</summary>
    public bool StarterGetsAllItems { get; init; }

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

    /// <summary>Fixed simulation steps per second; <see cref="Tick"/> runs this often whatever the network does.</summary>
    public const int TicksPerSecond = 20;

    public long Ticks { get; private set; }

    public void Poll() => _net.PollEvents();

    /// <summary>
    /// One fixed step of the world. Everything that happens over time, rather than in answer to a
    /// packet, belongs here: autosave today, NPC thinking, regeneration and respawns next.
    /// </summary>
    public void Tick()
    {
        Ticks++;
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
                    case PacketType.MapRequest: HandleMapRequest(player, reader); break;
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
            Inventory = StarterGetsAllItems ? _items.All.Select(i => i.Id).ToList() : _items.StarterItems.ToList(),
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

        session.Player = player;
        PlaceOnMap(player, map, x, y);
        BroadcastChat(player.MapId, "", $"{player.Name} chegou.");
        Log.Info($"{player.Name} (#{player.Id}, '{account.Username}') entered map {map.Id} at {x},{y}.");
    }

    /// <summary>
    /// Puts a player on a map at a free walkable tile near (x, y): tells them where they are, shows them
    /// who is already there and shows them to everyone else.
    /// </summary>
    private void PlaceOnMap(Player player, MapData map, int x, int y)
    {
        var others = PlayersOn(map.Id).Where(p => p != player).ToList();
        (x, y) = map.NearestWalkable(Math.Clamp(x, 0, map.Width - 1), Math.Clamp(y, 0, map.Height - 1),
            (tx, ty) => !others.Any(p => p.X == tx && p.Y == ty));
        player.MapId = map.Id;
        player.X = x;
        player.Y = y;

        var change = PacketIO.Begin(PacketType.MapChange);
        change.Put(map.Id);
        change.Put(map.Revision);
        change.Put(x);
        change.Put(y);
        change.Put((byte)player.Dir);
        player.Peer.Send(change, DeliveryMethod.ReliableOrdered);

        foreach (var other in others)
        {
            var existing = PacketIO.Begin(PacketType.PlayerJoined);
            existing.Put(other.Info);
            player.Peer.Send(existing, DeliveryMethod.ReliableOrdered);
        }
        Broadcast(map.Id, Joined(player), except: player);
    }

    /// <summary>Moves a player to another map (or another spot on the same one).</summary>
    private void Transfer(Player player, int mapId, int x, int y)
    {
        if (!_maps.Exists(mapId))
        {
            Tell(player, $"O mapa {mapId} não existe.");
            return;
        }
        var left = PacketIO.Begin(PacketType.PlayerLeft);
        left.Put(player.Id);
        Broadcast(player.MapId, left, except: player);

        PlaceOnMap(player, _maps.Get(mapId), x, y);
    }

    private void HandleMapRequest(Player player, NetDataReader reader)
    {
        var id = reader.GetInt();
        // Only the map the player is on: no reading the whole world from a client.
        if (id != player.MapId) return;
        SendMap(player.Peer, _maps.Get(id));
        Log.Info($"Sent map {id} to {player.Name}.");
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
        var (tx, ty) = (player.X + dx, player.Y + dy);

        // Walking off an edge that links to another map carries on there, at the matching spot.
        if (!tooFast && !desynced && !map.InBounds(tx, ty) && map.Links[dir] is var linked and > 0 && _maps.Exists(linked))
        {
            player.Dir = dir;
            player.MoveBudgetMs -= StepCostMs;
            var next = _maps.Get(linked);
            var (nx, ny) = dir switch
            {
                Direction.Up => (tx, next.Height - 1),
                Direction.Down => (tx, 0),
                Direction.Left => (next.Width - 1, ty),
                _ => (0, ty),
            };
            Transfer(player, linked, nx, ny);
            return;
        }

        var blocked = !map.IsWalkable(tx, ty);

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

        if (map.WarpAt(player.X, player.Y) is { } warp) Transfer(player, warp.Map, warp.ToX, warp.ToY);
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
                Tell(player, "Online: " + string.Join(", ", Players.Select(p => $"{p.Name} (mapa {p.MapId})")));
                break;

            case "/mapas":
                Tell(player, "Mapas: " + string.Join(", ", _maps.Ids().Select(id => $"{id} {_maps.Get(id).Name}")));
                break;

            case "/ir" or "/trazer" or "/novomapa" or "/mapa" when !player.IsAdmin:
                Tell(player, "Comando de administrador.");
                break;

            // /ir <mapa> [x y]: go to a map.
            case "/ir" when parts.Length > 1 && int.TryParse(parts[1], out var target):
            {
                var x = parts.Length > 3 && int.TryParse(parts[2], out var px) ? px : -1;
                var y = parts.Length > 3 && int.TryParse(parts[3], out var py) ? py : -1;
                if (!_maps.Exists(target)) Tell(player, $"O mapa {target} não existe.");
                else if (x < 0) Transfer(player, target, _maps.Get(target).Spawn.x, _maps.Get(target).Spawn.y);
                else Transfer(player, target, x, y);
                break;
            }

            // /trazer <nome>: bring a player here.
            case "/trazer" when parts.Length > 1:
            {
                var who = Players.FirstOrDefault(p => p.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
                if (who == null) Tell(player, $"{parts[1]} não está online.");
                else Transfer(who, player.MapId, player.X, player.Y + 1);
                break;
            }

            // /novomapa <largura> <altura> [nome]: a new map floored with the tile you stand on.
            case "/novomapa" when parts.Length > 2 && int.TryParse(parts[1], out var w) && int.TryParse(parts[2], out var h)
                                  && w is >= 10 and <= 256 && h is >= 10 and <= 256:
            {
                var here = _maps.Get(player.MapId);
                var name = parts.Length > 3 ? string.Join(' ', parts[3..]) : "Novo mapa";
                var created = _maps.Create(name, w, h, here.Tilesets, here.GetTile(0, player.X, player.Y), here.Layers.Length, here.FringeFrom);
                Tell(player, $"Mapa {created.Id} '{name}' criado ({w}x{h}).");
                Transfer(player, created.Id, w / 2, h / 2);
                break;
            }

            case "/mapa":
                MapCommand(player, parts);
                break;

            default:
                Tell(player, "Comandos: /online, /mapas, /itens. Admin: /item <id>, /ir <mapa> [x y], /trazer <nome>, /novomapa <l> <a> [nome], /mapa.");
                break;
        }
    }

    /// <summary>
    /// /mapa nome &lt;texto&gt; · /mapa pvp|seguro · /mapa link cima|baixo|esquerda|direita &lt;id|0&gt; · /mapa spawn ·
    /// /mapa musica &lt;arquivo&gt;. Edits the map the admin is on.
    /// </summary>
    private void MapCommand(Player player, string[] parts)
    {
        var map = _maps.Get(player.MapId);
        var sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
        switch (sub)
        {
            case "nome" when parts.Length > 2:
                map.Name = string.Join(' ', parts[2..]);
                break;
            case "pvp":
                map.Moral = MapMoral.Pvp;
                break;
            case "seguro":
                map.Moral = MapMoral.Safe;
                break;
            case "spawn":
                (map.SpawnX, map.SpawnY) = (player.X, player.Y);
                break;
            case "musica":
                map.Music = parts.Length > 2 ? parts[2] : "";
                break;
            case "link" when parts.Length > 3 && int.TryParse(parts[3], out var to) && DirectionNamed(parts[2]) is { } dir:
                if (to != 0 && !_maps.Exists(to))
                {
                    Tell(player, $"O mapa {to} não existe.");
                    return;
                }
                map.Links.Set(dir, to);
                break;
            default:
                var l = map.Links;
                Tell(player, $"Mapa {map.Id} '{map.Name}' {map.Width}x{map.Height}, {(map.Moral == MapMoral.Pvp ? "PvP" : "seguro")}, " +
                             $"links cima {l.Up} baixo {l.Down} esquerda {l.Left} direita {l.Right}, {map.Warps.Count} teleportes. " +
                             "Use: /mapa nome|pvp|seguro|spawn|musica|link <direção> <id>.");
                return;
        }
        _maps.Save(map);
        AnnounceRevision(map);
        Tell(player, "Mapa atualizado.");
    }

    private static Direction? DirectionNamed(string name) => name.ToLowerInvariant() switch
    {
        "cima" or "norte" or "up" => Direction.Up,
        "baixo" or "sul" or "down" => Direction.Down,
        "esquerda" or "oeste" or "left" => Direction.Left,
        "direita" or "leste" or "right" => Direction.Right,
        _ => null,
    };

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

        map.Revision = current.Revision;
        _maps.Save(map);
        AnnounceRevision(map);
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
            && map.Layers.Length is > 0 and <= MapData.MaxLayers
            && map.FringeFrom >= 0 && map.FringeFrom <= map.Layers.Length
            && map.Layers.All(l => l.Length == cells)
            && map.Attributes.Length == cells;
    }

    /// <summary>The walkable tile nearest the map's spawn point that nobody is standing on.</summary>
    private static (int x, int y) FindSpawn(MapData map, List<Player> others)
    {
        var (spawnX, spawnY) = map.Spawn;
        for (var radius = 0; radius < 8; radius++)
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
            var (x, y) = (spawnX + dx, spawnY + dy);
            if (map.IsWalkable(x, y) && !others.Any(p => p.X == x && p.Y == y)) return (x, y);
        }
        return (spawnX, spawnY);
    }

    /// <summary>Everyone on a map that just changed fetches it again (their cached copy is now stale).</summary>
    private void AnnounceRevision(MapData map)
    {
        var w = PacketIO.Begin(PacketType.MapRevision);
        w.Put(map.Id);
        w.Put(map.Revision);
        Broadcast(map.Id, w);
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

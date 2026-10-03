using LiteNetLib.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Soluna.Client.Graphics;
using Soluna.Client.Net;
using Soluna.Client.UI;
using Soluna.Client.World;
using Soluna.Shared;

namespace Soluna.Client;

/// <param name="User">Log in with this account on start (and create it if it does not exist yet).</param>
/// <param name="Password">Password for <paramref name="User"/>.</param>
/// <param name="Play">After logging in, enter the world with the first character, creating a random one if the slot is empty.</param>
/// <param name="Screenshot">Debug: save a PNG of the screen to this path a few seconds in, then quit.</param>
/// <param name="Walk">Debug: wander around on its own, for testing with several clients.</param>
/// <param name="Editor">Open the map editor as soon as the map arrives.</param>
/// <param name="Inventory">Open the equipment panel on entry.</param>
/// <param name="Creation">After logging in, open character creation instead of the select screen.</param>
/// <param name="GameEditorTab">Open the game editor on entry, on this tab ("items" or "npcs").</param>
/// <param name="Import">Open the import dialog on this file on entry, as if it had been dropped on the window.</param>
internal sealed record ClientOptions(
    string Host, int Port, string Name, string? User = null, string? Password = null, bool Play = false,
    string? Screenshot = null, bool Walk = false, bool Editor = false, bool Inventory = false, bool Creation = false,
    string? GameEditorTab = null, string? EditorMode = null, string? Import = null);

internal enum Stage
{
    Login,
    Select,
    Create,
    World,
}

internal sealed class SolunaGame : Game
{
    private const float RetrySeconds = 3;

    // Light around the local player, in world pixels.
    private const float LightInner = 70, LightOuter = 230;
    private const float MaskInner = 0.07f, MaskOuter = MaskInner * LightOuter / LightInner;

    private readonly ClientOptions _options;
    private readonly GraphicsDeviceManager _graphics;
    private readonly Connection _connection = new();
    private readonly Camera _camera = new();
    private readonly Input _input = new();
    private readonly ChatBox _chat = new();
    private readonly Dictionary<int, Character> _others = [];

    // Filled by the server after login; nothing about items is read from disk on the client.
    private readonly ItemCatalog _items = ItemCatalog.Empty();
    private readonly NpcCatalog _npcDefs = new();

    /// <summary>NPCs on the current map by their index on it, with their definition.</summary>
    private readonly Dictionary<int, (Character view, NpcDef def)> _npcs = [];

    private readonly Hud _hud = new();
    private Gui _gui = null!;
    private GameEditor _gameEditor = null!;
    private ImportDialog _import = null!;
    private AssetSync _assets = null!;
    private string? _pendingImport;
    private bool _autoPlay;
    private readonly FloatingText _floating = new();
    private float _nextAttackIn;

    private SpriteBatch _batch = null!;
    private Textures _textures = null!;
    private Fonts _fonts = null!;
    private MapRenderer _renderer = null!;
    private MapEditor _editor = null!;
    private Sprites _sprites = null!;
    private InventoryPanel _inventory = null!;
    private LoginScreen _login = null!;
    private SelectScreen _select = null!;
    private CreationScreen? _creation;
    private Texture2D _lightMask = null!;

    private Stage _stage = Stage.Login;
    private MapData? _map;
    private Character? _local;
    private byte _access;
    private bool _connecting;
    private bool _loggingOut;
    private bool _autoRegisterTried;
    private float _retryIn;
    private bool _night = true;

    // Set while walking off a map's edge, until the server answers with the next map.
    private bool _awaitingTransfer;

    private static readonly string MapCache = Path.Combine(DataPaths.Root, "cache", DataPaths.Game, "maps");
    private float _stageSeconds;
    private Direction? _wanderDir;
    private float _wanderSeconds;

    public SolunaGame(ClientOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = true,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = Constants.GameName;
        Window.TextInput += (_, e) =>
        {
            switch (_stage)
            {
                case Stage.Login: _login.OnTextInput(e.Character); break;
                case Stage.Create: _creation?.OnTextInput(e.Character); break;
                case Stage.World when _import.Open || _gameEditor.Open || _editor.Active && !_chat.Typing: _gui.OnTextInput(e.Character); break;
                case Stage.World: _chat.OnTextInput(e.Character); break;
            }
        };
        // Dropping a PNG on the window is how admins add tilesets and sprites to the game.
        Window.FileDrop += (_, e) =>
        {
            if (_stage != Stage.World || e.Files.Length == 0) return;
            if (_access == 0) _chat.Add("", "Só administradores podem importar arquivos.");
            else _import.Show(e.Files[0]);
        };
        Window.ClientSizeChanged += (_, _) =>
        {
            var bounds = Window.ClientBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            _graphics.PreferredBackBufferWidth = bounds.Width;
            _graphics.PreferredBackBufferHeight = bounds.Height;
            _graphics.ApplyChanges();
        };

        _connection.Connected += OnConnected;
        _connection.Disconnected += OnDisconnected;
        _connection.Received += OnPacket;
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _textures = new Textures(GraphicsDevice);
        _fonts = new Fonts();
        _sprites = new Sprites(new CharacterSprites(GraphicsDevice, _items), _textures);
        _renderer = new MapRenderer(_textures, _sprites);
        _inventory = new InventoryPanel(_items, _sprites);
        _login = new LoginScreen(_options.User ?? "");
        _select = new SelectScreen(_sprites);
        _lightMask = PlaceholderArt.LightMask(GraphicsDevice, MaskInner, MaskOuter);
        _gui = new Gui(_textures.Pixel, _fonts, _input);
        _editor = new MapEditor(_textures, _renderer, _gui)
        {
            SaveRequested = SaveMap,
            MapsRequested = () => Send(PacketType.MapListRequest),
            // Going to and creating maps run on the server; the buttons send the admin commands for them.
            GoTo = id => Send(PacketType.ChatSend, w => w.Put($"/ir {id}")),
            CreateMap = (name, width, height) => Send(PacketType.ChatSend, w => w.Put($"/novomapa {width} {height} {name}")),
        };
        _gameEditor = new GameEditor(_gui, _textures, _sprites, _items, _npcDefs)
        {
            Save = (kind, json) => Send(PacketType.ContentSave, w =>
            {
                w.Put((byte)kind);
                w.PutBlob(System.Text.Encoding.UTF8.GetBytes(json));
            }),
        };
        _import = new ImportDialog(_gui, _textures)
        {
            Upload = (kind, name, shareable, data) => Send(PacketType.AssetUpload, w =>
            {
                w.Put((byte)kind);
                w.Put(name);
                w.Put(shareable);
                w.PutBlob(data);
            }),
        };
        _assets = new AssetSync(path => Send(PacketType.AssetRequest, w => w.Put(path)));
        if (_options.Import != null) _pendingImport = _options.Import;
        Connect();
    }

    protected override void UnloadContent() => _connection.Stop();

    private Point Screen => new(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

    private void GoTo(Stage stage)
    {
        _stage = stage;
        _stageSeconds = 0;
    }

    // ---- Networking ----

    private void Connect()
    {
        _connecting = true;
        _connection.Connect(_options.Host, _options.Port);
    }

    private void OnConnected()
    {
        _connecting = false;
        _login.Online = true;
        if (_options.User != null && _options.Password != null) Send(PacketType.Login, w => { w.Put(_options.User); w.Put(_options.Password); });
    }

    private void OnDisconnected(string reason)
    {
        var wasIn = _stage != Stage.Login;
        _connecting = false;
        _map = null;
        _local = null;
        _others.Clear();
        _npcs.Clear();
        _creation = null;
        _retryIn = _loggingOut ? 0 : RetrySeconds;
        _login.Online = false;
        _login.Busy = false;
        _login.Message = _loggingOut ? "" : wasIn ? $"Conexão perdida ({reason})." : "";
        _login.MessageIsError = true;
        _loggingOut = false;
        GoTo(Stage.Login);
    }

    private void Send(PacketType type, Action<NetDataWriter>? write = null)
    {
        var w = PacketIO.Begin(type);
        write?.Invoke(w);
        _connection.Send(w);
    }

    private void OnPacket(PacketType type, NetDataReader r)
    {
        switch (type)
        {
            case PacketType.Refused:
                OnRefused(r.GetString());
                break;

            case PacketType.AssetManifest:
            {
                var count = r.GetInt();
                var entries = new List<(string, long, string)>(count);
                for (var i = 0; i < count; i++) entries.Add((r.GetString(), r.GetLong(), r.GetString()));
                _assets.OnManifest(entries);
                break;
            }
            case PacketType.AssetData:
                _assets.OnData(r.GetString(), r.GetBlob());
                break;
            case PacketType.AssetAdded:
                _assets.OnAdded(r.GetString(), r.GetLong(), r.GetString());
                break;

            case PacketType.ItemCatalog:
                _items.Replace(r.GetString());
                _gameEditor.CatalogChanged(ContentKind.Items);
                _sprites.Clear();
                break;

            case PacketType.NpcCatalog:
                _npcDefs.Replace(r.GetString());
                _gameEditor.CatalogChanged(ContentKind.Npcs);
                // NPCs on screen hold their old definition: point them at the new one.
                foreach (var (index, npc) in _npcs.ToList())
                {
                    if (_npcDefs.Get(npc.def.Id) is { } def) _npcs[index] = (npc.view, def);
                }
                break;

            case PacketType.NpcSpawned:
            {
                var index = r.GetInt();
                var def = _npcDefs.Get(r.GetInt());
                var (x, y, dir) = (r.GetInt(), r.GetInt(), (Direction)r.GetByte());
                var (hp, maxHp) = (r.GetInt(), r.GetInt());
                if (def == null) break;
                var view = new Character(-1, def.Name, def.Look ?? new Appearance(0, 0, 0, 0, 0), new Equipment()) { Hp = hp, MaxHp = maxHp };
                view.Place(x, y, dir);
                _npcs[index] = (view, def);
                break;
            }
            case PacketType.NpcMoved:
            {
                var index = r.GetInt();
                var (x, y, dir) = (r.GetInt(), r.GetInt(), (Direction)r.GetByte());
                if (!_npcs.TryGetValue(index, out var npc)) break;
                if (Math.Abs(x - npc.view.TileX) + Math.Abs(y - npc.view.TileY) == 1) npc.view.StartMove(dir, x, y);
                else npc.view.Place(x, y, dir);
                break;
            }
            case PacketType.NpcRemoved:
                _npcs.Remove(r.GetInt());
                break;

            case PacketType.Vitals:
                (_hud.Hp, _hud.MaxHp, _hud.Mp, _hud.MaxMp) = (r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt());
                (_hud.Level, _hud.Exp, _hud.ExpToNext) = (r.GetInt(), r.GetInt(), r.GetInt());
                break;

            case PacketType.HpChanged:
            {
                var kind = (UnitKind)r.GetByte();
                var id = r.GetInt();
                var (hp, maxHp, change) = (r.GetInt(), r.GetInt(), r.GetInt());
                if (Unit(kind, id) is not { } unit) break;
                (unit.Hp, unit.MaxHp) = (hp, maxHp);
                _floating.Add(unit.Position + new Vector2(Constants.TileSize / 2f, 0), change);
                break;
            }
            case PacketType.Attacked:
            {
                var kind = (UnitKind)r.GetByte();
                var id = r.GetInt();
                var dir = (Direction)r.GetByte();
                Unit(kind, id)?.Swing(dir);
                break;
            }

            case PacketType.CharacterList:
                OnCharacterList(r);
                break;

            case PacketType.LoginOk:
            {
                var me = r.GetPlayerInfo();
                _access = r.GetByte();
                _local = new Character(me.Id, me.Name, me.Look, me.Equipment);
                _local.Place(me.X, me.Y, me.Dir);
                _inventory.Open = _options.Inventory;
                if (_options.GameEditorTab != null && _access > 0) _gameEditor.OpenOn(_options.GameEditorTab);
                Window.Title = $"{Constants.GameName} · {me.Name}";
                _chat.Add("", _access > 0 ? "Você é administrador: F1 abre o editor, /item <id> cria itens." : "Bem-vindo a Soluna.");
                GoTo(Stage.World);
                break;
            }
            case PacketType.InventoryUpdate:
            {
                _inventory.Inventory.Clear();
                var count = r.GetInt();
                for (var i = 0; i < count; i++) _inventory.Inventory.Add(r.GetInt());
                break;
            }
            case PacketType.MapList:
            {
                var maps = new List<(int, string)>();
                var count = r.GetInt();
                for (var i = 0; i < count; i++) maps.Add((r.GetInt(), r.GetString()));
                _editor.Maps = maps;
                break;
            }
            case PacketType.MapChange:
                OnMapChange(r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt(), (Direction)r.GetByte());
                break;

            case PacketType.MapRevision:
            {
                var id = r.GetInt();
                var revision = r.GetInt();
                if (_map != null && id == _map.Id && revision != _map.Revision) Send(PacketType.MapRequest, w => w.Put(id));
                break;
            }
            case PacketType.MapLoad:
            {
                var blob = r.GetBlob();
                ShowMap(MapData.FromBytes(blob));
                try
                {
                    Directory.CreateDirectory(MapCache);
                    File.WriteAllBytes(Path.Combine(MapCache, $"{_map!.Id}.json"), blob);
                }
                catch (IOException)
                {
                    // A cache that cannot be written only costs a download next time.
                }
                break;
            }

            case PacketType.PlayerJoined:
            {
                var p = r.GetPlayerInfo();
                if (p.Id == _local?.Id) break;
                var character = new Character(p.Id, p.Name, p.Look, p.Equipment);
                character.Place(p.X, p.Y, p.Dir);
                _others[p.Id] = character;
                break;
            }
            case PacketType.PlayerLeft:
                _others.Remove(r.GetInt());
                break;

            case PacketType.PlayerMoved:
            {
                var id = r.GetInt();
                var x = r.GetInt();
                var y = r.GetInt();
                var dir = (Direction)r.GetByte();
                if (!_others.TryGetValue(id, out var other)) break;
                var adjacent = Math.Abs(x - other.TileX) + Math.Abs(y - other.TileY) == 1;
                if (adjacent) other.StartMove(dir, x, y);
                else other.Place(x, y, dir);
                break;
            }
            case PacketType.PlayerPosition:
                _awaitingTransfer = false;
                _local?.Place(r.GetInt(), r.GetInt(), (Direction)r.GetByte());
                break;

            case PacketType.ChatMessage:
            {
                var (from, text) = (r.GetString(), r.GetString());
                _chat.Add(from, text);
                if (from.Length == 0 && _gameEditor.Open) _gameEditor.Message(text);
                break;
            }

            case PacketType.PlayerLook:
            {
                var id = r.GetInt();
                var equipment = r.GetEquipment();
                if (id == _local?.Id) _local.Equipment = equipment;
                else if (_others.TryGetValue(id, out var other)) other.Equipment = equipment;
                break;
            }
        }
    }

    /// <summary>
    /// The server put us on a map: everyone we could see belongs to the old one, our position is the
    /// new one, and the map comes from the cache when its revision matches, otherwise from the server.
    /// </summary>
    private void OnMapChange(int id, int revision, int x, int y, Direction dir)
    {
        _awaitingTransfer = false;
        _others.Clear();
        _npcs.Clear();
        _local?.Place(x, y, dir);

        if (_map?.Id == id && _map.Revision == revision) return;
        var cached = LoadCached(id);
        if (cached?.Revision == revision)
        {
            ShowMap(cached);
            return;
        }
        _map = null;
        Send(PacketType.MapRequest, w => w.Put(id));
    }

    private void ShowMap(MapData map)
    {
        var changed = _map?.Id != map.Id;
        _map = map;
        _editor.Dirty = false;
        if (changed && _editor.Active)
        {
            _editor.Toggle(map);
            _editor.Toggle(map);
        }
        if (_options.Editor && !_editor.Active)
        {
            _editor.Toggle(map);
            if (_options.EditorMode != null) _editor.SetMode(_options.EditorMode);
        }
        if (changed) _chat.Add("", $"Você está em {map.Name}.");
    }

    private static MapData? LoadCached(int id)
    {
        try
        {
            var path = Path.Combine(MapCache, $"{id}.json");
            return File.Exists(path) ? MapData.FromBytes(File.ReadAllBytes(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private Character? Unit(UnitKind kind, int id) => kind == UnitKind.Npc
        ? (_npcs.TryGetValue(id, out var npc) ? npc.view : null)
        : id == _local?.Id ? _local : _others.GetValueOrDefault(id);

    private void OnRefused(string message)
    {
        switch (_stage)
        {
            case Stage.Login:
                // --play with an account that does not exist yet: create it once, then carry on.
                if (_options.Play && _options.User != null && _options.Password != null && !_autoRegisterTried)
                {
                    _autoRegisterTried = true;
                    Send(PacketType.Register, w => { w.Put(_options.User); w.Put(_options.Password); });
                    return;
                }
                _login.Busy = false;
                _login.Message = message;
                _login.MessageIsError = true;
                break;
            case Stage.Create when _creation != null:
                _creation.Message = message;
                break;
            default:
                _select.Message = message;
                break;
        }
    }

    private void OnCharacterList(NetDataReader r)
    {
        for (var i = 0; i < Constants.MaxCharacters; i++)
            _select.Slots[i] = r.GetBool() ? new CharacterSummary(r.GetString(), Appearance.Read(r), r.GetEquipment()) : null;
        _select.Message = "";
        _login.Busy = false;
        _creation = null;
        GoTo(Stage.Select);

        if (_options.Creation)
        {
            _creation = new CreationScreen(_sprites, _items, _options.Name);
            GoTo(Stage.Create);
            return;
        }
        _autoPlay = _options.Play || _options.Walk;
    }

    /// <summary>Test runs enter the world on their own, once the game files are in.</summary>
    private void AutoPlay()
    {
        _autoPlay = false;
        if (_select.Slots[0] == null)
            Send(PacketType.CreateCharacter, w => { w.Put((byte)0); w.Put(_options.Name); TestLook().Write(w); });
        else
            Send(PacketType.PlayCharacter, w => w.Put((byte)0));
    }

    /// <summary>Random look for test characters, including race and beard so a crowd of them shows the art off.</summary>
    private static Appearance TestLook()
    {
        var o = CharacterOptions.Current;
        var rng = Random.Shared;
        return Appearance.Random(rng) with
        {
            Race = (byte)rng.Next(Math.Max(1, o.Races.Length)),
            Beard = (byte)(rng.Next(3) == 0 ? rng.Next(Math.Max(1, o.Beards.Length)) : 0),
        };
    }

    // ---- Update ----

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _stageSeconds += dt;
        _input.Update(IsActive && _options.Screenshot == null);
        _sprites.Trim();
        _connection.Poll();
        ApplyAssetChanges();
        _camera.Viewport = Screen;

        if (!_connection.IsConnected && !_connecting)
        {
            _retryIn -= dt;
            if (_retryIn <= 0) Connect();
        }

        switch (_stage)
        {
            case Stage.Login: UpdateLogin(dt); break;
            case Stage.Select: UpdateSelect(dt); break;
            case Stage.Create: UpdateCreate(dt); break;
            case Stage.World: UpdateWorld(dt); break;
        }

        base.Update(gameTime);
    }

    private void UpdateLogin(float dt)
    {
        switch (_login.Update(_input, Screen, dt))
        {
            case LoginAction.Login:
                Send(PacketType.Login, w => { w.Put(_login.User); w.Put(_login.Password); });
                break;
            case LoginAction.Register:
                Send(PacketType.Register, w => { w.Put(_login.User); w.Put(_login.Password); });
                break;
        }
    }

    private void UpdateSelect(float dt)
    {
        if (_autoPlay && !_assets.Busy) AutoPlay();
        var action = _select.Update(_input, Screen, dt);
        var slot = (byte)_select.Slot;
        if (_assets.Busy && action is SelectAction.Play or SelectAction.Create)
        {
            _select.Message = "Aguarde: ainda chegando arquivos do jogo.";
            return;
        }
        switch (action)
        {
            case SelectAction.Play:
                Send(PacketType.PlayCharacter, w => w.Put(slot));
                break;
            case SelectAction.Create:
                _creation = new CreationScreen(_sprites, _items, "");
                GoTo(Stage.Create);
                break;
            case SelectAction.Delete:
                Send(PacketType.DeleteCharacter, w => w.Put(slot));
                break;
            case SelectAction.Logout:
                _loggingOut = true;
                _connection.Disconnect();
                break;
        }
    }

    private void UpdateCreate(float dt)
    {
        if (_creation == null) return;
        switch (_creation.Update(_input, Screen, dt))
        {
            case CreationAction.Confirm:
                var slot = (byte)_select.Slot;
                var name = _creation.Name;
                var look = _creation.Look;
                _creation.Message = "";
                Send(PacketType.CreateCharacter, w => { w.Put(slot); w.Put(name); look.Write(w); });
                break;
            case CreationAction.Cancel:
                _creation = null;
                GoTo(Stage.Select);
                break;
        }
    }

    private void UpdateWorld(float dt)
    {
        if (_pendingImport != null && _access > 0 && _stageSeconds > 1)
        {
            _import.Show(_pendingImport);
            _pendingImport = null;
        }

        // The game editor and the import dialog take the keyboard and mouse while open; the world keeps going behind.
        if (_gameEditor.Open || _import.Open)
        {
            if (_import.Open && _input.Pressed(Keys.Escape)) _import.Close();
            else if (_gameEditor.Open && (_input.Pressed(Keys.F2) || _input.Pressed(Keys.Escape) && !_gui.Typing)) _gameEditor.Toggle();
            if (_map == null || _local == null) return;
            _local.Update(dt * 1000);
            foreach (var other in _others.Values) other.Update(dt * 1000);
            foreach (var (view, _) in _npcs.Values) view.Update(dt * 1000);
            _floating.Update(dt);
            _camera.Follow(_local.Position + new Vector2(Constants.TileSize / 2f), new Vector2(_map.Width, _map.Height) * Constants.TileSize);
            return;
        }
        if (_input.Pressed(Keys.F2) && !_chat.Typing)
        {
            if (_access > 0) _gameEditor.Toggle();
            else _chat.Add("", "Só administradores podem editar o jogo.");
        }

        // Typing in a map editor field: keys are text, not movement or shortcuts.
        var typingInPanel = _editor.Active && _gui.Typing;
        if (!typingInPanel) HandleHotkeys();
        if (_map == null || _local == null) return;

        if (typingInPanel) _local.Update(dt * 1000);
        else UpdateLocal(dt * 1000);
        if (_options.Walk) TryOnRandomItem(dt);
        foreach (var other in _others.Values) other.Update(dt * 1000);
        foreach (var (view, _) in _npcs.Values) view.Update(dt * 1000);
        _floating.Update(dt);
        if (!typingInPanel) UpdateAttack(dt);

        if (_editor.Update(_input, _map, _camera, Screen, _npcDefs) && _editor.Dirty) SaveMap();
        if (_inventory.Update(_input, Screen) is { } clicked) Send(PacketType.EquipToggle, w => w.Put(clicked));
        _camera.Follow(_local.Position + new Vector2(Constants.TileSize / 2f), new Vector2(_map.Width, _map.Height) * Constants.TileSize);
    }

    /// <summary>Commands the client handles itself. Returns true when the text was one.</summary>
    private bool LocalCommand(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not ["/destino", ..]) return false;

        if (parts.Length == 4 && int.TryParse(parts[1], out var map) && int.TryParse(parts[2], out var x) && int.TryParse(parts[3], out var y))
        {
            _editor.WarpTarget = new Warp { Map = map, ToX = x, ToY = y };
            _chat.Add("", $"Teleportes pintados levam ao mapa {map} ({x}, {y}).");
        }
        else
        {
            _chat.Add("", "Use /destino <mapa> <x> <y>, depois B até Teleporte no editor.");
        }
        return true;
    }

    private void HandleHotkeys()
    {
        if (_chat.Typing)
        {
            if (_input.Pressed(Keys.Escape)) _chat.Cancel();
            if (_input.Pressed(Keys.Enter) && _chat.Submit() is { } text)
            {
                if (!LocalCommand(text)) Send(PacketType.ChatSend, w => w.Put(text));
            }
            return;
        }

        if (_input.Pressed(Keys.Enter)) _chat.Open();
        if (_input.Pressed(Keys.F1) && _map != null)
        {
            if (_access == 0 && !_editor.Active) _chat.Add("", "Só administradores podem editar mapas.");
            else
            {
                _editor.Toggle(_map);
                _inventory.Open = false;
            }
        }
        if (_input.Pressed(Keys.I) && _local != null)
        {
            _inventory.Open = !_inventory.Open;
            if (_inventory.Open && _editor.Active && _map != null) _editor.Toggle(_map);
        }
        if (_input.Pressed(Keys.F3)) _night = !_night;

        var mouse = _input.Mouse.ToPoint();
        var overPanel = (_editor.Active && _editor.PanelRect(Screen).Contains(mouse))
                        || (_inventory.Open && _inventory.PanelRect(Screen).Contains(mouse));
        var zoom = (_input.Pressed(Keys.OemPlus) || _input.Pressed(Keys.Add) ? 1 : 0)
                   - (_input.Pressed(Keys.OemMinus) || _input.Pressed(Keys.Subtract) ? 1 : 0)
                   + (overPanel ? 0 : _input.Wheel);
        if (zoom != 0) _camera.Zoom = Math.Clamp(_camera.Zoom + zoom, Camera.MinZoom, Camera.MaxZoom);
    }

    private void UpdateLocal(float elapsedMs)
    {
        var local = _local!;
        var leftover = local.Update(elapsedMs);
        if (local.Moving || _chat.Typing || ReadDirection() is not { } dir) return;

        var (dx, dy) = dir.Delta();
        var tx = local.TileX + dx;
        var ty = local.TileY + dy;
        if (!_map!.InBounds(tx, ty) && _map.Links[dir] > 0)
        {
            local.Dir = dir;
            if (_awaitingTransfer) return;
            _awaitingTransfer = true;
            var (ex, ey) = (local.TileX, local.TileY);
            Send(PacketType.MoveRequest, w => { w.Put((byte)dir); w.Put(ex); w.Put(ey); });
            return;
        }
        if (!_map.IsWalkable(tx, ty))
        {
            local.Dir = dir;
            return;
        }

        var (fromX, fromY) = (local.TileX, local.TileY);
        Send(PacketType.MoveRequest, w => { w.Put((byte)dir); w.Put(fromX); w.Put(fromY); });

        local.StartMove(dir, tx, ty);
        // Carry the time past the end of the last step so continuous walking stays smooth.
        local.Update(leftover);
    }

    private Direction? ReadDirection()
    {
        if (_options.Walk) return Wander();

        if (_input.Down(Keys.Up) || _input.Down(Keys.W)) return Direction.Up;
        if (_input.Down(Keys.Down) || _input.Down(Keys.S) && !_input.Ctrl) return Direction.Down;
        if (_input.Down(Keys.Left) || _input.Down(Keys.A)) return Direction.Left;
        if (_input.Down(Keys.Right) || _input.Down(Keys.D)) return Direction.Right;
        return null;
    }

    private float _tryOnIn = 1;

    /// <summary>Test characters put on random things from their inventory every few seconds.</summary>
    private void TryOnRandomItem(float dt)
    {
        _tryOnIn -= dt;
        if (_tryOnIn > 0 || _inventory.Inventory.Count == 0) return;
        _tryOnIn = 1.5f + Random.Shared.NextSingle() * 3;
        var item = _inventory.Inventory[Random.Shared.Next(_inventory.Inventory.Count)];
        if (_items.Get(item) is { } def && _local!.Equipment[def.Slot] != item)
            Send(PacketType.EquipToggle, w => w.Put(item));
    }

    /// <summary>Space or Ctrl attacks what is in front, as often as the attack speed allows while held.</summary>
    private void UpdateAttack(float dt)
    {
        _nextAttackIn -= dt;
        if (_nextAttackIn > 0 || _chat.Typing || _local == null || _local.Moving) return;

        var wants = _input.Down(Keys.Space) || _input.Down(Keys.LeftControl) && !_input.Down(Keys.S);
        if (_options.Walk) wants = FaceAdjacentMonster();
        if (!wants) return;

        _nextAttackIn = Formulas.PlayerAttackMs / 1000f;
        _local.Swing(_local.Dir);
        var dir = _local.Dir;
        Send(PacketType.Attack, w => w.Put((byte)dir));
    }

    /// <summary>Test characters turn to a hostile NPC standing next to them, and say whether they found one.</summary>
    private bool FaceAdjacentMonster()
    {
        foreach (var (view, def) in _npcs.Values)
        {
            if (!def.Hostile) continue;
            var (dx, dy) = (view.TileX - _local!.TileX, view.TileY - _local.TileY);
            if (Math.Abs(dx) + Math.Abs(dy) != 1) continue;
            _local.Dir = dx > 0 ? Direction.Right : dx < 0 ? Direction.Left : dy > 0 ? Direction.Down : Direction.Up;
            return true;
        }
        return false;
    }

    private Direction? Wander()
    {
        _wanderSeconds -= (float)TargetElapsedTime.TotalSeconds;
        if (_wanderSeconds <= 0)
        {
            _wanderSeconds = Random.Shared.NextSingle() * 1.5f + 0.3f;
            _wanderDir = Random.Shared.Next(5) == 0 ? null : (Direction)Random.Shared.Next(4);
        }
        return _wanderDir;
    }

    private void SaveMap()
    {
        Send(PacketType.MapSave, w => w.PutBlob(_map!.ToBytes()));
        _editor.Dirty = false;
    }

    // ---- Draw ----

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Theme.Background);

        var inWorld = _stage == Stage.World && _map != null && _local != null;
        if (inWorld) DrawWorld(_map!, _local!);

        _batch.Begin(samplerState: SamplerState.PointClamp);
        switch (_stage)
        {
            case Stage.Login:
                _login.Draw(_batch, _textures.Pixel, _fonts, Screen);
                break;
            case Stage.Select:
                _select.Draw(_batch, _textures.Pixel, _fonts, Screen);
                DrawDownload();
                break;
            case Stage.Create:
                _creation?.Draw(_batch, _textures.Pixel, _fonts, Screen);
                break;
            case Stage.World when inWorld:
                DrawNames();
                DrawStatusBar(_map!);
                _hud.Draw(_batch, _textures.Pixel, _fonts);
                _chat.Draw(_batch, _textures.Pixel, _fonts, Screen);
                _inventory.Draw(_batch, _textures.Pixel, _fonts, _local!, Screen);
                if (_gameEditor.Open || _editor.Active || _import.Open)
                {
                    var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                    _gui.Begin(_batch, dt);
                    if (_editor.Active) _editor.DrawPanel(_batch, _map!, Screen, _local!, _npcDefs);
                    if (_gameEditor.Open) _gameEditor.Draw(Screen, _local!.Look, dt);
                    if (_import.Open) _import.Draw(Screen);
                    _gui.End();
                }
                break;
        }
        _batch.End();

        base.Draw(gameTime);

        // Automated runs photograph the world once they are in it, or wherever they stopped otherwise.
        var shotReady = _stage == Stage.World ? _stageSeconds > 4 : _stageSeconds > 3 && !_options.Play && !_options.Walk;
        if (_options.Screenshot != null && shotReady) SaveScreenshot(_options.Screenshot);
    }

    private void SaveScreenshot(string path)
    {
        var screen = Screen;
        var data = new Color[screen.X * screen.Y];
        GraphicsDevice.GetBackBufferData(data);
        using var texture = new Texture2D(GraphicsDevice, screen.X, screen.Y);
        texture.SetData(data);
        using (var file = File.Create(path)) texture.SaveAsPng(file, screen.X, screen.Y);
        Exit();
    }

    private void DrawWorld(MapData map, Character local)
    {
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, transformMatrix: _camera.Transform);

        _renderer.DrawLayers(_batch, map, _camera, 0, map.FringeFrom);
        var units = _others.Values.Append(local).Select(c => (c, sheet: _sprites.SheetFor(c.Look, c.Equipment)))
            .Concat(_npcs.Values.Select(n => (n.view, sheet: NpcSheet(n.def))));
        foreach (var (character, sheet) in units.OrderBy(u => u.Item1.Position.Y))
            _renderer.DrawCharacter(_batch, character, sheet);
        _renderer.DrawLayers(_batch, map, _camera, map.FringeFrom, map.Layers.Length);
        _editor.DrawWorld(_batch, map, _camera, _npcDefs, NpcSheet);

        _batch.End();

        if (_night && !_editor.Active) DrawNight(local);
    }

    /// <summary>A dark tint over the world with a soft hole of light around the player.</summary>
    private void DrawNight(Character local)
    {
        var tint = Theme.Night * Theme.NightStrength;
        var center = _camera.WorldToScreen(local.Position + new Vector2(Constants.TileSize / 2f));
        var half = LightInner * _camera.Zoom / MaskInner;
        var rect = new Rectangle((int)(center.X - half), (int)(center.Y - half), (int)(half * 2), (int)(half * 2));
        var screen = Screen;
        var pixel = _textures.Pixel;

        _batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.LinearClamp);
        _batch.Draw(_lightMask, rect, tint);
        // The mask only covers a square; cover whatever of the screen lies outside it.
        _batch.Draw(pixel, new Rectangle(0, 0, screen.X, Math.Max(0, rect.Top)), tint);
        _batch.Draw(pixel, new Rectangle(0, rect.Bottom, screen.X, Math.Max(0, screen.Y - rect.Bottom)), tint);
        _batch.Draw(pixel, new Rectangle(0, rect.Top, Math.Max(0, rect.Left), rect.Height), tint);
        _batch.Draw(pixel, new Rectangle(rect.Right, rect.Top, Math.Max(0, screen.X - rect.Right), rect.Height), tint);
        _batch.End();
    }

    private void DrawNames()
    {
        foreach (var character in _others.Values.Append(_local!))
            NameAndBar(character, _sprites.SheetFor(character.Look, character.Equipment), character == _local ? Theme.Sol : Theme.Text);
        foreach (var (view, def) in _npcs.Values)
            NameAndBar(view, NpcSheet(def), def.Hostile ? Theme.Danger : Theme.TextDim);
        _floating.Draw(_batch, _fonts, _camera.WorldToScreen);
    }

    /// <summary>The name over a unit's head, with a health bar under it once it has been hurt.</summary>
    private void NameAndBar(Character unit, Texture2D sheet, Color color)
    {
        var top = unit.Position + SheetLayout.Offset(sheet) + new Vector2(0, SheetLayout.HeadTop(sheet));
        var head = _camera.WorldToScreen(new Vector2(unit.Position.X + Constants.TileSize / 2f, top.Y - 3)) - new Vector2(0, 8);
        Ui.Tag(_batch, _textures.Pixel, _fonts.Small, unit.Name, head, color);
        if (unit.MaxHp <= 0 || unit.Hp >= unit.MaxHp) return;
        var bar = new Rectangle((int)head.X - 18, (int)head.Y + 9, 36, 4);
        _batch.Draw(_textures.Pixel, bar, Theme.Background);
        _batch.Draw(_textures.Pixel, new Rectangle(bar.X, bar.Y, bar.Width * Math.Max(0, unit.Hp) / unit.MaxHp, bar.Height), Theme.Danger);
    }

    /// <summary>An NPC's sheet: its paper-doll look, its sprite file, or a placeholder.</summary>
    private Texture2D NpcSheet(NpcDef def) =>
        def.Look != null ? _sprites.SheetFor(def.Look, NpcEquipment(def))
        : _textures.Sheet(def.Sprite) ?? _textures.Character(def.Id);

    private Equipment NpcEquipment(NpcDef def)
    {
        var equipment = new Equipment();
        foreach (var id in def.Wears)
        {
            if (_items.Get(id) is { } item) equipment[item.Slot] = id;
        }
        return equipment;
    }

    /// <summary>A bar at the bottom while game files are still arriving from the server.</summary>
    private void DrawDownload()
    {
        if (!_assets.Busy || _assets.BytesTotal == 0) return;
        var done = 1f - (float)_assets.BytesLeft / _assets.BytesTotal;
        var bar = new Rectangle(Screen.X / 2 - 200, Screen.Y - 60, 400, 8);
        _batch.Draw(_textures.Pixel, bar, Theme.Panel);
        _batch.Draw(_textures.Pixel, bar with { Width = (int)(bar.Width * done) }, Theme.Luna);
        var text = $"Baixando arquivos do jogo: {_assets.BytesLeft / 1024} KB restantes";
        var size = _fonts.Small.MeasureString(text);
        Ui.Text(_batch, _fonts.Small, text, new Vector2(Screen.X / 2f - size.X / 2, bar.Y - 22), Theme.TextDim);
    }

    /// <summary>Files that arrived or were imported: caches holding the old ones let go, editors list them.</summary>
    private void ApplyAssetChanges()
    {
        var changed = _assets.TakeChanged();
        if (changed.Count == 0) return;
        foreach (var path in changed) _textures.Forget(path);
        if (changed.Any(p => p.StartsWith("characters/"))) _sprites.Reload();
        _gameEditor.AssetsChanged();
        if (_map != null && _editor.Active) _editor.RefreshPalette(_map);
    }

    private void DrawStatusBar(MapData map)
    {
        var pixel = _textures.Pixel;
        var online = _others.Count + 1;
        var moral = map.Moral == MapMoral.Pvp ? "  ·  PvP" : "";
        var info = $"{map.Name}{moral}  ·  {online} online  ·  {_connection.PingMs} ms";
        var width = (int)(_fonts.Title.MeasureString(Constants.GameName).X + _fonts.Body.MeasureString(info).X) + 44;

        var bar = new Rectangle(12, 12, width, 32);
        Ui.Panel(_batch, pixel, bar);
        Ui.Text(_batch, _fonts.Title, Constants.GameName, new Vector2(bar.X + 12, bar.Y + 2), Theme.Luna);
        Ui.Text(_batch, _fonts.Body, info, new Vector2(bar.X + 28 + _fonts.Title.MeasureString(Constants.GameName).X, bar.Y + 7), Theme.TextDim);

        if (_editor.Active || _inventory.Open) return;
        var hint = _access > 0
            ? "Enter chat  ·  I equipamento  ·  F1 mapa  ·  F2 jogo  ·  F3 noite  ·  +/- zoom"
            : "Enter chat  ·  I equipamento  ·  F3 noite  ·  +/- zoom";
        var size = _fonts.Small.MeasureString(hint);
        var screen = Screen;
        Ui.Text(_batch, _fonts.Small, hint, new Vector2(screen.X - size.X - 14, screen.Y - size.Y - 12), Theme.TextDim);
    }
}

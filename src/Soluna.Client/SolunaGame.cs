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
internal sealed record ClientOptions(
    string Host, int Port, string Name, string? User = null, string? Password = null, bool Play = false,
    string? Screenshot = null, bool Walk = false, bool Editor = false, bool Inventory = false, bool Creation = false);

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
                case Stage.World: _chat.OnTextInput(e.Character); break;
            }
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
        _editor = new MapEditor(_textures, _renderer);
        _inventory = new InventoryPanel(_items, _sprites);
        _login = new LoginScreen(_options.User ?? "");
        _select = new SelectScreen(_sprites);
        _lightMask = PlaceholderArt.LightMask(GraphicsDevice, MaskInner, MaskOuter);
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

            case PacketType.ItemCatalog:
                _items.Replace(r.GetString());
                _sprites.Clear();
                break;

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
            case PacketType.MapLoad:
                _map = MapData.FromBytes(r.GetBlob());
                _editor.Dirty = false;
                if (_options.Editor && !_editor.Active) _editor.Toggle(_map);
                break;

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
                _local?.Place(r.GetInt(), r.GetInt(), (Direction)r.GetByte());
                break;

            case PacketType.ChatMessage:
                _chat.Add(r.GetString(), r.GetString());
                break;

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
        if (!_options.Play && !_options.Walk) return;
        if (_select.Slots[0] == null)
            Send(PacketType.CreateCharacter, w => { w.Put((byte)0); w.Put(_options.Name); Appearance.Random(Random.Shared).Write(w); });
        else
            Send(PacketType.PlayCharacter, w => w.Put((byte)0));
    }

    // ---- Update ----

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _stageSeconds += dt;
        _input.Update(IsActive && _options.Screenshot == null);
        _sprites.Trim();
        _connection.Poll();
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
        var action = _select.Update(_input, Screen, dt);
        var slot = (byte)_select.Slot;
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
        HandleHotkeys();
        if (_map == null || _local == null) return;

        UpdateLocal(dt * 1000);
        foreach (var other in _others.Values) other.Update(dt * 1000);

        if (_editor.Update(_input, _map, _camera, Screen) && _editor.Dirty) SaveMap();
        if (_inventory.Update(_input, Screen) is { } clicked) Send(PacketType.EquipToggle, w => w.Put(clicked));
        _camera.Follow(_local.Position + new Vector2(Constants.TileSize / 2f), new Vector2(_map.Width, _map.Height) * Constants.TileSize);
    }

    private void HandleHotkeys()
    {
        if (_chat.Typing)
        {
            if (_input.Pressed(Keys.Escape)) _chat.Cancel();
            if (_input.Pressed(Keys.Enter) && _chat.Submit() is { } text) Send(PacketType.ChatSend, w => w.Put(text));
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
        if (!_map!.IsWalkable(tx, ty))
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
                break;
            case Stage.Create:
                _creation?.Draw(_batch, _textures.Pixel, _fonts, Screen);
                break;
            case Stage.World when inWorld:
                DrawNames();
                DrawStatusBar(_map!);
                _chat.Draw(_batch, _textures.Pixel, _fonts, Screen);
                _editor.DrawPanel(_batch, _fonts, _map!, Screen);
                _inventory.Draw(_batch, _textures.Pixel, _fonts, _local!, Screen);
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
        foreach (var character in _others.Values.Append(local).OrderBy(c => c.Position.Y))
            _renderer.DrawCharacter(_batch, character);
        _renderer.DrawLayers(_batch, map, _camera, map.FringeFrom, map.Layers.Length);
        _editor.DrawWorld(_batch, map, _camera);

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
        {
            var sheet = _sprites.SheetFor(character.Look, character.Equipment);
            var top = character.Position + SheetLayout.Offset(sheet) + new Vector2(0, SheetLayout.HeadTop(sheet));
            var head = new Vector2(character.Position.X + Constants.TileSize / 2f, top.Y - 3);
            var color = character == _local ? Theme.Sol : Theme.Text;
            Ui.Tag(_batch, _textures.Pixel, _fonts.Small, character.Name, _camera.WorldToScreen(head) - new Vector2(0, 8), color);
        }
    }

    private void DrawStatusBar(MapData map)
    {
        var pixel = _textures.Pixel;
        var online = _others.Count + 1;
        var info = $"{map.Name}  ·  {online} online  ·  {_connection.PingMs} ms";
        var width = (int)(_fonts.Title.MeasureString(Constants.GameName).X + _fonts.Body.MeasureString(info).X) + 44;

        var bar = new Rectangle(12, 12, width, 32);
        Ui.Panel(_batch, pixel, bar);
        Ui.Text(_batch, _fonts.Title, Constants.GameName, new Vector2(bar.X + 12, bar.Y + 2), Theme.Luna);
        Ui.Text(_batch, _fonts.Body, info, new Vector2(bar.X + 28 + _fonts.Title.MeasureString(Constants.GameName).X, bar.Y + 7), Theme.TextDim);

        if (_editor.Active || _inventory.Open) return;
        var hint = _access > 0
            ? "Enter chat  ·  I equipamento  ·  F1 editor  ·  F3 noite  ·  +/- zoom"
            : "Enter chat  ·  I equipamento  ·  F3 noite  ·  +/- zoom";
        var size = _fonts.Small.MeasureString(hint);
        var screen = Screen;
        Ui.Text(_batch, _fonts.Small, hint, new Vector2(screen.X - size.X - 14, screen.Y - size.Y - 12), Theme.TextDim);
    }
}

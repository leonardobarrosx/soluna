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

/// <param name="Screenshot">Debug: save a PNG of the screen to this path a few seconds after entering, then quit.</param>
/// <param name="Walk">Debug: wander around on its own, for testing with several clients.</param>
/// <param name="Editor">Open the map editor as soon as the map arrives.</param>
/// <param name="SkipCreation">Skip character creation with a random look (implied by Walk).</param>
/// <param name="Inventory">Open the equipment panel on entry.</param>
internal sealed record ClientOptions(
    string Host, int Port, string Name, string? Screenshot = null, bool Walk = false, bool Editor = false,
    bool SkipCreation = false, bool Inventory = false);

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
    private readonly ItemCatalog _items = ItemCatalog.Load();

    private SpriteBatch _batch = null!;
    private Textures _textures = null!;
    private Fonts _fonts = null!;
    private MapRenderer _renderer = null!;
    private MapEditor _editor = null!;
    private Sprites _sprites = null!;
    private CreationScreen? _creation;
    private InventoryPanel _inventory = null!;
    private Appearance? _look;
    private string _name;
    private Texture2D _lightMask = null!;

    private MapData? _map;
    private Character? _local;
    private bool _connecting;
    private float _retryIn;
    private string _status = "";
    private bool _night = true;
    private float _inWorldSeconds;
    private float _runSeconds;
    private Direction? _wanderDir;
    private float _wanderSeconds;

    public SolunaGame(ClientOptions options)
    {
        _options = options;
        _name = options.Name;
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
            if (_creation != null) _creation.OnTextInput(e.Character);
            else _chat.OnTextInput(e.Character);
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
        _lightMask = PlaceholderArt.LightMask(GraphicsDevice, MaskInner, MaskOuter);

        if (_options.SkipCreation || _options.Walk)
        {
            _look = Appearance.Random(Random.Shared);
            Connect();
        }
        else
        {
            _creation = new CreationScreen(_sprites, _items, _options.Name);
        }
    }

    protected override void UnloadContent() => _connection.Stop();

    private Point Screen => new(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

    // ---- Networking ----

    private void Connect()
    {
        _connecting = true;
        _status = $"Conectando a {_options.Host}:{_options.Port}...";
        _connection.Connect(_options.Host, _options.Port);
    }

    private void OnConnected()
    {
        _connecting = false;
        _status = "Entrando...";
        var login = PacketIO.Begin(PacketType.Login);
        login.Put(_name);
        _look!.Write(login);
        _connection.Send(login);
    }

    private void OnDisconnected(string reason)
    {
        var wasPlaying = _local != null;
        _connecting = false;
        _map = null;
        _local = null;
        _others.Clear();
        _retryIn = RetrySeconds;
        _status = wasPlaying ? $"Conexão perdida ({reason}). Tentando de novo..." : "Servidor indisponível. Tentando de novo...";
    }

    private void OnPacket(PacketType type, NetDataReader r)
    {
        switch (type)
        {
            case PacketType.LoginOk:
            {
                var me = r.GetPlayerInfo();
                _local = new Character(me.Id, me.Name, me.Look, me.Equipment);
                _local.Place(me.X, me.Y, me.Dir);
                _inventory.Inventory.Clear();
                var count = r.GetInt();
                for (var i = 0; i < count; i++) _inventory.Inventory.Add(r.GetInt());
                _inventory.Open = _options.Inventory;
                Window.Title = $"{Constants.GameName} · {me.Name}";
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

    // ---- Update ----

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _runSeconds += dt;
        _input.Update(IsActive && _options.Screenshot == null);
        _sprites.Trim();

        if (_creation != null)
        {
            if (_creation.Update(_input, Screen, dt))
            {
                _look = _creation.Look;
                _name = _creation.Name;
                _creation = null;
                Connect();
            }
            base.Update(gameTime);
            return;
        }

        _connection.Poll();
        _camera.Viewport = Screen;

        if (!_connection.IsConnected && !_connecting)
        {
            _retryIn -= dt;
            if (_retryIn <= 0) Connect();
        }

        HandleHotkeys();

        if (_map != null && _local != null)
        {
            _inWorldSeconds += dt;
            UpdateLocal(dt * 1000);
            foreach (var other in _others.Values) other.Update(dt * 1000);

            if (_editor.Update(_input, _map, _camera, Screen) && _editor.Dirty) SaveMap();
            if (_inventory.Update(_input, Screen) is { } clicked)
            {
                var w = PacketIO.Begin(PacketType.EquipToggle);
                w.Put(clicked);
                _connection.Send(w);
            }
            _camera.Follow(_local.Position + new Vector2(Constants.TileSize / 2f), new Vector2(_map.Width, _map.Height) * Constants.TileSize);
        }

        base.Update(gameTime);
    }

    private void HandleHotkeys()
    {
        if (_chat.Typing)
        {
            if (_input.Pressed(Keys.Escape)) _chat.Cancel();
            if (_input.Pressed(Keys.Enter))
            {
                var text = _chat.Submit();
                if (text != null && _connection.IsConnected)
                {
                    var w = PacketIO.Begin(PacketType.ChatSend);
                    w.Put(text);
                    _connection.Send(w);
                }
            }
            return;
        }

        if (_input.Pressed(Keys.Enter)) _chat.Open();
        if (_input.Pressed(Keys.F1) && _map != null)
        {
            _editor.Toggle(_map);
            _inventory.Open = false;
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

        var w = PacketIO.Begin(PacketType.MoveRequest);
        w.Put((byte)dir);
        w.Put(local.TileX);
        w.Put(local.TileY);
        _connection.Send(w);

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
        var w = PacketIO.Begin(PacketType.MapSave);
        w.PutBlob(_map!.ToBytes());
        _connection.Send(w);
        _editor.Dirty = false;
    }

    // ---- Draw ----

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Theme.Background);

        if (_map != null && _local != null) DrawWorld(_map, _local);

        _batch.Begin(samplerState: SamplerState.PointClamp);
        if (_creation != null)
        {
            _creation.Draw(_batch, _textures.Pixel, _fonts, Screen);
        }
        else if (_map != null && _local != null)
        {
            DrawNames();
            DrawStatusBar(_map);
            _chat.Draw(_batch, _textures.Pixel, _fonts, Screen);
            _editor.DrawPanel(_batch, _fonts, _map, Screen);
            _inventory.Draw(_batch, _textures.Pixel, _fonts, _local, Screen);
        }
        else
        {
            DrawConnecting();
        }
        _batch.End();

        base.Draw(gameTime);

        var shotReady = _creation != null ? _runSeconds > 3 : _inWorldSeconds > 4;
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

        _renderer.DrawLayers(_batch, map, _camera, 0, MapData.FirstFringeLayer);
        foreach (var character in _others.Values.Append(local).OrderBy(c => c.Position.Y))
            _renderer.DrawCharacter(_batch, character);
        _renderer.DrawLayers(_batch, map, _camera, MapData.FirstFringeLayer, MapData.LayerCount);
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

        if (_editor.Active) return;
        if (_inventory.Open) return;
        const string hint = "Enter chat  ·  I equipamento  ·  F1 editor  ·  F3 noite  ·  +/- zoom";
        var size = _fonts.Small.MeasureString(hint);
        var screen = Screen;
        Ui.Text(_batch, _fonts.Small, hint, new Vector2(screen.X - size.X - 14, screen.Y - size.Y - 12), Theme.TextDim);
    }

    private void DrawConnecting()
    {
        var screen = Screen;
        var title = _fonts.Title.MeasureString(Constants.GameName);
        var status = _fonts.Body.MeasureString(_status);
        var center = new Vector2(screen.X / 2f, screen.Y / 2f);
        Ui.Text(_batch, _fonts.Title, Constants.GameName, center - new Vector2(title.X / 2, title.Y + 6), Theme.Luna);
        Ui.Text(_batch, _fonts.Body, _status, center - new Vector2(status.X / 2, -6), Theme.TextDim);
    }
}

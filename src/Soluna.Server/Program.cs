using Soluna.Server;
using Soluna.Shared;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// --game <name> serves that game from games/<name>; --new-game <name> [--from <template>] creates one and exits.
string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
if (Arg("--new-game") is { } newGame)
{
    DataPaths.CreateGame(newGame, Arg("--from") ?? DataPaths.DefaultGame);
    Log.Info($"Created game '{newGame}' in {Path.Combine(DataPaths.GamesRoot, newGame)}.");
    return;
}
if (Arg("--game") is { } game) DataPaths.UseGame(game);
if (!Directory.Exists(DataPaths.GameFolder))
{
    Log.Warn($"Game '{DataPaths.Game}' not found in {DataPaths.GamesRoot}. Games: {string.Join(", ", DataPaths.Games())}.");
    return;
}
Log.Info($"Game '{DataPaths.Game}' ({DataPaths.GameFolder}).");

var maps = new MapStore(Path.Combine(DataPaths.Data, "maps"));

// Soluna.Server import <map.tmx> <id> [name]: bring a Tiled map in, then exit.
if (args is ["import", var tmx, var idText, ..] && int.TryParse(idText, out var importId))
{
    maps.Import(tmx, importId, args.Length > 3 ? string.Join(' ', args[3..]) : Path.GetFileNameWithoutExtension(tmx));
    return;
}

var port = args.FirstOrDefault(a => int.TryParse(a, out _)) is { } portText ? int.Parse(portText) : Constants.DefaultPort;
var items = ItemCatalog.Load();
Log.Info($"Loaded {items.All.Count()} items.");
var accounts = new AccountStore(Path.Combine(DataPaths.Data, "accounts"));
// --all-items: new characters get the whole wardrobe (development).
var npcs = NpcCatalog.Load();
Log.Info($"Loaded {npcs.All.Count()} NPC definitions.");
var server = new GameServer(maps, items, accounts, npcs) { StarterGetsAllItems = args.Contains("--all-items") };

var running = true;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    running = false;
};

server.Start(port);
Log.Info($"{Constants.GameName} server listening on UDP {port}. Ctrl+C to stop.");

// Network events as they come; world steps at a fixed rate, catching up if a step ran late.
var clock = System.Diagnostics.Stopwatch.StartNew();
var step = TimeSpan.FromSeconds(1.0 / GameServer.TicksPerSecond);
var next = clock.Elapsed;
while (running)
{
    server.Poll();
    var caughtUp = 0;
    while (clock.Elapsed >= next && caughtUp++ < 5)
    {
        server.Tick();
        next += step;
    }
    if (clock.Elapsed > next + step * 5) next = clock.Elapsed; // fell far behind: skip ahead rather than spiral
    Thread.Sleep(5);
}

server.Stop();
Log.Info("Server stopped.");

using Soluna.Server;
using Soluna.Shared;

var maps = new MapStore(Path.Combine(DataPaths.Data, "maps"));

// Soluna.Server import <map.tmx> <id> [name]: bring a Tiled map in, then exit.
if (args is ["import", var tmx, var idText, ..] && int.TryParse(idText, out var importId))
{
    maps.Import(tmx, importId, args.Length > 3 ? string.Join(' ', args[3..]) : Path.GetFileNameWithoutExtension(tmx));
    return;
}

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : Constants.DefaultPort;
var items = ItemCatalog.Load();
Log.Info($"Loaded {items.All.Count()} items.");
var accounts = new AccountStore(Path.Combine(DataPaths.Data, "accounts"));
var server = new GameServer(maps, items, accounts);

var running = true;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    running = false;
};

server.Start(port);
Log.Info($"{Constants.GameName} server listening on UDP {port}. Ctrl+C to stop.");

while (running)
{
    server.Poll();
    Thread.Sleep(15);
}

server.Stop();
Log.Info("Server stopped.");

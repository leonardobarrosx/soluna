using Soluna.Server;
using Soluna.Shared;

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : Constants.DefaultPort;

var maps = new MapStore(Path.Combine(DataPaths.Data, "maps"));
var server = new GameServer(maps);

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

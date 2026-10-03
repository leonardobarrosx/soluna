using Soluna.Client;
using Soluna.Shared;

// Usage: Soluna.Client [--host 127.0.0.1] [--port 7171] [--name Leo] [--walk] [--editor] [--screenshot shot.png]
var host = "127.0.0.1";
var port = Constants.DefaultPort;
var name = Environment.UserName;
string? screenshot = null;
var walk = args.Contains("--walk");
var editor = args.Contains("--editor");

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--host": host = args[++i]; break;
        case "--port": port = int.Parse(args[++i]); break;
        case "--name": name = args[++i]; break;
        case "--screenshot": screenshot = args[++i]; break;
    }
}

using var game = new SolunaGame(new ClientOptions(host, port, name, screenshot, walk, editor));
game.Run();

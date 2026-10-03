using Soluna.Client;
using Soluna.Shared;

// Usage: Soluna.Client [--host 127.0.0.1] [--port 7171]
//   [--user name --password secret [--play]]   log in on start, creating the account if needed
//   [--name Leo] [--walk] [--editor] [--inventory] [--screenshot shot.png]   for testing
var host = "127.0.0.1";
var port = Constants.DefaultPort;
string? name = null, user = null, password = null, screenshot = null;
var walk = args.Contains("--walk");
var play = args.Contains("--play") || walk;
var editor = args.Contains("--editor");
var inventory = args.Contains("--inventory");

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--host": host = args[++i]; break;
        case "--port": port = int.Parse(args[++i]); break;
        case "--name": name = args[++i]; break;
        case "--user": user = args[++i]; break;
        case "--password": password = args[++i]; break;
        case "--screenshot": screenshot = args[++i]; break;
    }
}

// Test runs with only a name get a matching local test account.
if (walk && user == null && name != null)
{
    user = name.ToLowerInvariant();
    password = $"{user}-local-test";
}

using var game = new SolunaGame(new ClientOptions(
    host, port, name ?? user ?? Environment.UserName, user, password, play, screenshot, walk, editor, inventory));
game.Run();

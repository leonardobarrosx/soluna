using System.Text.RegularExpressions;

namespace Soluna.Shared;

/// <summary>
/// Where things live. The engine can hold several games: each is a folder under games/ with its own
/// data/ (maps, items, NPCs, accounts) and assets/ (tilesets, characters, fonts). Server and client
/// pick one with --game; everything that reads content goes through <see cref="Data"/> and <see cref="Assets"/>.
/// </summary>
public static partial class DataPaths
{
    public const string DefaultGame = "soluna";

    /// <summary>
    /// The repository root (the folder holding Soluna.slnx), found by walking up from the
    /// executable, so paths resolve the same under dotnet run and a published build.
    /// Falls back to the executable folder.
    /// </summary>
    public static string Root { get; } = FindRoot();

    public static string GamesRoot => Path.Combine(Root, "games");

    /// <summary>The game being served or played, as its folder name.</summary>
    public static string Game { get; private set; } = Environment.GetEnvironmentVariable("SOLUNA_GAME") is { Length: > 0 } env ? env : DefaultGame;

    public static string GameFolder => Path.Combine(GamesRoot, Game);

    public static string Data => Path.Combine(GameFolder, "data");

    public static string Assets => Path.Combine(GameFolder, "assets");

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex GameName();

    public static bool IsValidGameName(string name) => GameName().IsMatch(name);

    /// <summary>Switches to another game. Call before anything reads content.</summary>
    public static void UseGame(string name)
    {
        if (!IsValidGameName(name)) throw new ArgumentException($"'{name}' is not a valid game name (letters, digits, - and _).");
        Game = name;
    }

    /// <summary>Every game folder under games/.</summary>
    public static IEnumerable<string> Games() =>
        Directory.Exists(GamesRoot) ? Directory.EnumerateDirectories(GamesRoot).Select(Path.GetFileName).OfType<string>().Order() : [];

    /// <summary>
    /// Creates a game by copying another one's shareable content: maps, items, NPCs and the art that may
    /// be redistributed. Private art, maps that use it and player accounts stay behind.
    /// </summary>
    public static void CreateGame(string name, string template = DefaultGame)
    {
        if (!IsValidGameName(name)) throw new ArgumentException($"'{name}' is not a valid game name.");
        var target = Path.Combine(GamesRoot, name);
        if (Directory.Exists(target)) throw new IOException($"The game '{name}' already exists.");
        var source = Path.Combine(GamesRoot, template);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Template game '{template}' not found.");

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Contains("private") || parts.Contains("accounts")) continue;
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        Directory.CreateDirectory(Path.Combine(target, "data", "maps"));
    }

    private static string FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("SOLUNA_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Soluna.slnx"))) return dir.FullName;
        }
        return AppContext.BaseDirectory;
    }
}

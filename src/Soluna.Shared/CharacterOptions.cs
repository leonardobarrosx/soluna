using System.Text.Json;
using LiteNetLib.Utils;

namespace Soluna.Shared;

/// <summary>One choice at character creation: an id (a layer name or a colour) and what the player sees.</summary>
public sealed record Choice(string Id, string Label, string[]? Parts = null);

/// <summary>
/// The choices offered at character creation, read from the active character art set's
/// options.json (see <see cref="CharacterArt"/>), so each art set brings its own bodies,
/// skins, races, hair and eyes. Server and client read the same file.
/// </summary>
public sealed class CharacterOptions
{
    /// <summary>
    /// Sheet for the body. {body} and {skin} are replaced by the chosen ids. When skins are
    /// colours ("#rrggbb") the template has no {skin} and the body is painted instead.
    /// </summary>
    public string Body { get; init; } = "body_{body}";

    public Choice[] Bodies { get; init; } = [];
    public Choice[] Skins { get; init; } = [];

    /// <summary>Extra layers per race, such as ears and tails; {skin} is replaced so they can match the skin.</summary>
    public Choice[] Races { get; init; } = [new("human", "Humano", [])];

    public Choice[] Hair { get; init; } = [];
    public Choice[] HairColors { get; init; } = [];
    public Choice[] Eyes { get; init; } = [];

    /// <summary>Beards, painted in the hair colour. An empty id means none.</summary>
    public Choice[] Beards { get; init; } = [new("", "Nenhuma")];

    /// <summary>
    /// Sheets worn when a slot is empty, by slot name, so taking clothes off never leaves a bare body
    /// in art sets whose bodies are drawn without any.
    /// </summary>
    public Dictionary<string, string> Defaults { get; init; } = [];

    /// <summary>How many of the first skins and hair colours a random character may get (the natural ones).</summary>
    public int RandomSkins { get; init; }
    public int RandomHairColors { get; init; }
    public int RandomEyes { get; init; }

    /// <summary>Names for the creation rows when the set means something else by them (fur for skin, tails for race).</summary>
    public Dictionary<string, string> Labels { get; init; } = [];

    public string Label(string row, string fallback) => Labels.GetValueOrDefault(row, fallback);

    private static CharacterOptions? _current;

    /// <summary>The options of the active art set, loaded once.</summary>
    public static CharacterOptions Current => _current ??= Load(Path.Combine(CharacterArt.Folder, "options.json"));

    public static CharacterOptions Load(string path)
    {
        if (!File.Exists(path)) return new CharacterOptions();
        var options = JsonSerializer.Deserialize<CharacterOptions>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return options ?? new CharacterOptions();
    }
}

/// <summary>
/// Which character art the engine uses: a private set in assets/characters/private/&lt;name&gt; when one is
/// installed (art that may be used but not redistributed), otherwise the chibi set that ships with the repo.
/// </summary>
public static class CharacterArt
{
    public static string Folder { get; } = Find();

    private static string Find()
    {
        var root = Path.Combine(DataPaths.Assets, "characters");
        var privateRoot = Path.Combine(root, "private");
        static bool Complete(string d) => File.Exists(Path.Combine(d, "catalog.json")) && File.Exists(Path.Combine(d, "options.json"));

        // The importers write the name of the set they built to private/active; that one wins.
        var activeFile = Path.Combine(privateRoot, "active");
        if (File.Exists(activeFile) && Path.Combine(privateRoot, File.ReadAllText(activeFile).Trim()) is var chosen && Complete(chosen))
            return chosen;

        var privateSet = Directory.Exists(privateRoot)
            ? Directory.EnumerateDirectories(privateRoot).Where(Complete).Order().FirstOrDefault()
            : null;
        return privateSet ?? Path.Combine(root, "chibi");
    }
}

/// <summary>How a character was made at creation: indices into <see cref="CharacterOptions.Current"/>.</summary>
public sealed record Appearance(byte Body, byte Skin, byte Hair, byte HairColor, byte Eyes, byte Race = 0, byte Beard = 0)
{
    private static CharacterOptions O => CharacterOptions.Current;

    public string BodyId => Pick(O.Bodies, Body).Id;
    public string SkinId => Pick(O.Skins, Skin).Id;
    public string HairId => Pick(O.Hair, Hair).Id;
    public string HairColorId => Pick(O.HairColors, HairColor).Id;
    public string EyesId => Pick(O.Eyes, Eyes).Id;
    public Choice RaceChoice => Pick(O.Races, Race);
    public string BeardId => Pick(O.Beards, Beard).Id;

    /// <summary>Every choice exists; a list the art set leaves empty (cats have no hair) only accepts 0.</summary>
    public bool IsValid =>
        Fits(Body, O.Bodies) && Fits(Skin, O.Skins) && Fits(Hair, O.Hair) && Fits(HairColor, O.HairColors)
        && Fits(Eyes, O.Eyes) && Fits(Race, O.Races) && Fits(Beard, O.Beards);

    private static bool Fits(byte value, Choice[] choices) => value < Math.Max(1, choices.Length);

    /// <summary>
    /// A random look with natural colours. A saved look from another art set may point past the end
    /// of these lists; <see cref="Pick"/> wraps it round rather than failing.
    /// </summary>
    public static Appearance Random(Random rng) => new(
        (byte)rng.Next(Math.Max(1, O.Bodies.Length)),
        (byte)rng.Next(Math.Max(1, O.RandomSkins > 0 ? O.RandomSkins : O.Skins.Length)),
        (byte)rng.Next(Math.Max(1, O.Hair.Length)),
        (byte)rng.Next(Math.Max(1, O.RandomHairColors > 0 ? O.RandomHairColors : O.HairColors.Length)),
        (byte)rng.Next(Math.Max(1, O.RandomEyes > 0 ? O.RandomEyes : O.Eyes.Length)));

    private static Choice Pick(Choice[] choices, byte index) =>
        choices.Length == 0 ? new Choice("", "") : choices[index % choices.Length];

    public void Write(NetDataWriter w)
    {
        w.Put(Body);
        w.Put(Skin);
        w.Put(Hair);
        w.Put(HairColor);
        w.Put(Eyes);
        w.Put(Race);
        w.Put(Beard);
    }

    public static Appearance Read(NetDataReader r) =>
        new(r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte());
}

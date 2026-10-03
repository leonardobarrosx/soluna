using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soluna.Shared;

/// <summary>Numbers that grow with level. One place, so balancing is a matter of editing this file.</summary>
public static class Formulas
{
    public const int MaxLevel = 50;

    public static int MaxHp(int level) => 40 + level * 12;
    public static int MaxMp(int level) => 20 + level * 6;
    public static int Attack(int level) => 4 + level * 2;
    public static int Defense(int level) => 1 + level;

    /// <summary>Experience needed to go from this level to the next.</summary>
    public static int ExpToNext(int level) => level >= MaxLevel ? 0 : (int)(30 * Math.Pow(level, 1.5));

    /// <summary>Damage of one hit: attack with some spread, half the defence taken off, never below 1.</summary>
    public static int Damage(int attack, int defense, Random rng)
    {
        var roll = attack * (0.85 + rng.NextDouble() * 0.3);
        return Math.Max(1, (int)Math.Round(roll - defense / 2.0));
    }

    /// <summary>Milliseconds between a player's attacks.</summary>
    public const int PlayerAttackMs = 550;
}

public enum NpcBehaviour : byte
{
    /// <summary>Wanders and cannot be attacked.</summary>
    Friendly = 0,

    /// <summary>Wanders until hit, then fights back.</summary>
    Passive = 1,

    /// <summary>Attacks any player that comes within range.</summary>
    Aggressive = 2,
}

/// <summary>An NPC from data/npcs.json (or data/private/npcs.json alongside a private art set).</summary>
public sealed class NpcDef
{
    public int Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>A 3x4 RPG Maker sheet, as a path under assets/. Used when <see cref="Look"/> is not set.</summary>
    public string Sprite { get; init; } = "";

    /// <summary>A paper-doll look instead of a sprite sheet: appearance plus item ids worn.</summary>
    public Appearance? Look { get; init; }
    public int[] Wears { get; init; } = [];

    public NpcBehaviour Behaviour { get; init; }
    public int Hp { get; init; } = 20;
    public int Attack { get; init; } = 4;
    public int Defense { get; init; }
    public int Exp { get; init; }

    /// <summary>Tiles within which an aggressive NPC notices a player.</summary>
    public int Range { get; init; } = 5;

    public int MoveMs { get; init; } = 600;
    public int AttackMs { get; init; } = 1200;
    public int RespawnSeconds { get; init; } = 20;

    public bool Hostile => Behaviour != NpcBehaviour.Friendly;
}

/// <summary>Where an NPC appears on a map; it respawns there after dying.</summary>
public sealed class NpcSpawn
{
    public int NpcId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class NpcCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private Dictionary<int, NpcDef> _npcs = [];

    public string Json { get; private set; } = "[]";

    /// <summary>The server's copy: data/private/npcs.json when there is one, otherwise data/npcs.json.</summary>
    public static NpcCatalog Load()
    {
        var catalog = new NpcCatalog();
        var privateNpcs = Path.Combine(DataPaths.Data, "private", "npcs.json");
        var path = File.Exists(privateNpcs) ? privateNpcs : Path.Combine(DataPaths.Data, "npcs.json");
        if (File.Exists(path)) catalog.Replace(File.ReadAllText(path));
        return catalog;
    }

    public void Replace(string json)
    {
        var npcs = JsonSerializer.Deserialize<List<NpcDef>>(json, JsonOptions) ?? [];
        _npcs = npcs.ToDictionary(n => n.Id);
        Json = json;
    }

    public NpcDef? Get(int id) => _npcs.GetValueOrDefault(id);

    public IEnumerable<NpcDef> All => _npcs.Values.OrderBy(n => n.Id);
}

/// <summary>Who a hit or a heal happened to, in combat packets.</summary>
public enum UnitKind : byte
{
    Player = 0,
    Npc = 1,
}

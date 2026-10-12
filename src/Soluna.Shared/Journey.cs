using System.Text.Json;

namespace Soluna.Shared;

/// <summary>What a player asks of the world. Craft, Use and Buy carry a string: recipe id, supply id, offer index.</summary>
public enum AdventureAction : byte { Interact, Skill, ReturnHome, Craft, SellMaterials, Use, Buy }
public enum LandmarkKind { Shrine, Merchant, Noticeboard, Chest, Expedition, Exit, Fishing, Forage, Mining, Woodcutting, Extraction, Workbench, Descent }

public sealed class Landmark
{
    public LandmarkKind Kind { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>Account-wide memories survive a character's death and are shared by its character slots.</summary>
public sealed class Legacy
{
    public HashSet<string> Memories { get; set; } = [];
    public int Lives { get; set; } = 1;
    public int Kills { get; set; }
    public int Catches { get; set; }
    public int Bosses { get; set; }
    public int InnateLuck { get; set; }
}

/// <summary>A life, separate from account-wide legacy. All counters are awarded by the server.</summary>
public sealed class Journey
{
    public string Calling { get; set; } = "unformed";
    public string Bloodline { get; set; } = "unformed";
    public int Gold { get; set; } = 20;
    public int Kills { get; set; }
    public int MeleeHits { get; set; }
    public int Spells { get; set; }
    public int Steps { get; set; }
    public int Bosses { get; set; }
    public int Catches { get; set; }
    public int Harvests { get; set; }
    public int Expeditions { get; set; }
    public int Maturity { get; set; }
    public int DeepestFloor { get; set; }
    public Dictionary<string, int> Resources { get; set; } = [];
    public List<LuckSource> LuckSources { get; set; } = [];
    public int Luck => Fortune.Total(LuckSources);
    /// <summary>Timed effects in force: effect id to the Unix second it ends.</summary>
    public Dictionary<string, long> Effects { get; set; } = [];
    public Legacy Legacy { get; set; } = new();
    public Dictionary<string, int> Habits { get; set; } = [];
    public HashSet<string> Rewards { get; set; } = [];
    /// <summary>Supply id to how many are in the bag.</summary>
    public Dictionary<string, int> Supplies { get; set; } = [];

    // Derived by the authoritative evolution rules; synchronized so custom games need no client code changes.
    public string CallingName { get; set; } = "Sem forma";
    public string BloodlineName { get; set; } = "Sem origem";
    public string Trace { get; set; } = "Você ainda é uma página em branco.";
    public string Tint { get; set; } = "#dddfe8";
    public string Accent { get; set; } = "#c0c6de";
    public string Silhouette { get; set; } = "blank";
    public int AttackBonus { get; set; }
    public int DefenseBonus { get; set; }
    public int HpBonus { get; set; }
    public int MpBonus { get; set; }
    public int SkillCost { get; set; } = 8;
    public int SkillCooldownMs { get; set; } = 2800;
    public int Count(string item) => Supplies.GetValueOrDefault(item);
    public void Add(string item, int count = 1) => Supplies[item] = Math.Clamp(Count(item) + count, 0, 99);
    public bool Consume(string item)
    {
        if (Count(item) <= 0) return false;
        Supplies[item]--;
        return true;
    }
    public void Record(string habit, int amount = 1) => Habits[habit] = Math.Clamp(Habits.GetValueOrDefault(habit) + amount, 0, 1_000_000);
    public bool HasEffect(string id, long now) => id.Length > 0 && Effects.GetValueOrDefault(id) > now;

    /// <summary>Counter names objectives and memories can watch. "account." ones survive death.</summary>
    public static readonly string[] Counters =
        ["kills", "bosses", "catches", "harvests", "expeditions", "depth", "spells",
         "account.kills", "account.bosses", "account.catches", "account.lives"];

    public int Counter(string name) => name switch
    {
        "kills" => Kills, "bosses" => Bosses, "catches" => Catches, "harvests" => Harvests,
        "expeditions" => Expeditions, "depth" => DeepestFloor, "spells" => Spells,
        "account.kills" => Legacy.Kills, "account.bosses" => Legacy.Bosses,
        "account.catches" => Legacy.Catches, "account.lives" => Legacy.Lives,
        _ => 0,
    };
}

public sealed class EvolutionRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "calling";
    public string Trace { get; set; } = "";
    public Dictionary<string, int> Requires { get; set; } = [];
    public Dictionary<string, double> Weights { get; set; } = [];
    public string Tint { get; set; } = "#dddfe8";
    public string Accent { get; set; } = "#c0c6de";
    public string Silhouette { get; set; } = "cloak";
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Health { get; set; }
    public int Mana { get; set; }
    public int SkillCost { get; set; } = 8;
    public int SkillCooldownMs { get; set; } = 2800;
    public bool Matches(Journey j) => Requires.Count > 0 && Requires.All(p => j.Habits.GetValueOrDefault(p.Key) >= p.Value);
    public double Score(Journey j) => Weights.Sum(p => Math.Min(200, j.Habits.GetValueOrDefault(p.Key)) * p.Value) / Math.Max(1, Weights.Values.Sum());
}

/// <summary>Game-authored combinations of habits, never a class menu or an activity restriction.</summary>
public sealed class EvolutionRules
{
    public List<EvolutionRule> Rules { get; set; } = [];
    public static string SourcePath => Path.Combine(DataPaths.Data, "evolution.json");
    public static EvolutionRules Load() => Load(SourcePath);
    public static EvolutionRules Parse(string json) => JsonSerializer.Deserialize<EvolutionRules>(json, AdventureContent.JsonOptions) ?? new();
    public string ToJson() => JsonSerializer.Serialize(this, AdventureContent.JsonOptions);

    /// <summary>Problems that would break evolution if saved; empty when the rules are sound.</summary>
    public List<string> Problems()
    {
        var problems = new List<string>();
        foreach (var g in Rules.GroupBy(r => r.Id).Where(g => g.Key.Length == 0 || g.Count() > 1))
            problems.Add(g.Key.Length == 0 ? "Há uma forma sem identificador." : $"Há mais de uma forma '{g.Key}'.");
        static bool Color(string c) => c.Length == 7 && c[0] == '#' && c[1..].All(Uri.IsHexDigit);
        foreach (var r in Rules)
        {
            if (r.Kind is not ("calling" or "bloodline")) problems.Add($"'{r.Name}' precisa ser especialização ou origem.");
            if (r.Requires.Count == 0 || r.Requires.Any(p => p.Key.Length == 0 || p.Value < 1)) problems.Add($"'{r.Name}' precisa exigir pelo menos um hábito.");
            if (r.Weights.Any(p => p.Value < 0)) problems.Add($"'{r.Name}' tem um peso negativo.");
            if (!Color(r.Tint) || !Color(r.Accent)) problems.Add($"'{r.Name}' tem uma cor inválida (use #rrggbb).");
            if (r.SkillCost < 0 || r.SkillCooldownMs < 100) problems.Add($"'{r.Name}' tem custo ou recarga de habilidade inválidos.");
        }
        return problems;
    }
    public static EvolutionRules Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<EvolutionRules>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new()
        : new();

    public bool Apply(Journey j)
    {
        var old = (j.Calling, j.Bloodline, j.Maturity);
        EvolutionRule? Pick(string kind, string current)
        {
            var eligible = Rules.Where(r => r.Kind == kind && r.Matches(j)).OrderByDescending(r => r.Score(j)).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
            var best = eligible.FirstOrDefault();
            var previous = eligible.FirstOrDefault(r => r.Id == current);
            // A new habit must become meaningfully dominant; a single alternating action cannot flicker forms.
            return previous != null && best != null && best.Score(j) <= previous.Score(j) * 1.2 ? previous : best;
        }
        var calling = Pick("calling", j.Calling);
        var blood = Pick("bloodline", j.Bloodline);
        j.Calling = calling?.Id ?? "unformed";
        j.CallingName = calling?.Name ?? "Sem forma";
        j.Bloodline = blood?.Id ?? "unformed";
        j.BloodlineName = blood?.Name ?? "Sem origem";
        j.Maturity = calling == null && blood == null ? 0 : Math.Clamp(j.Habits.Values.Sum() / 20, 1, 3);
        j.Tint = blood?.Tint ?? calling?.Tint ?? "#dddfe8";
        j.Accent = blood?.Accent ?? calling?.Accent ?? "#c0c6de";
        j.Silhouette = calling?.Silhouette ?? "blank";
        j.AttackBonus = (calling?.Attack ?? 0) + (blood?.Attack ?? 0);
        j.DefenseBonus = (calling?.Defense ?? 0) + (blood?.Defense ?? 0);
        j.HpBonus = (calling?.Health ?? 0) + (blood?.Health ?? 0) + Math.Min(12, j.Legacy.Memories.Count * 2);
        j.MpBonus = (calling?.Mana ?? 0) + (blood?.Mana ?? 0);
        j.SkillCost = calling?.SkillCost ?? 8;
        j.SkillCooldownMs = calling?.SkillCooldownMs ?? 2800;
        if (old.Bloodline != j.Bloodline && blood != null) j.Trace = blood.Trace;
        else if (old.Calling != j.Calling && calling != null) j.Trace = calling.Trace;
        return old != (j.Calling, j.Bloodline, j.Maturity);
    }
}


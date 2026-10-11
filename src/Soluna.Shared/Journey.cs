using System.Text.Json;

namespace Soluna.Shared;

public enum Supply { Tonic, Ether, Moonfruit, Wildroot, Emberseed, FortuneTea, AshSalt }
public enum AdventureAction : byte { Interact, Tonic, Ether, Skill, Moonfruit, Wildroot, Emberseed, BuyTonic, BuyEther, BuyBlade, ReturnHome, Craft, SellMaterials, FortuneTea, AshSalt }
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
    public long BlessingUntil { get; set; }
    public long CurseUntil { get; set; }
    public long TeaUntil { get; set; }
    public Legacy Legacy { get; set; } = new();
    public Dictionary<string, int> Habits { get; set; } = [];
    public HashSet<string> Rewards { get; set; } = [];
    public Dictionary<Supply, int> Supplies { get; set; } = new() { [Supply.Tonic] = 4, [Supply.Ether] = 3 };

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
    public int Count(Supply item) => Supplies.GetValueOrDefault(item);
    public void Add(Supply item, int count = 1) => Supplies[item] = Math.Clamp(Count(item) + count, 0, 99);
    public bool Consume(Supply item)
    {
        if (Count(item) <= 0) return false;
        Supplies[item]--;
        return true;
    }
    public void Record(string habit, int amount = 1) => Habits[habit] = Math.Clamp(Habits.GetValueOrDefault(habit) + amount, 0, 1_000_000);
    public static Journey Reborn(Legacy legacy)
    {
        legacy.Lives++;
        return new Journey { Legacy = legacy, HpBonus = Math.Min(12, legacy.Memories.Count * 2) };
    }
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
    public static EvolutionRules Load() => Load(Path.Combine(DataPaths.Data, "evolution.json"));
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

public static class JourneyRules
{
    public static string SupplyName(Supply value) => value switch
    {
        Supply.Tonic => "Tônico de vida", Supply.Ether => "Éter azul", Supply.Moonfruit => "Fruto pálido",
        Supply.Wildroot => "Raiz amarga", Supply.Emberseed => "Semente ardente",
        Supply.FortuneTea => "Chá dos espíritos", _ => "Sal de cinzas",
    };
    public static string Objective(Journey j) => !j.Rewards.Contains("first-hunt")
        ? $"Ecos no bosque · {Math.Min(6, j.Kills)}/6 criaturas"
        : !j.Rewards.Contains("eclipse") ? $"O coração do eclipse · {Math.Min(1, j.Bosses)}/1 guardião"
        : "A noite se lembra. As ruínas voltam a mudar.";
}

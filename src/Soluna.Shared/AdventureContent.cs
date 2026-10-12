using System.Text.Encodings.Web;
using System.Text.Json;

namespace Soluna.Shared;

public sealed class ResourceDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Value { get; set; } = 1;
}
public sealed class Recipe
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, int> Costs { get; set; } = [];
    public int Item { get; set; }
    public string Resource { get; set; } = "";
    /// <summary>Id of the supply it makes, or empty.</summary>
    public string Supply { get; set; } = "";
    public int Amount { get; set; } = 1;
}
public sealed class ActivityDef
{
    /// <summary>The landmark kind it answers to, lowercased: fishing, forage, mining, woodcutting, extraction.</summary>
    public string Id { get; set; } = "";
    public string Habit { get; set; } = "";
    public string Resource { get; set; } = "";
    public string Message { get; set; } = "";
    public int CooldownSeconds { get; set; } = 4;
    /// <summary>Supply that sometimes comes along, or empty.</summary>
    public string Supply { get; set; } = "";
}

/// <summary>Something carried in the bag and used from it: potions, food, charms.</summary>
public sealed class SupplyDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Hp { get; set; }
    public int Mp { get; set; }
    /// <summary>Habit recorded when used, feeding the evolution rules.</summary>
    public string Habit { get; set; } = "";
    /// <summary>Timed effect it starts (an id from Effects), or empty.</summary>
    public string Effect { get; set; } = "";
    /// <summary>Timed effect it ends, or empty.</summary>
    public string Cleanse { get; set; } = "";
    /// <summary>Number key (1 to 9) that uses it from the action bar; 0 for none.</summary>
    public int Hotkey { get; set; }
}

/// <summary>A timed influence on luck: a blessing, a curse, a tea.</summary>
public sealed class EffectDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Luck { get; set; }
    public int Seconds { get; set; } = 120;
}

/// <summary>What the merchant sells: a supply or an item, for a price.</summary>
public sealed class ShopOffer
{
    public string Supply { get; set; } = "";
    public int Item { get; set; }
    public string Name { get; set; } = "";
    public int Price { get; set; } = 1;
}
public sealed class ShopSettings
{
    public string Name { get; set; } = "Mercador";
    public string Greeting { get; set; } = "";
    public List<ShopOffer> Offers { get; set; } = [];
    /// <summary>Selling surplus keeps this many of every material in the bag.</summary>
    public int KeepPerResource { get; set; } = 10;
}

/// <summary>A goal on the noticeboard, claimed there in order once its counter reaches the goal.</summary>
public sealed class ObjectiveDef
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>A <see cref="Journey.Counter"/> name.</summary>
    public string Counter { get; set; } = "kills";
    public int Goal { get; set; } = 1;
    public int Gold { get; set; }
    public Dictionary<string, int> Supplies { get; set; } = [];
    public string Memory { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>Something the account remembers across deaths. Earned when its counter reaches the goal, or by an objective.</summary>
public sealed class MemoryDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>A <see cref="Journey.Counter"/> name; empty when only an objective grants it.</summary>
    public string Counter { get; set; } = "";
    public int Goal { get; set; } = 1;
}

/// <summary>What defeating creatures and opening chests gives.</summary>
public sealed class LootSettings
{
    public double SupplyChance { get; set; } = 0.4;
    public string Supply { get; set; } = "";
    /// <summary>Replaces <see cref="Supply"/> when luck favours the roll.</summary>
    public string RareSupply { get; set; } = "";
    public string Resource { get; set; } = "";
    public Dictionary<string, int> BossSupplies { get; set; } = [];
    public string BossMessage { get; set; } = "";
    public int ChestGold { get; set; } = 12;
    public int ChestGoldPerDepth { get; set; } = 3;
    public List<string> ChestSupplies { get; set; } = [];
    public string ChestResource { get; set; } = "";
    public double ChestCurseChance { get; set; } = 0.18;
    public string ChestCurse { get; set; } = "";
}

/// <summary>Names and sizes of the permanent luck influences.</summary>
public sealed class LuckSettings
{
    public string Innate { get; set; } = "Marca de nascimento";
    public string Spirits { get; set; } = "Ciclo dos espíritos";
    /// <summary>The spirit cycle: one value per phase, each phase lasting <see cref="PhaseSeconds"/>.</summary>
    public int[] SpiritPhases { get; set; } = [];
    public int PhaseSeconds { get; set; } = 180;
    public string Nature { get; set; } = "Natureza";
    public string Memories { get; set; } = "Memórias";
    public string Relics { get; set; } = "Relíquias";
    /// <summary>The shrine's blessing (an id from Effects).</summary>
    public string ShrineEffect { get; set; } = "";
}

/// <summary>Content travels to the client; every name, price and reward of a game lives here, not in code.</summary>
public sealed class AdventureContent
{
    public bool EmergentAppearance { get; set; }
    public bool DeathResetsLife { get; set; }
    public DungeonSettings? Dungeon { get; set; }
    public int StartingGold { get; set; } = 20;
    public Dictionary<string, int> StartingSupplies { get; set; } = [];
    public List<ResourceDef> Resources { get; set; } = [];
    public List<SupplyDef> Supplies { get; set; } = [];
    public List<EffectDef> Effects { get; set; } = [];
    public List<Recipe> Recipes { get; set; } = [];
    public List<ActivityDef> Activities { get; set; } = [];
    public ShopSettings Shop { get; set; } = new();
    public string ObjectivesTitle { get; set; } = "Objetivos";
    public List<ObjectiveDef> Objectives { get; set; } = [];
    public string ObjectivesDone { get; set; } = "Nada mais a fazer por ora.";
    public List<MemoryDef> Memories { get; set; } = [];
    public LootSettings Loot { get; set; } = new();
    public LuckSettings Luck { get; set; } = new();
    /// <summary>Messages the game says; any key left out uses the engine's plain default.</summary>
    public Dictionary<string, string> Texts { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string SourcePath => Path.Combine(DataPaths.Data, "adventure.json");

    public static AdventureContent Load() => File.Exists(SourcePath) ? Parse(File.ReadAllText(SourcePath)) : new();
    public static AdventureContent Parse(string json) => JsonSerializer.Deserialize<AdventureContent>(json, JsonOptions) ?? new();
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public bool CanCraft(Journey j, Recipe recipe) => recipe.Costs.Count > 0 && recipe.Amount > 0
        && recipe.Costs.All(c => c.Value > 0 && j.Resources.GetValueOrDefault(c.Key) >= c.Value);
    public string ResourceName(string id) => Resources.FirstOrDefault(r => r.Id == id)?.Name ?? id;
    public SupplyDef? Supply(string id) => Supplies.FirstOrDefault(s => s.Id == id);
    public string SupplyName(string id) => Supply(id)?.Name ?? id;
    public EffectDef? Effect(string id) => Effects.FirstOrDefault(e => e.Id == id);
    public string MemoryName(string id) => Memories.FirstOrDefault(m => m.Id == id)?.Name ?? id;
    public string Text(string key, string fallback) => Texts.TryGetValue(key, out var text) && text.Length > 0 ? text : fallback;

    /// <summary>The first objective not yet claimed, with progress; or the closing line once all are done.</summary>
    public ObjectiveDef? CurrentObjective(Journey j) => Objectives.FirstOrDefault(o => !j.Rewards.Contains(o.Id));
    public string ObjectiveLine(Journey j) => CurrentObjective(j) is { } o
        ? $"{o.Text} · {Math.Min(o.Goal, j.Counter(o.Counter))}/{o.Goal}"
        : ObjectivesDone;

    /// <summary>A fresh life with the game's starting purse and bag.</summary>
    public Journey NewJourney(Legacy? legacy = null)
    {
        var j = new Journey { Gold = StartingGold, Supplies = new Dictionary<string, int>(StartingSupplies) };
        if (legacy != null) j.Legacy = legacy;
        return j;
    }

    /// <summary>Problems that would break the game if saved, in plain words; empty when the content is sound.</summary>
    public List<string> Problems(Func<int, bool> itemExists)
    {
        var problems = new List<string>();
        void Duplicates<T>(IEnumerable<T> list, Func<T, string> id, string what)
        {
            foreach (var g in list.GroupBy(id).Where(g => g.Key.Length == 0 || g.Count() > 1))
                problems.Add(g.Key.Length == 0 ? $"Há {what} sem identificador." : $"Há mais de um {what} '{g.Key}'.");
        }
        Duplicates(Resources, r => r.Id, "material");
        Duplicates(Supplies, s => s.Id, "suprimento");
        Duplicates(Effects, e => e.Id, "efeito");
        Duplicates(Recipes, r => r.Id, "receita");
        Duplicates(Objectives, o => o.Id, "objetivo");
        Duplicates(Memories, m => m.Id, "memória");
        bool Resource(string id) => id.Length == 0 || Resources.Any(r => r.Id == id);
        bool SupplyOk(string id) => id.Length == 0 || Supplies.Any(s => s.Id == id);
        bool EffectOk(string id) => id.Length == 0 || Effects.Any(e => e.Id == id);
        foreach (var r in Recipes)
        {
            if (r.Costs.Count == 0 || r.Costs.Any(c => !Resource(c.Key) || c.Key.Length == 0 || c.Value <= 0)) problems.Add($"A receita '{r.Name}' tem custos inválidos.");
            if (!Resource(r.Resource) || !SupplyOk(r.Supply) || r.Item < 0 || r.Item > 0 && !itemExists(r.Item)) problems.Add($"A receita '{r.Name}' produz algo que não existe.");
            if (r.Item == 0 && r.Resource.Length == 0 && r.Supply.Length == 0) problems.Add($"A receita '{r.Name}' não produz nada.");
            if (r.Amount < 1) problems.Add($"A receita '{r.Name}' precisa produzir pelo menos 1.");
        }
        foreach (var a in Activities)
            if (!Resource(a.Resource) || a.Resource.Length == 0 || !SupplyOk(a.Supply)) problems.Add($"O ofício '{a.Id}' usa um material ou suprimento que não existe.");
        foreach (var s in Supplies)
            if (!EffectOk(s.Effect) || !EffectOk(s.Cleanse)) problems.Add($"O suprimento '{s.Name}' usa um efeito que não existe.");
        foreach (var o in Shop.Offers)
            if (o.Price < 0 || (o.Supply.Length == 0) == (o.Item == 0) || !SupplyOk(o.Supply) || o.Item > 0 && !itemExists(o.Item)) problems.Add($"A oferta '{o.Name}' precisa vender um suprimento ou um item que exista, por um preço válido.");
        foreach (var o in Objectives)
            if (o.Goal < 1 || !Journey.Counters.Contains(o.Counter) || o.Supplies.Keys.Any(k => !SupplyOk(k)) || o.Memory.Length > 0 && Memories.All(m => m.Id != o.Memory))
                problems.Add($"O objetivo '{o.Text}' tem contador, recompensa ou memória inválidos.");
        foreach (var m in Memories)
            if (m.Counter.Length > 0 && (!Journey.Counters.Contains(m.Counter) || m.Goal < 1)) problems.Add($"A memória '{m.Name}' tem um contador inválido.");
        if (!SupplyOk(Loot.Supply) || !SupplyOk(Loot.RareSupply) || !Resource(Loot.Resource) || !Resource(Loot.ChestResource)
            || Loot.BossSupplies.Keys.Any(k => !SupplyOk(k)) || Loot.ChestSupplies.Any(k => !SupplyOk(k)) || !EffectOk(Loot.ChestCurse))
            problems.Add("Os espólios usam um suprimento, material ou efeito que não existe.");
        if (!EffectOk(Luck.ShrineEffect)) problems.Add("O santuário usa um efeito que não existe.");
        if (StartingSupplies.Keys.Any(k => !SupplyOk(k))) problems.Add("A bolsa inicial tem um suprimento que não existe.");
        return problems;
    }
}

public sealed record LuckSource(string Name, int Value);
public static class Fortune
{
    public static int Total(IEnumerable<LuckSource> sources) => Math.Clamp(sources.Sum(s => s.Value), -30, 40);
    public static double RareChance(int luck) => Math.Clamp(0.12 + luck * 0.006, 0.02, 0.5);
    public static int Yield(int luck, Random rng) => 1 + (rng.NextDouble() < Math.Clamp(0.2 + luck * 0.01, 0.02, 0.8) ? 1 : 0);
}

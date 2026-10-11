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
    public int Supply { get; set; } = -1;
    public int Amount { get; set; } = 1;
}
public sealed class ActivityDef
{
    public string Id { get; set; } = "";
    public string Habit { get; set; } = "";
    public string Resource { get; set; } = "";
    public string Message { get; set; } = "";
    public int CooldownSeconds { get; set; } = 4;
    public int Supply { get; set; } = -1;
}
/// <summary>Content travels to the client; recipes and profession yields stay authored per game.</summary>
public sealed class AdventureContent
{
    public bool EmergentAppearance { get; set; }
    public bool DeathResetsLife { get; set; }
    public DungeonSettings? Dungeon { get; set; }
    public List<ResourceDef> Resources { get; set; } = [];
    public List<Recipe> Recipes { get; set; } = [];
    public List<ActivityDef> Activities { get; set; } = [];
    public static AdventureContent Load() => File.Exists(Path.Combine(DataPaths.Data, "adventure.json"))
        ? Parse(File.ReadAllText(Path.Combine(DataPaths.Data, "adventure.json"))) : new();
    public static AdventureContent Parse(string json) => JsonSerializer.Deserialize<AdventureContent>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    public bool CanCraft(Journey j, Recipe recipe) => recipe.Costs.Count > 0 && recipe.Amount > 0
        && recipe.Costs.All(c => c.Value > 0 && j.Resources.GetValueOrDefault(c.Key) >= c.Value);
    public string ResourceName(string id) => Resources.FirstOrDefault(r => r.Id == id)?.Name ?? id;
}

public sealed record LuckSource(string Name, int Value);
public static class Fortune
{
    public static int Total(IEnumerable<LuckSource> sources) => Math.Clamp(sources.Sum(s => s.Value), -30, 40);
    public static double RareChance(int luck) => Math.Clamp(0.12 + luck * 0.006, 0.02, 0.5);
    public static int Yield(int luck, Random rng) => 1 + (rng.NextDouble() < Math.Clamp(0.2 + luck * 0.01, 0.02, 0.8) ? 1 : 0);
}

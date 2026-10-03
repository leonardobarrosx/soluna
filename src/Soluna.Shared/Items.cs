using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soluna.Shared;

public enum EquipSlot : byte
{
    Head = 0,
    Torso = 1,
    Legs = 2,
    Feet = 3,
    Neck = 4,
    Arms = 5,
    Weapon = 6,
    Back = 7,
    Face = 8,
}

/// <summary>
/// An item from data/items.json. Wearables point at a character layer and the colour to
/// paint it (empty keeps the drawn colours), which is how equipping changes the sprite.
/// </summary>
public sealed class ItemDef
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public EquipSlot Slot { get; set; }
    public string Sheet { get; set; } = "";
    public string[] Colors { get; set; } = [];

    /// <summary>Given to every new character.</summary>
    public bool Starter { get; set; }

    /// <summary>Added to the wearer's attack and defence while equipped.</summary>
    public int Attack { get; set; }
    public int Defense { get; set; }

    /// <summary>What a shop asks for it, in the game's currency.</summary>
    public int Price { get; set; }

    public ItemDef Clone() => (ItemDef)MemberwiseClone();
}

/// <summary>Item ids worn in each <see cref="EquipSlot"/>; 0 means the slot is empty.</summary>
public sealed class Equipment
{
    public const int SlotCount = 9;

    private readonly int[] _items = new int[SlotCount];

    public int this[EquipSlot slot]
    {
        get => _items[(int)slot];
        set => _items[(int)slot] = value;
    }

    public IReadOnlyList<int> Items => _items;

    public Equipment Clone()
    {
        var copy = new Equipment();
        _items.CopyTo(copy._items, 0);
        return copy;
    }

    /// <summary>Stable text form, used as a cache key for composed sprites.</summary>
    public override string ToString() => string.Join(',', _items);
}

public sealed class ItemCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private Dictionary<int, ItemDef> _items = [];

    /// <summary>The file the server reads and the editor saves into.</summary>
    public string? SourcePath { get; private set; }

    /// <summary>
    /// The server's copy: data/private/items.json when a private character art set brought its own
    /// items, otherwise data/items.json.
    /// </summary>
    public static ItemCatalog Load()
    {
        var privateItems = Path.Combine(DataPaths.Data, "private", "items.json");
        return Load(File.Exists(privateItems) ? privateItems : Path.Combine(DataPaths.Data, "items.json"));
    }

    public static ItemCatalog Load(string path)
    {
        var catalog = new ItemCatalog { SourcePath = path };
        if (File.Exists(path)) catalog.Replace(File.ReadAllText(path));
        return catalog;
    }

    /// <summary>Items as JSON in the same shape as the files: one per line, readable in a diff.</summary>
    public static string ToJson(IEnumerable<ItemDef> items) => ContentJson.Array(items);

    /// <summary>Validates and parses JSON from an editor; throws on anything malformed.</summary>
    public static List<ItemDef> Parse(string json) =>
        JsonSerializer.Deserialize<List<ItemDef>>(json, JsonOptions) ?? throw new InvalidDataException("Empty item list.");

    /// <summary>The client starts empty and fills in when the server sends the catalog.</summary>
    public static ItemCatalog Empty() => new();

    /// <summary>The JSON the catalog was read from, as sent to clients.</summary>
    public string Json { get; private set; } = "[]";

    public void Replace(string json)
    {
        var items = JsonSerializer.Deserialize<List<ItemDef>>(json, JsonOptions) ?? [];
        _items = items.ToDictionary(i => i.Id);
        Json = json;
    }

    public IEnumerable<ItemDef> All => _items.Values.OrderBy(i => i.Slot).ThenBy(i => i.Id);

    public ItemDef? Get(int id) => _items.GetValueOrDefault(id);

    public IEnumerable<int> StarterItems => All.Where(i => i.Starter).Select(i => i.Id);

    /// <summary>What a new character starts wearing: the first starter item of each slot.</summary>
    public Equipment StarterEquipment()
    {
        var equipment = new Equipment();
        foreach (var item in All.Where(i => i.Starter))
        {
            if (equipment[item.Slot] == 0) equipment[item.Slot] = item.Id;
        }
        return equipment;
    }
}

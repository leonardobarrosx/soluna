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
}

/// <summary>
/// An item from data/items.json. Wearables point at a character layer and the colour to
/// paint it (empty keeps the drawn colours), which is how equipping changes the sprite.
/// </summary>
public sealed class ItemDef
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public EquipSlot Slot { get; init; }
    public string Sheet { get; init; } = "";
    public string[] Colors { get; init; } = [];

    /// <summary>Given to every new character.</summary>
    public bool Starter { get; init; }
}

/// <summary>Item ids worn in each <see cref="EquipSlot"/>; 0 means the slot is empty.</summary>
public sealed class Equipment
{
    public const int SlotCount = 6;

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

    /// <summary>The server's copy, from data/items.json.</summary>
    public static ItemCatalog Load() => Load(Path.Combine(DataPaths.Data, "items.json"));

    public static ItemCatalog Load(string path)
    {
        var catalog = new ItemCatalog();
        if (File.Exists(path)) catalog.Replace(File.ReadAllText(path));
        return catalog;
    }

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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soluna.Shared;

public enum EquipSlot : byte
{
    Head = 0,
    Torso = 1,
    Legs = 2,
    Feet = 3,
}

/// <summary>
/// An item from data/items.json. Wearables point at an LPC sheet and the palette
/// variant for each of its colour channels, which is how equipping changes the sprite.
/// </summary>
public sealed class ItemDef
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public EquipSlot Slot { get; init; }
    public string Sheet { get; init; } = "";
    public string[] Colors { get; init; } = [];
}

/// <summary>Item ids worn in each <see cref="EquipSlot"/>; 0 means the slot is empty.</summary>
public sealed class Equipment
{
    public const int SlotCount = 4;

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

    private readonly Dictionary<int, ItemDef> _items;

    private ItemCatalog(IEnumerable<ItemDef> items) => _items = items.ToDictionary(i => i.Id);

    public static ItemCatalog Load() => Load(Path.Combine(DataPaths.Data, "items.json"));

    public static ItemCatalog Load(string path)
    {
        var items = File.Exists(path)
            ? JsonSerializer.Deserialize<List<ItemDef>>(File.ReadAllText(path), JsonOptions) ?? []
            : [];
        return new ItemCatalog(items);
    }

    public IEnumerable<ItemDef> All => _items.Values.OrderBy(i => i.Slot).ThenBy(i => i.Id);

    public ItemDef? Get(int id) => _items.GetValueOrDefault(id);

    /// <summary>What a new character starts wearing: the first item of each slot, skipping hats.</summary>
    public Equipment StarterEquipment()
    {
        var equipment = new Equipment();
        foreach (var slot in new[] { EquipSlot.Torso, EquipSlot.Legs, EquipSlot.Feet })
            equipment[slot] = All.FirstOrDefault(i => i.Slot == slot)?.Id ?? 0;
        return equipment;
    }
}

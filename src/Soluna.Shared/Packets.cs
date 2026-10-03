using LiteNetLib.Utils;

namespace Soluna.Shared;

public enum PacketType : byte
{
    // Client -> Server, before entering the world
    Login = 1,
    Register = 6,
    CreateCharacter = 7,
    DeleteCharacter = 8,
    PlayCharacter = 9,

    // Client -> Server, in the world
    MoveRequest = 2,
    ChatSend = 3,
    MapSave = 4,
    EquipToggle = 5,

    /// <summary>The client lacks this map (or has an old revision) and asks for it.</summary>
    MapRequest = 10,

    /// <summary>Attack whatever is in front: direction.</summary>
    Attack = 11,

    /// <summary>Admin: replace a whole kind of content (<see cref="ContentKind"/>, then its JSON).</summary>
    ContentSave = 12,

    /// <summary>Admin: which maps the game has.</summary>
    MapListRequest = 13,

    /// <summary>Admin: a file to add to the game: kind, name, shareable, then the bytes.</summary>
    AssetUpload = 14,

    /// <summary>A game file the client lacks or has an old version of: its path.</summary>
    AssetRequest = 15,

    // Server -> Client
    LoginOk = 100,
    MapLoad = 101,
    PlayerJoined = 102,
    PlayerLeft = 103,
    PlayerMoved = 104,
    PlayerPosition = 105,
    ChatMessage = 106,
    PlayerLook = 107,

    /// <summary>Login, registration or character request refused; carries a message for the player.</summary>
    Refused = 108,

    /// <summary>The account's character slots, sent after login and after every change to them.</summary>
    CharacterList = 109,

    /// <summary>The item definitions as JSON, sent once after login so the client never reads them from disk.</summary>
    ItemCatalog = 110,

    InventoryUpdate = 111,

    /// <summary>
    /// The player is now on a map, at a position: map id, revision, x, y, direction. The client draws it
    /// from its cache when the revision matches, or sends <see cref="MapRequest"/>.
    /// </summary>
    MapChange = 112,

    /// <summary>A map the player is on was saved: map id and its new revision.</summary>
    MapRevision = 113,

    /// <summary>The NPC definitions as JSON, sent once after login.</summary>
    NpcCatalog = 114,

    /// <summary>An NPC is on the map: index, npc id, x, y, direction, hp, max hp.</summary>
    NpcSpawned = 115,

    /// <summary>index, x, y, direction.</summary>
    NpcMoved = 116,

    /// <summary>index (died or left).</summary>
    NpcRemoved = 117,

    /// <summary>Your own numbers: hp, max hp, mp, max mp, level, exp, exp to next level.</summary>
    Vitals = 118,

    /// <summary>A unit's health changed: kind, id, hp, max hp, change (negative for damage), for bars and floating numbers.</summary>
    HpChanged = 119,

    /// <summary>A unit swung: kind, id, direction, for the attack lunge.</summary>
    Attacked = 120,

    /// <summary>The game's maps: count, then id and name of each.</summary>
    MapList = 121,

    /// <summary>Every game file players need: count, then path, size and hash of each.</summary>
    AssetManifest = 122,

    /// <summary>A game file: path, then the bytes.</summary>
    AssetData = 123,

    /// <summary>A game file was added or replaced: path, size, hash.</summary>
    AssetAdded = 124,
}

/// <summary>What a character slot shows on the select screen.</summary>
public sealed record CharacterSummary(string Name, Appearance Look, Equipment Equipment);

public sealed record PlayerInfo(int Id, string Name, int X, int Y, Direction Dir, Appearance Look, Equipment Equipment);

public static class PacketIO
{
    public static NetDataWriter Begin(PacketType type)
    {
        var w = new NetDataWriter();
        w.Put((byte)type);
        return w;
    }

    public static void Put(this NetDataWriter w, PlayerInfo p)
    {
        w.Put(p.Id);
        w.Put(p.Name);
        w.Put(p.X);
        w.Put(p.Y);
        w.Put((byte)p.Dir);
        p.Look.Write(w);
        w.Put(p.Equipment);
    }

    public static PlayerInfo GetPlayerInfo(this NetDataReader r) =>
        new(r.GetInt(), r.GetString(), r.GetInt(), r.GetInt(), (Direction)r.GetByte(), Appearance.Read(r), r.GetEquipment());

    public static void Put(this NetDataWriter w, Equipment equipment)
    {
        foreach (var item in equipment.Items) w.Put(item);
    }

    public static Equipment GetEquipment(this NetDataReader r)
    {
        var equipment = new Equipment();
        for (var slot = 0; slot < Equipment.SlotCount; slot++) equipment[(EquipSlot)slot] = r.GetInt();
        return equipment;
    }

    public static void PutBlob(this NetDataWriter w, byte[] data)
    {
        w.Put(data.Length);
        w.Put(data);
    }

    public static byte[] GetBlob(this NetDataReader r)
    {
        // The length comes off the wire: never trust it past what the packet actually holds.
        var length = r.GetInt();
        if (length < 0 || length > r.AvailableBytes) throw new InvalidDataException($"Blob of {length} bytes in a packet with {r.AvailableBytes} left.");
        var data = new byte[length];
        r.GetBytes(data, length);
        return data;
    }
}

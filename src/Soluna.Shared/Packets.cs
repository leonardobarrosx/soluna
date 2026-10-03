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
        var data = new byte[r.GetInt()];
        r.GetBytes(data, data.Length);
        return data;
    }
}

using LiteNetLib.Utils;

namespace Soluna.Shared;

public enum PacketType : byte
{
    // Client -> Server
    Login = 1,
    MoveRequest = 2,
    ChatSend = 3,
    MapSave = 4,

    // Server -> Client
    LoginOk = 100,
    MapLoad = 101,
    PlayerJoined = 102,
    PlayerLeft = 103,
    PlayerMoved = 104,
    PlayerPosition = 105,
    ChatMessage = 106,
}

public sealed record PlayerInfo(int Id, string Name, int X, int Y, Direction Dir, byte Sprite);

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
        w.Put(p.Sprite);
    }

    public static PlayerInfo GetPlayerInfo(this NetDataReader r) =>
        new(r.GetInt(), r.GetString(), r.GetInt(), r.GetInt(), (Direction)r.GetByte(), r.GetByte());

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

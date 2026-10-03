using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;

namespace Soluna.Client.Net;

/// <summary>UDP link to the server. Events fire on the game thread, from <see cref="Poll"/>.</summary>
internal sealed class Connection
{
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private NetPeer? _server;

    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<PacketType, NetDataReader>? Received;

    public Connection()
    {
        _net = new NetManager(_listener) { AutoRecycle = true };
        _listener.PeerConnectedEvent += peer =>
        {
            _server = peer;
            Connected?.Invoke();
        };
        _listener.PeerDisconnectedEvent += (_, info) =>
        {
            _server = null;
            Disconnected?.Invoke(info.Reason.ToString());
        };
        _listener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            var type = (PacketType)reader.GetByte();
            Received?.Invoke(type, reader);
        };
        _net.Start();
    }

    public bool IsConnected => _server is { ConnectionState: ConnectionState.Connected };

    public int PingMs => _server?.Ping ?? 0;

    public void Connect(string host, int port) => _net.Connect(host, port, Constants.ConnectionKey);

    public void Poll() => _net.PollEvents();

    public void Send(NetDataWriter writer) => _server?.Send(writer, DeliveryMethod.ReliableOrdered);

    public void Stop() => _net.Stop();
}

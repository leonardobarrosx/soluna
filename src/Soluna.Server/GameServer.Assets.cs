using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;

namespace Soluna.Server;

/// <summary>
/// The game's files: players get the list at login and download what they lack, and admins add
/// tilesets and character sprites by dropping a PNG on the game window.
/// </summary>
internal sealed partial class GameServer
{
    private Dictionary<string, (long size, string hash)>? _assets;

    /// <summary>Path, size and hash of every distributed file, scanned once and kept up to date by uploads.</summary>
    private Dictionary<string, (long size, string hash)> Assets =>
        _assets ??= AssetFiles.Scan(DataPaths.Assets).ToDictionary(a => a.path, a => (a.size, a.hash), StringComparer.OrdinalIgnoreCase);

    private void SendAssetManifest(Session session)
    {
        var w = PacketIO.Begin(PacketType.AssetManifest);
        w.Put(Assets.Count);
        foreach (var (path, (size, hash)) in Assets)
        {
            w.Put(path);
            w.Put(size);
            w.Put(hash);
        }
        session.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    private void HandleAssetRequest(Session session, NetDataReader reader)
    {
        var path = reader.GetString();
        // Only files in the manifest: never anything outside the game's assets or in sources/.
        if (!Assets.ContainsKey(path) || AssetFiles.Resolve(path) is not { } full || !File.Exists(full))
        {
            Log.Warn($"{session.Peer} asked for unknown asset '{path}'.");
            return;
        }
        var w = PacketIO.Begin(PacketType.AssetData);
        w.Put(path);
        w.PutBlob(File.ReadAllBytes(full));
        session.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    private void HandleAssetUpload(Player player, NetDataReader reader)
    {
        var kind = (AssetKind)reader.GetByte();
        var name = reader.GetString().Trim();
        var shareable = reader.GetBool();
        var data = reader.GetBlob();
        if (!player.IsAdmin)
        {
            Tell(player, "Só administradores podem importar arquivos.");
            return;
        }

        string? problem = !Enum.IsDefined(kind) ? "Tipo de arquivo desconhecido."
            : !IsAssetName(name) ? "O nome precisa ter de 1 a 40 letras, números, - ou _."
            : data.Length > AssetFiles.MaxUploadBytes ? $"O arquivo passa de {AssetFiles.MaxUploadBytes / 1024 / 1024} MB."
            : AssetFiles.PngSize(data) is not { } size ? "O arquivo não é um PNG."
            : AssetFiles.Check(kind, size);
        if (problem != null)
        {
            Tell(player, problem);
            return;
        }

        var path = AssetFiles.Destination(kind, name, shareable);
        var full = AssetFiles.Resolve(path)!;
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var replaced = File.Exists(full);
        File.WriteAllBytes(full + ".tmp", data);
        File.Move(full + ".tmp", full, overwrite: true);

        var entry = (size: (long)data.Length, hash: AssetFiles.Hash(data));
        Assets[path] = entry;
        var w = PacketIO.Begin(PacketType.AssetAdded);
        w.Put(path);
        w.Put(entry.size);
        w.Put(entry.hash);
        foreach (var session in _sessions.Values.Where(s => s.Account != null)) session.Peer.Send(w, DeliveryMethod.ReliableOrdered);

        Tell(player, $"{(replaced ? "Substituído" : "Importado")}: {path}");
        Log.Info($"{player.Name} uploaded {path} ({data.Length} bytes).");
    }

    private static bool IsAssetName(string name) =>
        name.Length is >= 1 and <= 40 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

using System.Text;
using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;

namespace Soluna.Server;

/// <summary>Content edited from the in-game editors: validated, written to the game's files and pushed live.</summary>
internal sealed partial class GameServer
{
    private void HandleContentSave(Player player, NetDataReader reader)
    {
        var kind = (ContentKind)reader.GetByte();
        var json = Encoding.UTF8.GetString(reader.GetBlob());
        if (!player.IsAdmin)
        {
            Tell(player, "Só administradores podem editar o jogo.");
            return;
        }

        try
        {
            switch (kind)
            {
                case ContentKind.Items:
                {
                    var items = ItemCatalog.Parse(json);
                    Validate(items.Select(i => (i.Id, i.Name)), "item", "itens");
                    var text = ItemCatalog.ToJson(items.OrderBy(i => i.Id));
                    Write(_items.SourcePath ?? Path.Combine(DataPaths.Data, "items.json"), text);
                    _items.Replace(text);
                    BroadcastAll(PacketType.ItemCatalog, _items.Json);
                    Tell(player, $"{items.Count} itens salvos.");
                    break;
                }
                case ContentKind.Npcs:
                {
                    var npcs = NpcCatalog.Parse(json);
                    Validate(npcs.Select(n => (n.Id, n.Name)), "NPC", "NPCs");
                    if (npcs.Any(n => n.Hp < 1 || n.MoveMs < 100 || n.AttackMs < 100 || n.RespawnSeconds < 0 || n.Range < 0))
                        throw new InvalidDataException("Valores de NPC fora do permitido.");
                    var text = NpcCatalog.ToJson(npcs.OrderBy(n => n.Id));
                    Write(_npcs.SourcePath ?? Path.Combine(DataPaths.Data, "npcs.json"), text);
                    _npcs.Replace(text);
                    BroadcastAll(PacketType.NpcCatalog, _npcs.Json);
                    // NPCs already walking around carry their old definition: spawn every map's again.
                    foreach (var mapId in _states.Keys.ToList()) ResetState(_maps.Get(mapId));
                    Tell(player, $"{npcs.Count} NPCs salvos.");
                    break;
                }
                default:
                    Tell(player, "Tipo de conteúdo desconhecido.");
                    return;
            }
            Log.Info($"{player.Name} saved {kind}.");
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
        {
            Tell(player, $"Não foi possível salvar: {ex.Message}");
            Log.Warn($"{player.Name} sent bad {kind}: {ex.Message}");
        }
    }

    private void HandleMapList(Player player)
    {
        if (!player.IsAdmin) return;
        var ids = _maps.Ids().ToList();
        var w = PacketIO.Begin(PacketType.MapList);
        w.Put(ids.Count);
        foreach (var id in ids)
        {
            w.Put(id);
            w.Put(_maps.Get(id).Name);
        }
        player.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }

    private static void Validate(IEnumerable<(int id, string name)> entries, string what, string plural)
    {
        var list = entries.ToList();
        if (list.Any(e => e.id <= 0)) throw new InvalidDataException($"Todo {what} precisa de um número maior que zero.");
        if (list.GroupBy(e => e.id).Any(g => g.Count() > 1)) throw new InvalidDataException($"Há {plural} com o mesmo número.");
        if (list.Any(e => string.IsNullOrWhiteSpace(e.name))) throw new InvalidDataException($"Todo {what} precisa de um nome.");
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Sends to everyone logged in, in the world or still at the character screen.</summary>
    private void BroadcastAll(PacketType type, string text)
    {
        var w = PacketIO.Begin(type);
        w.Put(text);
        foreach (var session in _sessions.Values.Where(s => s.Account != null)) session.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }
}

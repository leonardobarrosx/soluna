using System.Text.Json;
using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;

namespace Soluna.Server;

internal sealed partial class GameServer
{
    private readonly EvolutionRules _evolution = EvolutionRules.Load();
    private readonly AdventureContent _adventure = AdventureContent.Load();
    private readonly Dictionary<(int player, string action), long> _activityTimers = [];
    private long _lastFortuneMs;

    private void SendJourney(Player p)
    {
        RefreshLuck(p);
        var w = PacketIO.Begin(PacketType.Journey);
        w.Put(JsonSerializer.Serialize(p.Journey));
        p.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }
    private void SaveJourney(Player p)
    {
        p.Store();
        _accounts.Save(p.Account);
        SendJourney(p);
    }
    private void SendAdventureCatalog(Player p)
    {
        var w = PacketIO.Begin(PacketType.AdventureCatalog);
        w.Put(JsonSerializer.Serialize(_adventure));
        p.Peer.Send(w, DeliveryMethod.ReliableOrdered);
        BroadcastForm(p);
    }
    private void BroadcastForm(Player p)
    {
        var w = FormPacket(p);
        Broadcast(p.MapId, w);
    }
    private static NetDataWriter FormPacket(Player p)
    {
        var w = PacketIO.Begin(PacketType.FormChanged);
        w.Put(p.Id);
        w.Put(p.Journey.Tint);
        w.Put(p.Journey.Accent);
        w.Put(p.Journey.Silhouette);
        w.Put(p.Journey.Maturity);
        w.Put(p.Journey.Bloodline);
        return w;
    }
    private void RecordHabit(Player p, string habit, int amount = 1)
    {
        p.Journey.Record(habit, amount);
        if (_evolution.Apply(p.Journey))
        {
            Tell(p, p.Journey.Trace);
            BroadcastForm(p);
            p.Hp = Math.Min(p.Hp, p.MaxHp);
            p.Mp = Math.Min(p.Mp, p.MaxMp);
            SendVitals(p);
        }
        SendJourney(p);
    }
    private void Memory(Player p, string id, string message)
    {
        if (!p.Account.Legacy.Memories.Add(id)) return;
        Tell(p, "Uma memória permanece: " + message);
        _evolution.Apply(p.Journey);
    }
    private void RefreshLuck(Player p)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var map = _maps.Get(p.MapId);
        var phase = (int)(now / 180 % 4);
        p.Journey.LuckSources =
        [
            new("Marca de nascimento", p.Account.Legacy.InnateLuck),
            new("Vozes dos espíritos", new[] { -3, 2, 5, -1 }[phase]),
            new("Natureza", map.Depth > 0 ? -Math.Min(8, map.Depth / 3) : map.Id == 2 ? 3 : 1),
            new("Memórias", Math.Min(4, p.Account.Legacy.Memories.Count / 2)),
            new("Relíquias", p.Equipment.Items.Sum(id => _items.Get(id)?.Luck ?? 0)),
        ];
        if (p.Journey.BlessingUntil > now) p.Journey.LuckSources.Add(new("Bênção do santuário", 6));
        if (p.Journey.CurseUntil > now) p.Journey.LuckSources.Add(new("Maldição das ruínas", -9));
        if (p.Journey.TeaUntil > now) p.Journey.LuckSources.Add(new("Chá dos espíritos", 7));
    }
    private void TickFortune()
    {
        if (_clock.ElapsedMilliseconds - _lastFortuneMs < 5000) return;
        _lastFortuneMs = _clock.ElapsedMilliseconds;
        foreach (var p in Players) SendJourney(p);
    }
    private bool Near(Player p, LandmarkKind kind) => _maps.Get(p.MapId).Landmarks.Any(l => l.Kind == kind && Math.Abs(l.X - p.X) + Math.Abs(l.Y - p.Y) <= 3);

    private void HandleAdventure(Player p, NetDataReader reader)
    {
        var action = (AdventureAction)reader.GetByte();
        if (!Enum.IsDefined(action)) return;
        var now = _clock.ElapsedMilliseconds;
        if (action == AdventureAction.Skill) { Skill(p, now); return; }
        if (now < p.NextActionMs) return;
        p.NextActionMs = now + 400;
        RefreshLuck(p);
        if (action == AdventureAction.Interact) { Interact(p); return; }
        if (action == AdventureAction.Craft)
        {
            var id = reader.GetString();
            if (id.Length > 80) return;
            Craft(p, id);
            return;
        }
        if (action is AdventureAction.BuyTonic or AdventureAction.BuyEther or AdventureAction.BuyBlade or AdventureAction.SellMaterials)
        {
            if (!Near(p, LandmarkKind.Merchant)) { Tell(p, "Aproxime-se do mercador de cinzas."); return; }
            if (action == AdventureAction.SellMaterials)
            {
                var value = 0;
                foreach (var resource in _adventure.Resources)
                {
                    // Keep ten of every material for recipes; only sell the surplus.
                    var sell = Math.Max(0, p.Journey.Resources.GetValueOrDefault(resource.Id) - 10);
                    p.Journey.Resources[resource.Id] = p.Journey.Resources.GetValueOrDefault(resource.Id) - sell;
                    value += sell * resource.Value;
                }
                p.Journey.Gold = Math.Min(1_000_000, p.Journey.Gold + value);
                Tell(p, $"Excedentes trocados por {value} ouro. Dez de cada material foram reservados.");
            }
            else
            {
                var price = action == AdventureAction.BuyBlade ? 60 : 8;
                if (p.Journey.Gold < price) { Tell(p, "Ouro insuficiente."); return; }
                if (action == AdventureAction.BuyBlade)
                {
                    if (_items.Get(101) == null || p.Inventory.Contains(101)) { Tell(p, "Você já possui essa lâmina."); return; }
                    p.Inventory.Add(101); SendInventory(p);
                }
                else
                {
                    var supply = action == AdventureAction.BuyTonic ? Supply.Tonic : Supply.Ether;
                    if (p.Journey.Count(supply) >= 99) return;
                    p.Journey.Add(supply);
                }
                p.Journey.Gold -= price;
                Tell(p, "A troca foi concluída.");
            }
            SaveJourney(p);
            return;
        }
        if (action == AdventureAction.ReturnHome)
        {
            if (_clock.ElapsedMilliseconds - p.LastCombatMs < 7000) { Tell(p, "A passagem não se abre durante o combate."); return; }
            var home = _maps.Get(MapStore.StartMapId);
            Transfer(p, home.Id, home.Spawn.x, home.Spawn.y);
            SaveJourney(p); return;
        }
        var used = action switch
        {
            AdventureAction.Tonic => Supply.Tonic, AdventureAction.Ether => Supply.Ether,
            AdventureAction.Moonfruit => Supply.Moonfruit, AdventureAction.Wildroot => Supply.Wildroot,
            AdventureAction.Emberseed => Supply.Emberseed, AdventureAction.FortuneTea => Supply.FortuneTea,
            AdventureAction.AshSalt => Supply.AshSalt, _ => (Supply?)null,
        };
        if (used is not { } supplyUsed) return;
        if (supplyUsed == Supply.Tonic && p.Hp >= p.MaxHp || supplyUsed == Supply.Ether && p.Mp >= p.MaxMp)
        { Tell(p, "Você ainda não precisa disso."); return; }
        if (!p.Journey.Consume(supplyUsed)) { Tell(p, "Sua bolsa está vazia."); return; }
        var seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        switch (supplyUsed)
        {
            case Supply.Tonic: p.Hp = Math.Min(p.MaxHp, p.Hp + 40); break;
            case Supply.Ether: p.Mp = Math.Min(p.MaxMp, p.Mp + 25); break;
            case Supply.Moonfruit: p.Mp = Math.Min(p.MaxMp, p.Mp + 12); RecordHabit(p, "moonfood"); break;
            case Supply.Wildroot: p.Hp = Math.Min(p.MaxHp, p.Hp + 18); RecordHabit(p, "wildfood"); break;
            case Supply.Emberseed: p.Hp = Math.Min(p.MaxHp, p.Hp + 10); RecordHabit(p, "emberfood"); break;
            case Supply.FortuneTea: p.Journey.TeaUntil = seconds + 120; break;
            case Supply.AshSalt: p.Journey.CurseUntil = 0; break;
        }
        Tell(p, $"Você consumiu {JourneyRules.SupplyName(supplyUsed).ToLowerInvariant()}.");
        SendVitals(p); SaveJourney(p);
    }
    private void Interact(Player p)
    {
        var map = _maps.Get(p.MapId);
        var landmark = map.Landmarks.Where(l => Math.Abs(l.X - p.X) + Math.Abs(l.Y - p.Y) <= 3)
            .OrderBy(l => Math.Abs(l.X - p.X) + Math.Abs(l.Y - p.Y)).FirstOrDefault();
        if (landmark == null) { Tell(p, "Nada responde aqui. Procure um sinal na paisagem."); return; }
        var key = $"{map.Seed}:{map.Id}:{landmark.X}:{landmark.Y}";
        switch (landmark.Kind)
        {
            case LandmarkKind.Fishing: case LandmarkKind.Forage: case LandmarkKind.Mining:
            case LandmarkKind.Woodcutting: case LandmarkKind.Extraction:
                Gather(p, landmark); return;
            case LandmarkKind.Shrine:
                if (p.Journey.BlessingUntil > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) { Tell(p, "O santuário ainda vela por você."); return; }
                p.Journey.BlessingUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 180;
                p.Hp = p.MaxHp; p.Mp = p.MaxMp;
                RecordHabit(p, "spirits");
                Tell(p, "Uma presença antiga pousa sobre seus ombros."); SendVitals(p); break;
            case LandmarkKind.Merchant: Tell(p, "O mercador espera. Abra o diário [J], aba Trocas. Excedentes: apenas acima de dez unidades."); break;
            case LandmarkKind.Workbench: Tell(p, "Bancada de ofícios. Abra [C] para combinar seus materiais."); break;
            case LandmarkKind.Noticeboard:
                if (p.Journey.Kills >= 6 && p.Journey.Rewards.Add("first-hunt"))
                { p.Journey.Gold += 45; p.Journey.Add(Supply.Tonic, 3); Memory(p, "hunt", "a primeira vigília"); Tell(p, "Vigília concluída. 45 ouro e três tônicos."); }
                else if (p.Journey.Bosses > 0 && p.Journey.Rewards.Add("eclipse"))
                { p.Journey.Gold += 100; Memory(p, "eclipse", "o coração do eclipse"); Tell(p, "O sino volta a soar. A vila se lembrará desta vida."); }
                else Tell(p, JourneyRules.Objective(p.Journey));
                break;
            case LandmarkKind.Chest:
                if (!p.Journey.Rewards.Add(key)) { Tell(p, "Restam apenas ecos neste relicário."); return; }
                p.Journey.Gold += 12 + Math.Min(200, map.Depth * 3);
                p.Journey.Add((Supply)_rng.Next(5), 2);
                AddResource(p, "echo", Fortune.Yield(p.Journey.Luck, _rng));
                if (_rng.NextDouble() < .18 && p.Journey.Luck < 5)
                { p.Journey.CurseUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120; Tell(p, "O relicário cedeu. Algo o seguiu de volta."); }
                else Tell(p, "O relicário guardava ouro, provisões e ecos antigos.");
                RecordHabit(p, "relics"); break;
            case LandmarkKind.Expedition: EnterFloor(p, 1); return;
            case LandmarkKind.Descent:
                if (StateFor(map).Npcs.Any(n => n.Alive && n.Def.Hostile)) { Tell(p, "A escada está selada. Silencie as criaturas deste andar."); return; }
                EnterFloor(p, map.Depth + 1); return;
            case LandmarkKind.Exit:
                var home = _maps.Get(MapStore.StartMapId); Transfer(p, home.Id, home.Spawn.x, home.Spawn.y); break;
        }
        SaveJourney(p);
    }
    private void Gather(Player p, Landmark landmark)
    {
        var id = landmark.Kind.ToString().ToLowerInvariant();
        var activity = _adventure.Activities.FirstOrDefault(a => a.Id == id);
        if (activity == null) return;
        var timer = (p.Id, id);
        var now = _clock.ElapsedMilliseconds;
        if (_activityTimers.GetValueOrDefault(timer) > now) { Tell(p, "Deixe a natureza respirar mais um instante."); return; }
        _activityTimers[timer] = now + Math.Max(1, activity.CooldownSeconds) * 1000L;
        var count = Fortune.Yield(p.Journey.Luck, _rng);
        AddResource(p, activity.Resource, count);
        if (activity.Supply >= 0 && _rng.NextDouble() < .35 + Fortune.RareChance(p.Journey.Luck)) p.Journey.Add((Supply)activity.Supply);
        if (landmark.Kind == LandmarkKind.Fishing)
        {
            p.Journey.Catches++; p.Account.Legacy.Catches++;
            if (p.Account.Legacy.Catches >= 10) Memory(p, "tide", "a paciência das águas");
        }
        else p.Journey.Harvests++;
        RecordHabit(p, activity.Habit);
        if (p.Journey.Harvests >= 12) Memory(p, "hands", "as mãos que aprenderam a criar");
        Tell(p, $"{activity.Message} +{count} {_adventure.ResourceName(activity.Resource)}.");
        GainExp(p, 3); SaveJourney(p);
    }
    private static void AddResource(Player p, string id, int count) => p.Journey.Resources[id] = Math.Clamp(p.Journey.Resources.GetValueOrDefault(id) + count, 0, 999);
    private void Craft(Player p, string id)
    {
        if (!Near(p, LandmarkKind.Workbench)) { Tell(p, "Aproxime-se de uma bancada de ofícios."); return; }
        var recipe = _adventure.Recipes.FirstOrDefault(r => r.Id == id);
        if (recipe == null || !_adventure.CanCraft(p.Journey, recipe)) { Tell(p, "Faltam materiais para essa receita."); return; }
        if (recipe.Item > 0 && (p.Inventory.Contains(recipe.Item) || _items.Get(recipe.Item) == null)) { Tell(p, "Você já possui esse equipamento."); return; }
        if (recipe.Supply >= 0 && p.Journey.Count((Supply)recipe.Supply) + recipe.Amount > 99) return;
        foreach (var cost in recipe.Costs) p.Journey.Resources[cost.Key] -= cost.Value;
        if (recipe.Item > 0) { p.Inventory.Add(recipe.Item); SendInventory(p); }
        if (recipe.Supply >= 0) p.Journey.Add((Supply)recipe.Supply, recipe.Amount);
        if (recipe.Resource.Length > 0) AddResource(p, recipe.Resource, recipe.Amount);
        RecordHabit(p, "craft");
        Tell(p, $"Você fabricou {recipe.Name}."); SaveJourney(p);
    }
    private void Skill(Player p, long now)
    {
        if (now < p.NextSkillMs || p.Mp < p.Journey.SkillCost) return;
        p.NextSkillMs = now + p.Journey.SkillCooldownMs;
        p.Mp -= p.Journey.SkillCost;
        p.LastCombatMs = now;
        var pulse = PacketIO.Begin(PacketType.Pulse); pulse.Put(p.X); pulse.Put(p.Y); Broadcast(p.MapId, pulse);
        var map = _maps.Get(p.MapId);
        var targets = StateFor(map).Npcs.Where(n => n.Alive && n.Def.Hostile && Distance(n, p) <= 3).ToList();
        Broadcast(map.Id, Attacked(UnitKind.Player, p.Id, p.Dir));
        if (targets.Count > 0) { p.Journey.Spells++; RecordHabit(p, "magic"); }
        foreach (var target in targets)
        { target.Target = p.Id; HurtNpc(map, target, Formulas.Damage(p.AttackPower(_items) + 7, target.Def.Defense, _rng), p); }
        SendVitals(p); SendJourney(p);
    }
    private void RewardVictory(Player p, NpcInstance npc)
    {
        p.Journey.Kills++; p.Account.Legacy.Kills++;
        p.Journey.Gold = Math.Min(1_000_000, p.Journey.Gold + 3 + Math.Min(50, npc.Def.Exp / 5));
        RecordHabit(p, "hunt");
        var loot = _rng.NextDouble() < Fortune.RareChance(p.Journey.Luck) ? Supply.Moonfruit : Supply.Tonic;
        if (_rng.NextDouble() < .4) p.Journey.Add(loot);
        AddResource(p, "echo", 1);
        if (npc.Def.Boss)
        {
            p.Journey.Bosses++; p.Account.Legacy.Bosses++;
            p.Journey.Add(Supply.Emberseed, 3); p.Journey.Add(Supply.Moonfruit, 2);
            RecordHabit(p, "boss"); Memory(p, "guardian", "a queda do guardião");
            Tell(p, "O Guardião do Eclipse se desfez. A escada responde ao seu toque.");
        }
        if (p.Account.Legacy.Kills >= 10) Memory(p, "hunter", "o instinto da caçada");
        SaveJourney(p);
    }
    private void EnterFloor(Player p, int depth)
    {
        // There is no authored final floor. Only representable map IDs bound the implementation.
        if (depth < 1 || depth > int.MaxValue - 1000) return;
        if (_adventure.Dungeon == null) { Tell(p, "Não há expedições neste mundo."); return; }
        if (depth == 1 && !Players.Any(other => _maps.Get(other.MapId).Depth > 0))
        {
            _maps.NewExpedition();
            foreach (var id in _states.Keys.Where(id => _maps.Get(id).Depth > 0).ToList()) _states.Remove(id);
        }
        var map = _maps.Expedition(_adventure.Dungeon, depth);
        p.Journey.DeepestFloor = Math.Max(p.Journey.DeepestFloor, depth);
        if (depth == 1) p.Journey.Expeditions++;
        RecordHabit(p, "ruins");
        Transfer(p, map.Id, map.Spawn.x, map.Spawn.y);
        Tell(p, $"As ruínas se fecham atrás de você. Profundidade {depth}.");
        SaveJourney(p);
    }
    private void Rebirth(Player p)
    {
        p.Journey = Journey.Reborn(p.Account.Legacy);
        p.Level = 1; p.Exp = 0;
        p.Look = new Appearance(0, 0, 0, 0, 0);
        p.Equipment = _items.StarterEquipment();
        p.Inventory.Clear(); p.Inventory.AddRange(_items.StarterItems);
        p.NextSkillMs = 0;
        p.Hp = p.MaxHp; p.Mp = p.MaxMp;
        SendInventory(p);
        var look = PacketIO.Begin(PacketType.PlayerLook); look.Put(p.Id); look.Put(p.Equipment); Broadcast(p.MapId, look);
        BroadcastForm(p);
        p.Store(); _accounts.Save(p.Account);
    }
}

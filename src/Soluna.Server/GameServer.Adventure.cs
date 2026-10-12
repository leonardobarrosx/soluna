using System.Text.Json;
using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;

namespace Soluna.Server;

/// <summary>
/// Activities, crafting, supplies, the merchant, objectives and luck. The mechanics live here; every
/// name, price, reward and message comes from the game's adventure.json, edited in the game editor.
/// </summary>
internal sealed partial class GameServer
{
    private EvolutionRules _evolution = EvolutionRules.Load();
    private AdventureContent _adventure = AdventureContent.Load();
    private readonly Dictionary<(int player, string action), long> _activityTimers = [];
    private long _lastFortuneMs;

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private string Say(string key, string fallback, params object[] args) => string.Format(_adventure.Text(key, fallback), args);

    private void SendJourney(Player p)
    {
        RefreshLuck(p);
        var w = PacketIO.Begin(PacketType.Journey);
        w.Put(JsonSerializer.Serialize(p.Journey));
        p.Peer.Send(w, DeliveryMethod.ReliableOrdered);
    }
    private void SaveJourney(Player p)
    {
        CheckMemories(p);
        p.Store();
        _accounts.Save(p.Account);
        SendJourney(p);
    }
    private void SendAdventureCatalog(Player p)
    {
        var w = PacketIO.Begin(PacketType.AdventureCatalog);
        w.Put(_adventure.ToJson());
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
        if (habit.Length == 0) return;
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
    private void Memory(Player p, string id)
    {
        if (id.Length == 0 || !p.Account.Legacy.Memories.Add(id)) return;
        Tell(p, Say("memory", "Uma memória permanece: {0}", _adventure.MemoryName(id)));
        _evolution.Apply(p.Journey);
    }
    /// <summary>Memories whose counter reached its goal. Cheap, so it runs whenever a journey is saved.</summary>
    private void CheckMemories(Player p)
    {
        foreach (var m in _adventure.Memories)
            if (m.Counter.Length > 0 && p.Journey.Counter(m.Counter) >= m.Goal) Memory(p, m.Id);
    }
    private void RefreshLuck(Player p)
    {
        var now = Now;
        var map = _maps.Get(p.MapId);
        var luck = _adventure.Luck;
        var phases = luck.SpiritPhases;
        p.Journey.LuckSources = [new(luck.Innate, p.Account.Legacy.InnateLuck)];
        if (phases.Length > 0) p.Journey.LuckSources.Add(new(luck.Spirits, phases[(int)(now / Math.Max(1, luck.PhaseSeconds) % phases.Length)]));
        p.Journey.LuckSources.Add(new(luck.Nature, map.Depth > 0 ? -Math.Min(8, map.Depth / 3) : map.Luck));
        p.Journey.LuckSources.Add(new(luck.Memories, Math.Min(4, p.Account.Legacy.Memories.Count / 2)));
        p.Journey.LuckSources.Add(new(luck.Relics, p.Equipment.Items.Sum(id => _items.Get(id)?.Luck ?? 0)));
        foreach (var effect in _adventure.Effects)
            if (p.Journey.HasEffect(effect.Id, now)) p.Journey.LuckSources.Add(new(effect.Name, effect.Luck));
    }
    private void StartEffect(Player p, string id)
    {
        if (_adventure.Effect(id) is { } effect) p.Journey.Effects[id] = Now + Math.Max(1, effect.Seconds);
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
        var arg = action is AdventureAction.Craft or AdventureAction.Use or AdventureAction.Buy ? reader.GetString() : "";
        if (arg.Length > 80) return;
        var now = _clock.ElapsedMilliseconds;
        if (action == AdventureAction.Skill) { Skill(p, now); return; }
        if (now < p.NextActionMs) return;
        p.NextActionMs = now + 400;
        RefreshLuck(p);
        switch (action)
        {
            case AdventureAction.Interact: Interact(p); break;
            case AdventureAction.Craft: Craft(p, arg); break;
            case AdventureAction.Use: Use(p, arg); break;
            case AdventureAction.Buy: Buy(p, arg); break;
            case AdventureAction.SellMaterials: Sell(p); break;
            case AdventureAction.ReturnHome:
                if (_clock.ElapsedMilliseconds - p.LastCombatMs < 7000) { Tell(p, Say("returnBlocked", "Não é possível voltar durante o combate.")); return; }
                var home = _maps.Get(MapStore.StartMapId);
                Transfer(p, home.Id, home.Spawn.x, home.Spawn.y);
                SaveJourney(p);
                break;
        }
    }

    private void Use(Player p, string id)
    {
        if (_adventure.Supply(id) is not { } supply) return;
        // Something that only restores is not wasted on a full bar.
        var onlyRestores = supply.Habit.Length == 0 && supply.Effect.Length == 0 && supply.Cleanse.Length == 0;
        if (onlyRestores && (supply.Hp <= 0 || p.Hp >= p.MaxHp) && (supply.Mp <= 0 || p.Mp >= p.MaxMp))
        { Tell(p, Say("notNeeded", "Você ainda não precisa disso.")); return; }
        if (!p.Journey.Consume(id)) { Tell(p, Say("emptyBag", "Sua bolsa está vazia.")); return; }
        p.Hp = Math.Min(p.MaxHp, p.Hp + supply.Hp);
        p.Mp = Math.Min(p.MaxMp, p.Mp + supply.Mp);
        StartEffect(p, supply.Effect);
        if (supply.Cleanse.Length > 0) p.Journey.Effects.Remove(supply.Cleanse);
        RecordHabit(p, supply.Habit);
        Tell(p, Say("consumed", "Você usou {0}.", supply.Name.ToLowerInvariant()));
        SendVitals(p); SaveJourney(p);
    }

    private void Buy(Player p, string arg)
    {
        if (!Near(p, LandmarkKind.Merchant)) { Tell(p, Say("merchantFar", "Aproxime-se do mercador.")); return; }
        if (!int.TryParse(arg, out var index) || index < 0 || index >= _adventure.Shop.Offers.Count) return;
        var offer = _adventure.Shop.Offers[index];
        if (p.Journey.Gold < offer.Price) { Tell(p, Say("noGold", "Ouro insuficiente.")); return; }
        if (offer.Item > 0)
        {
            if (_items.Get(offer.Item) == null || p.Inventory.Contains(offer.Item)) { Tell(p, Say("ownItem", "Você já possui esse item.")); return; }
            p.Inventory.Add(offer.Item); SendInventory(p);
        }
        else
        {
            if (_adventure.Supply(offer.Supply) == null || p.Journey.Count(offer.Supply) >= 99) return;
            p.Journey.Add(offer.Supply);
        }
        p.Journey.Gold -= offer.Price;
        Tell(p, Say("tradeDone", "A troca foi concluída."));
        SaveJourney(p);
    }

    private void Sell(Player p)
    {
        if (!Near(p, LandmarkKind.Merchant)) { Tell(p, Say("merchantFar", "Aproxime-se do mercador.")); return; }
        var keep = Math.Max(0, _adventure.Shop.KeepPerResource);
        var value = 0;
        foreach (var resource in _adventure.Resources)
        {
            var sell = Math.Max(0, p.Journey.Resources.GetValueOrDefault(resource.Id) - keep);
            p.Journey.Resources[resource.Id] = p.Journey.Resources.GetValueOrDefault(resource.Id) - sell;
            value += sell * resource.Value;
        }
        p.Journey.Gold = Math.Min(1_000_000, p.Journey.Gold + value);
        Tell(p, Say("sold", "Materiais vendidos por {0} ouro.", value));
        SaveJourney(p);
    }

    private void Interact(Player p)
    {
        var map = _maps.Get(p.MapId);
        var landmark = map.Landmarks.Where(l => Math.Abs(l.X - p.X) + Math.Abs(l.Y - p.Y) <= 3)
            .OrderBy(l => Math.Abs(l.X - p.X) + Math.Abs(l.Y - p.Y)).FirstOrDefault();
        if (landmark == null) { Tell(p, Say("nothingHere", "Não há nada aqui.")); return; }
        var key = $"{map.Seed}:{map.Id}:{landmark.X}:{landmark.Y}";
        switch (landmark.Kind)
        {
            case LandmarkKind.Fishing: case LandmarkKind.Forage: case LandmarkKind.Mining:
            case LandmarkKind.Woodcutting: case LandmarkKind.Extraction:
                Gather(p, landmark); return;
            case LandmarkKind.Shrine:
                var blessing = _adventure.Luck.ShrineEffect;
                if (p.Journey.HasEffect(blessing, Now)) { Tell(p, Say("shrineActive", "O santuário ainda protege você.")); return; }
                StartEffect(p, blessing);
                p.Hp = p.MaxHp; p.Mp = p.MaxMp;
                RecordHabit(p, "spirits");
                Tell(p, Say("shrine", "Você se sente renovado.")); SendVitals(p); break;
            case LandmarkKind.Merchant: Tell(p, Say("merchant", "Abra o diário [J], aba Trocas.")); break;
            case LandmarkKind.Workbench: Tell(p, Say("workbench", "Abra [C] para fabricar.")); break;
            case LandmarkKind.Noticeboard: ClaimObjective(p); break;
            case LandmarkKind.Chest:
                if (!p.Journey.Rewards.Add(key)) { Tell(p, Say("chestEmpty", "Este baú está vazio.")); return; }
                var loot = _adventure.Loot;
                p.Journey.Gold += loot.ChestGold + Math.Min(200, map.Depth * loot.ChestGoldPerDepth);
                if (loot.ChestSupplies.Count > 0) p.Journey.Add(loot.ChestSupplies[_rng.Next(loot.ChestSupplies.Count)], 2);
                if (loot.ChestResource.Length > 0) AddResource(p, loot.ChestResource, Fortune.Yield(p.Journey.Luck, _rng));
                if (loot.ChestCurse.Length > 0 && _rng.NextDouble() < loot.ChestCurseChance && p.Journey.Luck < 5)
                { StartEffect(p, loot.ChestCurse); Tell(p, Say("chestCursed", "Algo saiu do baú com você.")); }
                else Tell(p, Say("chest", "Você abriu o baú."));
                RecordHabit(p, "relics"); break;
            case LandmarkKind.Expedition: EnterFloor(p, 1); return;
            case LandmarkKind.Descent:
                if (StateFor(map).Npcs.Any(n => n.Alive && n.Def.Hostile)) { Tell(p, Say("descentSealed", "Derrote as criaturas deste andar para descer.")); return; }
                EnterFloor(p, map.Depth + 1); return;
            case LandmarkKind.Exit:
                var home = _maps.Get(MapStore.StartMapId); Transfer(p, home.Id, home.Spawn.x, home.Spawn.y); break;
        }
        SaveJourney(p);
    }

    /// <summary>The noticeboard pays out the current objective once its counter reaches the goal.</summary>
    private void ClaimObjective(Player p)
    {
        if (_adventure.CurrentObjective(p.Journey) is not { } o) { Tell(p, _adventure.ObjectivesDone); return; }
        if (p.Journey.Counter(o.Counter) < o.Goal) { Tell(p, _adventure.ObjectiveLine(p.Journey)); return; }
        p.Journey.Rewards.Add(o.Id);
        p.Journey.Gold = Math.Min(1_000_000, p.Journey.Gold + o.Gold);
        foreach (var (supply, count) in o.Supplies) p.Journey.Add(supply, count);
        Memory(p, o.Memory);
        Tell(p, o.Message.Length > 0 ? o.Message : Say("objectiveDone", "Objetivo concluído."));
    }

    private void Gather(Player p, Landmark landmark)
    {
        var id = landmark.Kind.ToString().ToLowerInvariant();
        var activity = _adventure.Activities.FirstOrDefault(a => a.Id == id);
        if (activity == null) return;
        var timer = (p.Id, id);
        var now = _clock.ElapsedMilliseconds;
        if (_activityTimers.GetValueOrDefault(timer) > now) { Tell(p, Say("gatherCooldown", "Espere um instante.")); return; }
        _activityTimers[timer] = now + Math.Max(1, activity.CooldownSeconds) * 1000L;
        var count = Fortune.Yield(p.Journey.Luck, _rng);
        AddResource(p, activity.Resource, count);
        if (activity.Supply.Length > 0 && _rng.NextDouble() < .35 + Fortune.RareChance(p.Journey.Luck)) p.Journey.Add(activity.Supply);
        if (landmark.Kind == LandmarkKind.Fishing) { p.Journey.Catches++; p.Account.Legacy.Catches++; }
        else p.Journey.Harvests++;
        RecordHabit(p, activity.Habit);
        Tell(p, $"{activity.Message} +{count} {_adventure.ResourceName(activity.Resource)}.");
        GainExp(p, 3); SaveJourney(p);
    }
    private static void AddResource(Player p, string id, int count) => p.Journey.Resources[id] = Math.Clamp(p.Journey.Resources.GetValueOrDefault(id) + count, 0, 999);
    private void Craft(Player p, string id)
    {
        if (!Near(p, LandmarkKind.Workbench)) { Tell(p, Say("benchFar", "Aproxime-se de uma bancada.")); return; }
        var recipe = _adventure.Recipes.FirstOrDefault(r => r.Id == id);
        if (recipe == null || !_adventure.CanCraft(p.Journey, recipe)) { Tell(p, Say("missing", "Faltam materiais para essa receita.")); return; }
        if (recipe.Item > 0 && (p.Inventory.Contains(recipe.Item) || _items.Get(recipe.Item) == null)) { Tell(p, Say("ownItem", "Você já possui esse item.")); return; }
        if (recipe.Supply.Length > 0 && p.Journey.Count(recipe.Supply) + recipe.Amount > 99) return;
        foreach (var cost in recipe.Costs) p.Journey.Resources[cost.Key] -= cost.Value;
        if (recipe.Item > 0) { p.Inventory.Add(recipe.Item); SendInventory(p); }
        if (recipe.Supply.Length > 0) p.Journey.Add(recipe.Supply, recipe.Amount);
        if (recipe.Resource.Length > 0) AddResource(p, recipe.Resource, recipe.Amount);
        RecordHabit(p, "craft");
        Tell(p, Say("crafted", "Você fabricou {0}.", recipe.Name)); SaveJourney(p);
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
        var loot = _adventure.Loot;
        p.Journey.Kills++; p.Account.Legacy.Kills++;
        p.Journey.Gold = Math.Min(1_000_000, p.Journey.Gold + 3 + Math.Min(50, npc.Def.Exp / 5));
        RecordHabit(p, "hunt");
        var supply = loot.RareSupply.Length > 0 && _rng.NextDouble() < Fortune.RareChance(p.Journey.Luck) ? loot.RareSupply : loot.Supply;
        if (supply.Length > 0 && _rng.NextDouble() < loot.SupplyChance) p.Journey.Add(supply);
        if (loot.Resource.Length > 0) AddResource(p, loot.Resource, 1);
        if (npc.Def.Boss)
        {
            p.Journey.Bosses++; p.Account.Legacy.Bosses++;
            foreach (var (id, count) in loot.BossSupplies) p.Journey.Add(id, count);
            RecordHabit(p, "boss");
            if (loot.BossMessage.Length > 0) Tell(p, loot.BossMessage);
        }
        SaveJourney(p);
    }
    private void EnterFloor(Player p, int depth)
    {
        // There is no authored final floor. Only representable map IDs bound the implementation.
        if (depth < 1 || depth > int.MaxValue - 1000) return;
        if (_adventure.Dungeon is not { Enemies.Length: > 0 } dungeon) { Tell(p, Say("noDungeon", "Não há expedições neste mundo.")); return; }
        if (depth == 1 && !Players.Any(other => _maps.Get(other.MapId).Depth > 0))
        {
            _maps.NewExpedition();
            foreach (var id in _states.Keys.Where(id => _maps.Get(id).Depth > 0).ToList()) _states.Remove(id);
        }
        var map = _maps.Expedition(dungeon, depth);
        p.Journey.DeepestFloor = Math.Max(p.Journey.DeepestFloor, depth);
        if (depth == 1) p.Journey.Expeditions++;
        RecordHabit(p, "ruins");
        Transfer(p, map.Id, map.Spawn.x, map.Spawn.y);
        Tell(p, Say("enterFloor", "Profundidade {0}.", depth));
        SaveJourney(p);
    }
    private void Rebirth(Player p)
    {
        p.Account.Legacy.Lives++;
        p.Journey = _adventure.NewJourney(p.Account.Legacy);
        _evolution.Apply(p.Journey);
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

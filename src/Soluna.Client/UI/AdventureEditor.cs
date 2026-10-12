using Microsoft.Xna.Framework;
using Soluna.Client.Graphics;
using Soluna.Shared;

namespace Soluna.Client.UI;

/// <summary>
/// The game editor's adventure and evolution tabs: materials, professions, supplies, effects, recipes,
/// the shop, objectives, memories, general rules, texts and the forms habits lead to. Works on a copy
/// of what the server last sent; Save sends the whole file back, Discard returns to the server's copy.
/// </summary>
internal sealed class AdventureEditor(Gui gui)
{
    public enum Section { Resources, Activities, Supplies, Effects, Recipes, Shop, Objectives, Memories, General, Texts, Dungeon }

    private static readonly string[] SectionNames = ["Materiais", "Ofícios", "Suprimentos", "Efeitos", "Receitas", "Loja", "Objetivos", "Memórias", "Geral", "Textos", "Ruínas"];
    private Dictionary<string, MapProp>? _scenery;

    /// <summary>Landmark kinds that run a profession, with the name the editor shows.</summary>
    private static readonly (string id, string name)[] GatherKinds =
        [("fishing", "Pesca"), ("forage", "Coleta"), ("mining", "Mineração"), ("woodcutting", "Corte de madeira"), ("extraction", "Extração")];

    private static readonly Dictionary<string, string> CounterNames = new()
    {
        ["kills"] = "Vitórias nesta vida", ["bosses"] = "Chefes nesta vida", ["catches"] = "Pescas nesta vida",
        ["harvests"] = "Coletas nesta vida", ["expeditions"] = "Expedições nesta vida", ["depth"] = "Andar mais fundo",
        ["spells"] = "Pulsos que acertaram", ["account.kills"] = "Vitórias (conta)", ["account.bosses"] = "Chefes (conta)",
        ["account.catches"] = "Pescas (conta)", ["account.lives"] = "Vidas (conta)",
    };

    /// <summary>Habits the engine records by itself; professions and supplies add their own.</summary>
    private static readonly string[] EngineHabits = ["melee", "hunt", "magic", "boss", "craft", "explore", "relics", "ruins", "spirits"];

    /// <summary>Every message the server can say, with what it is for. Empty keeps the engine's plain default.</summary>
    private static readonly (string key, string what)[] TextKeys =
    [
        ("journalTitle", "Título do diário"), ("merchant", "Ao falar com o mercador"), ("merchantFar", "Comprar longe do mercador"),
        ("shopFar", "Aba de trocas, longe do mercador"), ("sold", "Venda de excedentes ({0} = ouro)"), ("tradeDone", "Compra feita"),
        ("noGold", "Ouro insuficiente"), ("ownItem", "Já tem o item"), ("workbench", "Ao usar a bancada"),
        ("benchFar", "Fabricar longe da bancada"), ("missing", "Faltam materiais"), ("crafted", "Fabricou ({0} = receita)"),
        ("nothingHere", "Agir sem nada por perto"), ("shrine", "Ao rezar no santuário"), ("shrineActive", "Santuário já ativo"),
        ("chest", "Baú aberto"), ("chestCursed", "Baú amaldiçoado"), ("chestEmpty", "Baú vazio"),
        ("descentSealed", "Descida bloqueada"), ("enterFloor", "Entrar num andar ({0} = profundidade)"),
        ("returnBlocked", "Voltar durante o combate"), ("gatherCooldown", "Ofício em recarga"), ("noDungeon", "Jogo sem masmorra"),
        ("notNeeded", "Usar cura sem precisar"), ("emptyBag", "Suprimento acabou"), ("consumed", "Usou ({0} = suprimento)"),
        ("memory", "Nova memória ({0} = nome)"), ("objectiveDone", "Objetivo sem mensagem"), ("rebirth", "Morte que recomeça a vida"),
        ("woke", "Morte sem recomeço"),
    ];

    private AdventureContent _draft = new();
    private EvolutionRules _evolution = new();
    private string _savedAdventure = new AdventureContent().ToJson();
    private string _savedEvolution = new EvolutionRules().ToJson();
    private readonly Dictionary<string, int> _selected = [];
    private readonly Dictionary<string, string> _textDrafts = [];

    public Section Current { get; set; }

    public bool AdventureDirty => _draft.ToJson() != _savedAdventure;
    public bool EvolutionDirty => _evolution.ToJson() != _savedEvolution;

    public string AdventureJson => _draft.ToJson();
    public string EvolutionJson => _evolution.ToJson();

    /// <summary>The server's copy arrived or changed: take it, unless there are edits not saved yet.</summary>
    public void SetAdventure(string json)
    {
        var dirty = AdventureDirty;
        _savedAdventure = AdventureContent.Parse(json).ToJson();
        if (!dirty) _draft = AdventureContent.Parse(_savedAdventure);
    }

    public void SetEvolution(string json)
    {
        var dirty = EvolutionDirty;
        _savedEvolution = EvolutionRules.Parse(json).ToJson();
        if (!dirty) _evolution = EvolutionRules.Parse(_savedEvolution);
    }

    public void DiscardAdventure() => _draft = AdventureContent.Parse(_savedAdventure);
    public void DiscardEvolution() => _evolution = EvolutionRules.Parse(_savedEvolution);

    /// <summary>What is wrong with the draft, checked before it is sent; the server checks again.</summary>
    public List<string> AdventureProblems(Func<int, bool> itemExists) => _draft.Problems(itemExists);
    public List<string> EvolutionProblems() => _evolution.Problems();

    // ---- Adventure ----

    public void DrawAdventure(Rectangle body, IReadOnlyList<ItemDef> items, IReadOnlyList<NpcDef> npcs)
    {
        var tabWidth = Math.Min(118, (body.Width - 9 * 4) / SectionNames.Length);
        for (var i = 0; i < SectionNames.Length; i++)
            if (gui.Button(new Rectangle(body.X + i * (tabWidth + 4), body.Y, tabWidth, 28), SectionNames[i], Current == (Section)i)) Current = (Section)i;
        var area = new Rectangle(body.X, body.Y + 40, body.Width, body.Height - 40);
        switch (Current)
        {
            case Section.Resources: DrawResources(area); break;
            case Section.Activities: DrawActivities(area); break;
            case Section.Supplies: DrawSupplies(area); break;
            case Section.Effects: DrawEffects(area); break;
            case Section.Recipes: DrawRecipes(area, items); break;
            case Section.Shop: DrawShop(area, items); break;
            case Section.Objectives: DrawObjectives(area); break;
            case Section.Memories: DrawMemories(area); break;
            case Section.General: DrawGeneral(area, npcs); break;
            case Section.Texts: DrawTexts(area); break;
            case Section.Dungeon: DrawDungeon(area); break;
        }
    }

    private void DrawResources(Rectangle area)
    {
        if (Column("resources", area, _draft.Resources, r => $"{r.Name}  ({r.Id})",
                () => new ResourceDef { Id = NewId("material", _draft.Resources.Select(r => r.Id)), Name = "Novo material", Value = 1 }) is not { } r) return;
        var (x, y, w) = Form(area);
        r.Id = Id($"res-id-{Sel("resources")}", x, y, w, "Identificador (usado nas receitas)", r.Id); y += 42;
        r.Name = gui.TextField($"res-name-{Sel("resources")}", Gui.Field(x, y, w), "Nome", r.Name); y += 42;
        r.Value = gui.NumberField($"res-value-{Sel("resources")}", Gui.Field(x, y, w), "Valor de venda (ouro)", r.Value, 0, 99999);
    }

    private void DrawActivities(Rectangle area)
    {
        var missing = GatherKinds.Where(k => _draft.Activities.All(a => a.Id != k.id)).ToList();
        if (Column("activities", area, _draft.Activities, a => KindName(a.Id),
                () => new ActivityDef { Id = missing.FirstOrDefault().id ?? "", Habit = missing.FirstOrDefault().id ?? "", Resource = _draft.Resources.FirstOrDefault()?.Id ?? "" },
                canAdd: missing.Count > 0, canDuplicate: false) is not { } a) return;
        var (x, y, w) = Form(area);
        gui.Label(new Vector2(x, y), $"Responde aos pontos de {KindName(a.Id).ToLowerInvariant()} dos mapas.", Theme.TextDim, small: true); y += 24;
        a.Resource = Pick(Gui.Field(x, y, w), "Material obtido", ResourceOptions(), a.Resource); y += 42;
        a.Supply = Pick(Gui.Field(x, y, w), "Suprimento que às vezes vem junto", SupplyOptions(true), a.Supply); y += 42;
        a.CooldownSeconds = gui.NumberField($"act-cd-{a.Id}", Gui.Field(x, y, w), "Recarga (segundos)", a.CooldownSeconds, 1, 3600); y += 42;
        a.Habit = Id($"act-habit-{a.Id}", x, y, w, "Hábito que registra (molda a evolução)", a.Habit); y += 42;
        a.Message = gui.TextField($"act-msg-{a.Id}", Gui.Field(x, y, w * 2), "Mensagem ao coletar", a.Message, 120);
    }

    private void DrawSupplies(Rectangle area)
    {
        if (Column("supplies", area, _draft.Supplies, s => s.Hotkey > 0 ? $"[{s.Hotkey}] {s.Name}" : s.Name,
                () => new SupplyDef { Id = NewId("suprimento", _draft.Supplies.Select(s => s.Id)), Name = "Novo suprimento" }) is not { } s) return;
        var (x, y, w) = Form(area);
        var k = Sel("supplies");
        s.Id = Id($"sup-id-{Sel("supplies")}", x, y, w, "Identificador", s.Id); y += 42;
        s.Name = gui.TextField($"sup-name-{k}", Gui.Field(x, y, w), "Nome", s.Name); y += 42;
        s.Description = gui.TextField($"sup-desc-{k}", Gui.Field(x, y, w), "Descrição", s.Description, 80); y += 42;
        var half = (w - 8) / 2;
        s.Hp = gui.NumberField($"sup-hp-{k}", Gui.Field(x, y, half), "Cura vida", s.Hp, 0, 9999, 5);
        s.Mp = gui.NumberField($"sup-mp-{k}", Gui.Field(x + half + 8, y, half), "Cura essência", s.Mp, 0, 9999, 5); y += 42;
        s.Hotkey = gui.NumberField($"sup-key-{k}", Gui.Field(x, y, w), "Tecla na barra (1 a 9, 0 = nenhuma)", s.Hotkey, 0, 9); y += 42;
        var x2 = x + w + 20;
        var y2 = area.Y;
        s.Effect = Pick(Gui.Field(x2, y2, w), "Efeito que inicia", EffectOptions(), s.Effect); y2 += 42;
        s.Cleanse = Pick(Gui.Field(x2, y2, w), "Efeito que encerra", EffectOptions(), s.Cleanse); y2 += 42;
        s.Habit = Id($"sup-habit-{k}", x2, y2, w, "Hábito ao usar (vazio = nenhum)", s.Habit); y2 += 46;
        gui.Label(new Vector2(x2, y2), "Um suprimento que só cura não é gasto\ncom a barra cheia.", Theme.TextDim, small: true);
    }

    private void DrawEffects(Rectangle area)
    {
        if (Column("effects", area, _draft.Effects, e => $"{e.Name}  ({e.Luck:+0;-0;0} sorte)",
                () => new EffectDef { Id = NewId("efeito", _draft.Effects.Select(e => e.Id)), Name = "Novo efeito" }) is not { } e) return;
        var (x, y, w) = Form(area);
        var k = Sel("effects");
        e.Id = Id($"eff-id-{Sel("effects")}", x, y, w, "Identificador", e.Id); y += 42;
        e.Name = gui.TextField($"eff-name-{k}", Gui.Field(x, y, w), "Nome (aparece no diário)", e.Name); y += 42;
        e.Luck = gui.NumberField($"eff-luck-{k}", Gui.Field(x, y, w), "Sorte enquanto dura", e.Luck, -50, 50); y += 42;
        e.Seconds = gui.NumberField($"eff-sec-{k}", Gui.Field(x, y, w), "Duração (segundos)", e.Seconds, 1, 86400, 10);
    }

    private void DrawRecipes(Rectangle area, IReadOnlyList<ItemDef> items)
    {
        if (Column("recipes", area, _draft.Recipes, r => r.Name,
                () => new Recipe { Id = NewId("receita", _draft.Recipes.Select(r => r.Id)), Name = "Nova receita", Resource = _draft.Resources.FirstOrDefault()?.Id ?? "" }) is not { } r) return;
        var (x, y, w) = Form(area);
        var k = Sel("recipes");
        r.Id = Id($"rec-id-{Sel("recipes")}", x, y, w, "Identificador", r.Id); y += 42;
        r.Name = gui.TextField($"rec-name-{k}", Gui.Field(x, y, w), "Nome", r.Name); y += 42;
        r.Description = gui.TextField($"rec-desc-{k}", Gui.Field(x, y, w), "Descrição", r.Description, 80); y += 42;
        string[] kinds = ["Item (equipamento)", "Suprimento", "Material"];
        var kind = r.Item > 0 ? 0 : r.Supply.Length > 0 ? 1 : 2;
        var newKind = gui.Cycle(Gui.Field(x, y, w), "Produz", kinds, kind); y += 42;
        if (newKind != kind)
        {
            (r.Item, r.Supply, r.Resource) = (0, "", "");
            if (newKind == 0) r.Item = items.FirstOrDefault()?.Id ?? 0;
            if (newKind == 1) r.Supply = _draft.Supplies.FirstOrDefault()?.Id ?? "";
            if (newKind == 2) r.Resource = _draft.Resources.FirstOrDefault()?.Id ?? "";
        }
        if (newKind == 0) r.Item = PickItem(Gui.Field(x, y, w), "Item", items, r.Item);
        else if (newKind == 1) r.Supply = Pick(Gui.Field(x, y, w), "Suprimento", SupplyOptions(false), r.Supply);
        else r.Resource = Pick(Gui.Field(x, y, w), "Material", ResourceOptions(), r.Resource);
        y += 42;
        r.Amount = gui.NumberField($"rec-amount-{k}", Gui.Field(x, y, w), "Quantidade", r.Amount, 1, 99);
        Amounts($"rec-cost-{k}", x + w + 20, area.Y, w, "Custo (materiais)", r.Costs, ResourceOptions(), 1, 999);
    }

    private void DrawShop(Rectangle area, IReadOnlyList<ItemDef> items)
    {
        var shop = _draft.Shop;
        var (x, y, w) = (area.X, area.Y, 300);
        shop.Name = gui.TextField("shop-name", Gui.Field(x, y, w), "Nome do mercador", shop.Name); y += 42;
        shop.Greeting = gui.TextField("shop-greet", Gui.Field(x, y, w), "Saudação", shop.Greeting, 120); y += 42;
        shop.KeepPerResource = gui.NumberField("shop-keep", Gui.Field(x, y, w), "Venda guarda quantos de cada material", shop.KeepPerResource, 0, 999); y += 50;
        gui.Label(new Vector2(x, y), "O preço de venda de cada material fica\nna aba Materiais.", Theme.TextDim, small: true);

        var rest = new Rectangle(x + w + 20, area.Y, area.Right - (x + w + 20), area.Height);
        if (Column("offers", rest, shop.Offers, o => $"{o.Name}  ·  {o.Price} ouro",
                () => new ShopOffer { Supply = _draft.Supplies.FirstOrDefault()?.Id ?? "", Name = _draft.Supplies.FirstOrDefault()?.Name ?? "Oferta", Price = 10 }) is not { } offer) return;
        var (fx, fy, fw) = Form(rest);
        var k = Sel("offers");
        string[] kinds = ["Suprimento", "Item"];
        var kind = offer.Item > 0 ? 1 : 0;
        var newKind = gui.Cycle(Gui.Field(fx, fy, fw), "Vende", kinds, kind); fy += 42;
        if (newKind != kind)
        {
            offer.Item = newKind == 1 ? items.FirstOrDefault()?.Id ?? 0 : 0;
            offer.Supply = newKind == 0 ? _draft.Supplies.FirstOrDefault()?.Id ?? "" : "";
        }
        var before = (offer.Item, offer.Supply);
        if (newKind == 1) offer.Item = PickItem(Gui.Field(fx, fy, fw), "Item", items, offer.Item);
        else offer.Supply = Pick(Gui.Field(fx, fy, fw), "Suprimento", SupplyOptions(false), offer.Supply);
        // A new pick brings its name along; a name typed by hand stays.
        if (before != (offer.Item, offer.Supply))
            offer.Name = newKind == 1 ? items.FirstOrDefault(i => i.Id == offer.Item)?.Name ?? offer.Name : _draft.SupplyName(offer.Supply);
        fy += 42;
        offer.Name = gui.TextField($"offer-name-{k}", Gui.Field(fx, fy, fw), "Nome na loja", offer.Name); fy += 42;
        offer.Price = gui.NumberField($"offer-price-{k}", Gui.Field(fx, fy, fw), "Preço (ouro)", offer.Price, 0, 9_999_999, 5);
    }

    private void DrawObjectives(Rectangle area)
    {
        var head = 92;
        _draft.ObjectivesTitle = gui.TextField("obj-title", Gui.Field(area.X, area.Y, 300), "Título no canto da tela", _draft.ObjectivesTitle);
        _draft.ObjectivesDone = gui.TextField("obj-done", Gui.Field(area.X + 310, area.Y, Math.Min(500, area.Width - 310)), "Quando todos estiverem concluídos", _draft.ObjectivesDone, 120);
        gui.Label(new Vector2(area.X, area.Y + 44), "Cumpridos em ordem, no quadro de avisos do mapa.", Theme.TextDim, small: true);
        var rest = new Rectangle(area.X, area.Y + head - 22, area.Width, area.Height - head + 22);
        if (Column("objectives", rest, _draft.Objectives, o => $"{o.Text}  ({o.Goal})",
                () => new ObjectiveDef { Id = NewId("objetivo", _draft.Objectives.Select(o => o.Id)), Text = "Novo objetivo" }) is not { } o) return;
        var (x, y, w) = Form(rest);
        var k = Sel("objectives");
        o.Id = Id($"obj-id-{Sel("objectives")}", x, y, w, "Identificador", o.Id); y += 42;
        o.Text = gui.TextField($"obj-text-{k}", Gui.Field(x, y, w), "Texto", o.Text, 80); y += 42;
        o.Counter = Pick(Gui.Field(x, y, w), "Conta", CounterOptions(false), o.Counter); y += 42;
        var half = (w - 8) / 2;
        o.Goal = gui.NumberField($"obj-goal-{k}", Gui.Field(x, y, half), "Meta", o.Goal, 1, 999999);
        o.Gold = gui.NumberField($"obj-gold-{k}", Gui.Field(x + half + 8, y, half), "Ouro", o.Gold, 0, 999999, 5); y += 42;
        o.Memory = Pick(Gui.Field(x, y, w), "Memória que concede", MemoryOptions(), o.Memory); y += 42;
        o.Message = gui.TextField($"obj-msg-{k}", Gui.Field(x, y, w), "Mensagem ao concluir", o.Message, 120);
        Amounts($"obj-sup-{k}", x + w + 20, rest.Y, w, "Suprimentos de recompensa", o.Supplies, SupplyOptions(false), 1, 99);
    }

    private void DrawMemories(Rectangle area)
    {
        if (Column("memories", area, _draft.Memories, m => m.Name,
                () => new MemoryDef { Id = NewId("memoria", _draft.Memories.Select(m => m.Id)), Name = "Nova memória" }) is not { } m) return;
        var (x, y, w) = Form(area);
        var k = Sel("memories");
        m.Id = Id($"mem-id-{Sel("memories")}", x, y, w, "Identificador", m.Id); y += 42;
        m.Name = gui.TextField($"mem-name-{k}", Gui.Field(x, y, w), "Nome", m.Name, 60); y += 42;
        m.Counter = Pick(Gui.Field(x, y, w), "Conquistada por", CounterOptions(true), m.Counter); y += 42;
        if (m.Counter.Length > 0) m.Goal = gui.NumberField($"mem-goal-{k}", Gui.Field(x, y, w), "Meta", m.Goal, 1, 999999);
        else gui.Label(new Vector2(x, y + 6), "Só um objetivo concede esta memória.", Theme.TextDim, small: true);
        y += 50;
        gui.Label(new Vector2(x, y), "Memórias sobrevivem à morte e dão um pouco\nde vida e de sorte às próximas vidas.", Theme.TextDim, small: true);
    }

    private void DrawGeneral(Rectangle area, IReadOnlyList<NpcDef> npcs)
    {
        var w = Math.Min(300, (area.Width - 40) / 3);
        var (x, y) = (area.X, area.Y);
        _draft.EmergentAppearance = gui.Toggle(Gui.Field(x, y, w), "Aparência vem da evolução (sem criação)", _draft.EmergentAppearance); y += 42;
        _draft.DeathResetsLife = gui.Toggle(Gui.Field(x, y, w), "A morte recomeça a vida", _draft.DeathResetsLife); y += 42;
        _draft.StartingGold = gui.NumberField("gen-gold", Gui.Field(x, y, w), "Ouro inicial", _draft.StartingGold, 0, 999999, 5); y += 46;
        Amounts("gen-start", x, y, w, "Bolsa inicial", _draft.StartingSupplies, SupplyOptions(false), 1, 99);

        x += w + 20; y = area.Y;
        var luck = _draft.Luck;
        gui.Label(new Vector2(x, y), "Sorte", Theme.Sol); y += 22;
        luck.Spirits = gui.TextField("luck-spirits", Gui.Field(x, y, w), "Nome do ciclo", luck.Spirits); y += 42;
        luck.SpiritPhases = IntList("luck-phases", Gui.Field(x, y, w), "Fases do ciclo (ex.: -3, 2, 5, -1)", luck.SpiritPhases, -50, 50); y += 42;
        luck.PhaseSeconds = gui.NumberField("luck-phase-s", Gui.Field(x, y, w), "Duração de cada fase (s)", luck.PhaseSeconds, 10, 86400, 30); y += 42;
        luck.ShrineEffect = Pick(Gui.Field(x, y, w), "Efeito do santuário", EffectOptions(), luck.ShrineEffect); y += 42;
        var loot = _draft.Loot;
        gui.Label(new Vector2(x, y + 4), "Espólios", Theme.Sol); y += 26;
        loot.SupplyChance = gui.NumberField("loot-chance", Gui.Field(x, y, w), "Chance de suprimento ao vencer (%)", (int)Math.Round(loot.SupplyChance * 100), 0, 100, 5) / 100.0; y += 42;
        loot.Supply = Pick(Gui.Field(x, y, w), "Suprimento comum", SupplyOptions(true), loot.Supply); y += 42;
        loot.RareSupply = Pick(Gui.Field(x, y, w), "Suprimento raro (com sorte)", SupplyOptions(true), loot.RareSupply); y += 42;
        loot.Resource = Pick(Gui.Field(x, y, w), "Material por vitória", ResourceOptions(true), loot.Resource);

        x += w + 20; y = area.Y;
        gui.Label(new Vector2(x, y), "Baús e chefes", Theme.Sol); y += 22;
        var half = (w - 8) / 2;
        loot.ChestGold = gui.NumberField("chest-gold", Gui.Field(x, y, half), "Ouro do baú", loot.ChestGold, 0, 99999);
        loot.ChestGoldPerDepth = gui.NumberField("chest-depth", Gui.Field(x + half + 8, y, half), "+ por andar", loot.ChestGoldPerDepth, 0, 999); y += 42;
        loot.ChestResource = Pick(Gui.Field(x, y, w), "Material do baú", ResourceOptions(true), loot.ChestResource); y += 42;
        loot.ChestCurse = Pick(Gui.Field(x, y, half), "Maldição", EffectOptions(), loot.ChestCurse);
        loot.ChestCurseChance = gui.NumberField("chest-curse", Gui.Field(x + half + 8, y, half), "Chance (%)", (int)Math.Round(loot.ChestCurseChance * 100), 0, 100, 5) / 100.0; y += 42;
        loot.BossMessage = gui.TextField("boss-msg", Gui.Field(x, y, w), "Mensagem ao derrotar um chefe", loot.BossMessage, 120); y += 46;
        var dungeon = _draft.Dungeon;
        gui.Label(new Vector2(x, y), "Masmorra", Theme.Sol); y += 22;
        if (dungeon == null)
        {
            if (gui.Button(new Rectangle(x, y, w, 30), "Criar masmorra")) _draft.Dungeon = new DungeonSettings();
            return;
        }
        dungeon.Name = gui.TextField("dun-name", Gui.Field(x, y, w), "Nome", dungeon.Name); y += 42;
        dungeon.Enemies = IntList("dun-enemies", Gui.Field(x, y, w), "Inimigos (números dos NPCs)", dungeon.Enemies, 1, 999999); y += 42;
        dungeon.Boss = PickNpc(Gui.Field(x, y, half), "Chefe", npcs, dungeon.Boss);
        dungeon.BossEvery = gui.NumberField("dun-every", Gui.Field(x + half + 8, y, half), "A cada (andares)", dungeon.BossEvery, 1, 99); y += 42;
        if (gui.Button(new Rectangle(x, y, w, 28), "Remover masmorra")) _draft.Dungeon = null;
    }

    private void DrawDungeon(Rectangle area)
    {
        if (_draft.Dungeon is not { } dungeon)
        {
            gui.Label(new Vector2(area.X, area.Y), "Crie uma masmorra na seção Geral para definir os cenários.", Theme.TextDim);
            return;
        }
        var w = Math.Min(300, (area.Width - 40) / 3); var half = (w - 8) / 2;
        var (x, y) = (area.X, area.Y);
        dungeon.Tileset = gui.TextField("dun-tileset", Gui.Field(x, y, w), "Tileset", dungeon.Tileset, 120); y += 42;
        dungeon.FloorTile = gui.NumberField("dun-floor", Gui.Field(x, y, half), "Piso", dungeon.FloorTile, 0, 65535);
        dungeon.WallTile = gui.NumberField("dun-wall", Gui.Field(x + half + 8, y, half), "Parede / vazio", dungeon.WallTile, -1, 65535); y += 42;
        dungeon.WallFaceTile = gui.NumberField("dun-face", Gui.Field(x, y, w), "Frente da parede (-1: omitir)", dungeon.WallFaceTile, -1, 65535); y += 42;
        dungeon.FloorVariants = IntList("dun-variants", Gui.Field(x, y, w), "Variações de piso", dungeon.FloorVariants, 0, 65535); y += 42;
        dungeon.ExitTile = gui.NumberField("dun-exit", Gui.Field(x, y, half), "Saída", dungeon.ExitTile, -1, 65535);
        dungeon.DescentTile = gui.NumberField("dun-stairs", Gui.Field(x + half + 8, y, half), "Descida", dungeon.DescentTile, -1, 65535); y += 42;
        dungeon.ChestTile = gui.NumberField("dun-chest", Gui.Field(x, y, half), "Baú", dungeon.ChestTile, -1, 65535);
        dungeon.ExtractionTile = gui.NumberField("dun-crystal", Gui.Field(x + half + 8, y, half), "Extração", dungeon.ExtractionTile, -1, 65535); y += 52;
        gui.Label(new Vector2(x, y), "As mudanças valem para novos andares.\n-1 mantém o piso ou omite a peça.\nO gerador preserva as passagens.", Theme.TextDim, small: true);

        x += w + 20; y = area.Y;
        var styles = dungeon.RoomStyles;
        var selected = gui.List("dun-styles", new Rectangle(x, y, w, 140), styles.Select(s => s.Name).ToList(), Sel("dun-styles"));
        _selected["dun-styles"] = selected; y += 148;
        if (gui.Button(new Rectangle(x, y, half, 28), "+ Estilo", enabled: styles.Count < 16))
        { styles.Add(new DungeonRoomStyle()); _selected["dun-styles"] = styles.Count - 1; }
        if (gui.Button(new Rectangle(x + half + 8, y, half, 28), "Remover", enabled: selected >= 0 && selected < styles.Count))
        { styles.RemoveAt(selected); return; }
        if (selected < 0 || selected >= styles.Count) return;
        var style = styles[selected]; y += 38;
        style.Name = gui.TextField("style-name", Gui.Field(x, y, w), "Nome do estilo", style.Name); y += 42;
        style.MinDepth = gui.NumberField("style-depth", Gui.Field(x, y, w), "Surge a partir do andar", style.MinDepth, 1, 1000000); y += 42;
        style.FloorTile = gui.NumberField("style-floor", Gui.Field(x, y, half), "Piso", style.FloorTile, -1, 65535);
        style.BorderTile = gui.NumberField("style-border", Gui.Field(x + half + 8, y, half), "Borda", style.BorderTile, -1, 65535);

        x += w + 20; y = area.Y;
        var catalog = Path.Combine(DataPaths.Assets, "props", "catalog.json");
        _scenery ??= File.Exists(catalog)
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, MapProp>>(File.ReadAllText(catalog)) ?? [] : [];
        string Name(MapProp p) => _scenery.FirstOrDefault(kv => kv.Value.Sprite == p.Sprite && kv.Value.Source.SequenceEqual(p.Source)).Key ?? p.Sprite;
        var decorations = style.Decorations;
        var part = gui.List("dun-pieces", new Rectangle(x, y, w, 140), decorations.Select(d => Name(d.Prop)).ToList(), Sel("dun-pieces"));
        _selected["dun-pieces"] = part; y += 148;
        MapProp Copy(MapProp p) => new() { Sprite = p.Sprite, Source = [..p.Source], Width = p.Width, Height = p.Height, Underfoot = p.Underfoot, Solid = [..p.Solid] };
        if (gui.Button(new Rectangle(x, y, half, 28), "+ Peça", enabled: decorations.Count < 16 && _scenery.Count > 0))
        { decorations.Add(new DungeonDecoration { Prop = Copy(_scenery.First().Value) }); _selected["dun-pieces"] = decorations.Count - 1; }
        if (gui.Button(new Rectangle(x + half + 8, y, half, 28), "Remover", enabled: part >= 0 && part < decorations.Count))
        { decorations.RemoveAt(part); return; }
        if (part < 0 || part >= decorations.Count) return;
        var piece = decorations[part]; y += 38;
        var current = Name(piece.Prop);
        var picked = Pick(Gui.Field(x, y, w), "Peça do catálogo", _scenery.Keys.Select(k => (k, k)).ToList(), current); y += 42;
        if (picked != current && _scenery.TryGetValue(picked, out var prop)) piece.Prop = Copy(prop);
        piece.Anchor = (RoomAnchor)gui.Cycle(Gui.Field(x, y, w), "Posição na sala", ["Noroeste", "Nordeste", "Sudoeste", "Sudeste", "Centro"], (int)piece.Anchor); y += 42;
        piece.OffsetX = gui.NumberField("piece-x", Gui.Field(x, y, half), "Deslocamento X", piece.OffsetX, -32, 32);
        piece.OffsetY = gui.NumberField("piece-y", Gui.Field(x + half + 8, y, half), "Deslocamento Y", piece.OffsetY, -32, 32);
    }

    private void DrawTexts(Rectangle area)
    {
        gui.Label(new Vector2(area.X, area.Y), "Vazio usa a frase padrão da engine. {0} é trocado pelo valor indicado.", Theme.TextDim, small: true);
        var rows = TextKeys.Select(t => (_draft.Texts.GetValueOrDefault(t.key, "").Length > 0 ? "● " : "○ ") + t.what).ToList();
        var list = new Rectangle(area.X, area.Y + 24, 300, area.Height - 24);
        var selected = gui.List("texts", list, rows, Sel("texts"));
        _selected["texts"] = Math.Clamp(selected, 0, TextKeys.Length - 1);
        var (key, what) = TextKeys[_selected["texts"]];
        var x = list.Right + 20;
        gui.Label(new Vector2(x, area.Y + 24), what, Theme.Sol);
        var value = gui.TextField($"text-{key}", Gui.Field(x, area.Y + 50, Math.Min(640, area.Right - x)), key, _draft.Texts.GetValueOrDefault(key, ""), 200);
        if (value.Length > 0) _draft.Texts[key] = value;
        else _draft.Texts.Remove(key);
    }

    // ---- Evolution ----

    public void DrawEvolution(Rectangle body)
    {
        gui.Label(new Vector2(body.X, body.Y), "Formas surgem dos hábitos: a regra vale quando todos os mínimos são atingidos; entre as válidas, vence a de maior peso.", Theme.TextDim, small: true);
        var area = new Rectangle(body.X, body.Y + 24, body.Width, body.Height - 24);
        if (Column("forms", area, _evolution.Rules, r => $"{r.Name}  ({(r.Kind == "bloodline" ? "origem" : "especialização")})",
                () => new EvolutionRule { Id = NewId("forma", _evolution.Rules.Select(r => r.Id)), Name = "Nova forma", Requires = new() { ["melee"] = 5 }, Weights = new() { ["melee"] = 1 } }) is not { } r) return;
        var (x, y, w) = Form(area);
        var k = Sel("forms");
        var half = (w - 8) / 2;
        r.Id = Id($"form-id-{Sel("forms")}", x, y, w, "Identificador", r.Id); y += 42;
        r.Name = gui.TextField($"form-name-{k}", Gui.Field(x, y, w), "Nome", r.Name); y += 42;
        string[] kinds = ["calling", "bloodline"];
        r.Kind = kinds[gui.Cycle(Gui.Field(x, y, w), "Tipo", ["Especialização", "Origem"], r.Kind == "bloodline" ? 1 : 0)]; y += 42;
        r.Trace = gui.TextField($"form-trace-{k}", Gui.Field(x, y, w), "Frase ao surgir", r.Trace, 120); y += 42;
        r.Tint = ColorField($"form-tint-{k}", x, y, half, "Cor", r.Tint);
        r.Accent = ColorField($"form-accent-{k}", x + half + 8, y, half, "Detalhe", r.Accent); y += 42;
        r.Silhouette = Pick(Gui.Field(x, y, w), "Silhueta", SilhouetteOptions(r.Silhouette), r.Silhouette); y += 42;
        r.Attack = gui.NumberField($"form-atk-{k}", Gui.Field(x, y, half), "Ataque", r.Attack, -99, 999);
        r.Defense = gui.NumberField($"form-def-{k}", Gui.Field(x + half + 8, y, half), "Defesa", r.Defense, -99, 999); y += 42;
        r.Health = gui.NumberField($"form-hp-{k}", Gui.Field(x, y, half), "Vida", r.Health, -99, 9999, 2);
        r.Mana = gui.NumberField($"form-mp-{k}", Gui.Field(x + half + 8, y, half), "Essência", r.Mana, -99, 9999, 2); y += 42;
        r.SkillCost = gui.NumberField($"form-cost-{k}", Gui.Field(x, y, half), "Custo do pulso", r.SkillCost, 0, 999);
        r.SkillCooldownMs = gui.NumberField($"form-cd-{k}", Gui.Field(x + half + 8, y, half), "Recarga (ms)", r.SkillCooldownMs, 100, 60000, 100);

        var habits = HabitOptions(r.Requires.Keys.Concat(r.Weights.Keys));
        var x2 = x + w + 20;
        var y2 = Amounts($"form-req-{k}", x2, area.Y, w, "Exige (mínimo de cada hábito)", r.Requires, habits, 1, 99999);
        var weights = r.Weights.ToDictionary(p => p.Key, p => (int)Math.Round(p.Value));
        Amounts($"form-w-{k}", x2, y2, w, "Peso de cada hábito na disputa", weights, habits, 0, 999);
        if (weights.Count != r.Weights.Count || weights.Any(p => r.Weights.GetValueOrDefault(p.Key, -1) != p.Value))
            r.Weights = weights.ToDictionary(p => p.Key, p => (double)p.Value);
    }

    // ---- Widgets ----

    /// <summary>
    /// A list on the left with New, Duplicate and Delete under it. Returns the selected entry, or null when
    /// the list is empty; the form goes to the right of the column.
    /// </summary>
    private T? Column<T>(string id, Rectangle area, List<T> list, Func<T, string> label, Func<T> create, bool canAdd = true, bool canDuplicate = true) where T : class
    {
        var col = new Rectangle(area.X, area.Y, 260, area.Height);
        var selected = Math.Clamp(Sel(id), 0, Math.Max(0, list.Count - 1));
        selected = gui.List(id, new Rectangle(col.X, col.Y, col.Width, col.Height - 42), list.Select(label).ToList(), selected);
        var buttons = col.Bottom - 34;
        var bw = (col.Width - 8) / 3;
        if (gui.Button(new Rectangle(col.X, buttons, bw, 30), "Novo", enabled: canAdd))
        {
            list.Add(create());
            selected = list.Count - 1;
            gui.Reveal(id, selected, (col.Height - 46) / 24);
        }
        if (gui.Button(new Rectangle(col.X + bw + 4, buttons, bw, 30), "Duplicar", enabled: canDuplicate && list.Count > 0))
        {
            // A JSON round trip is a deep copy of any of these records; the copy needs its own id.
            var copy = System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(list[selected], AdventureContent.JsonOptions), AdventureContent.JsonOptions)!;
            if (copy.GetType().GetProperty("Id") is { PropertyType.Name: "String" } prop)
                prop.SetValue(copy, NewId((string)prop.GetValue(copy)!, list.Select(e => (string)prop.GetValue(e)!)));
            list.Insert(selected + 1, copy);
            selected++;
        }
        if (gui.Button(new Rectangle(col.X + (bw + 4) * 2, buttons, bw, 30), "Excluir", enabled: list.Count > 0))
        {
            list.RemoveAt(selected);
            selected = Math.Max(0, selected - 1);
        }
        _selected[id] = selected;
        return list.Count > 0 ? list[Math.Clamp(selected, 0, list.Count - 1)] : null;
    }

    private static (int x, int y, int w) Form(Rectangle area) => (area.X + 280, area.Y, Math.Min(320, (area.Width - 300) / 2));

    private int Sel(string id) => _selected.GetValueOrDefault(id);

    /// <summary>An identifier: letters, digits, - and _, no spaces.</summary>
    private string Id(string id, int x, int y, int w, string label, string value)
    {
        var typed = gui.TextField(id, Gui.Field(x, y, w), label, value, 40);
        return new string(typed.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
    }

    private string ColorField(string id, int x, int y, int w, string label, string value)
    {
        var typed = gui.TextField(id, Gui.Field(x, y, w - 30), label, value, 7);
        if (typed.Length == 7 && typed[0] == '#' && uint.TryParse(typed[1..], System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            gui.Fill(new Rectangle(x + w - 26, y + 4, 24, 26), new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        return typed;
    }

    /// <summary>One id out of options; a value not among them stays selectable so nothing is lost by looking.</summary>
    private string Pick(Rectangle r, string label, List<(string id, string name)> options, string value)
    {
        if (options.All(o => o.id != value)) options.Add((value, value + " (não existe)"));
        var index = options.FindIndex(o => o.id == value);
        return options[gui.Cycle(r, label, options.Select(o => o.name).ToArray(), index)].id;
    }

    private int PickItem(Rectangle r, string label, IReadOnlyList<ItemDef> items, int value) =>
        int.Parse(Pick(r, label, items.Select(i => (i.Id.ToString(), $"{i.Id} · {i.Name}")).ToList(), value.ToString()));

    private int PickNpc(Rectangle r, string label, IReadOnlyList<NpcDef> npcs, int value) =>
        int.Parse(Pick(r, label, npcs.Select(n => (n.Id.ToString(), $"{n.Id} · {n.Name}")).Prepend(("0", "Nenhum")).ToList(), value.ToString()));

    /// <summary>Rows of (choice, amount) with a remove button each and one to add; returns the y below it.</summary>
    private int Amounts(string id, int x, int y, int w, string label, Dictionary<string, int> values, List<(string id, string name)> options, int min, int max)
    {
        gui.Label(new Vector2(x, y), label, Theme.TextDim, small: true);
        y += 20;
        foreach (var key in values.Keys.ToList())
        {
            var picked = Pick(Gui.Field(x, y, w - 150), "", [.. options], key);
            var amount = gui.NumberField($"{id}-{key}", Gui.Field(x + w - 144, y, 112), "", values[key], min, max);
            if (gui.Button(new Rectangle(x + w - 28, y + 4, 26, 26), "×"))
            {
                values.Remove(key);
                continue;
            }
            if (picked != key && !values.ContainsKey(picked))
            {
                values.Remove(key);
                values[picked] = amount;
            }
            else values[key] = amount;
            y += 40;
        }
        var free = options.FirstOrDefault(o => !values.ContainsKey(o.id));
        if (gui.Button(new Rectangle(x, y, 130, 28), "+ adicionar", enabled: free.id != null)) values[free.id!] = Math.Max(min, 1);
        return y + 40;
    }

    /// <summary>Whole numbers typed as a comma-separated list; what is being typed survives until it parses.</summary>
    private int[] IntList(string id, Rectangle r, string label, int[] values, int min, int max)
    {
        var shown = gui.Focus == id && _textDrafts.TryGetValue(id, out var draft) ? draft : string.Join(", ", values);
        var typed = gui.TextField(id, r, label, shown, 120);
        _textDrafts[id] = typed;
        var parts = typed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = parts.Select(p => int.TryParse(p, out var n) ? n : (int?)null).ToList();
        return parsed.All(n => n != null) ? parsed.Select(n => Math.Clamp(n!.Value, min, max)).ToArray() : values;
    }

    private List<(string, string)> ResourceOptions(bool allowNone = false)
    {
        var list = _draft.Resources.Select(r => (r.Id, r.Name)).ToList();
        if (allowNone) list.Insert(0, ("", "Nenhum"));
        return list;
    }

    private List<(string, string)> SupplyOptions(bool allowNone)
    {
        var list = _draft.Supplies.Select(s => (s.Id, s.Name)).ToList();
        if (allowNone) list.Insert(0, ("", "Nenhum"));
        return list;
    }

    private List<(string, string)> EffectOptions() => _draft.Effects.Select(e => (e.Id, e.Name)).Prepend(("", "Nenhum")).ToList();

    private List<(string, string)> MemoryOptions() => _draft.Memories.Select(m => (m.Id, m.Name)).Prepend(("", "Nenhuma")).ToList();

    private static List<(string, string)> CounterOptions(bool allowNone)
    {
        var list = Journey.Counters.Select(c => (c, CounterNames.GetValueOrDefault(c, c))).ToList();
        if (allowNone) list.Insert(0, ("", "Só por objetivo"));
        return list;
    }

    private List<(string, string)> HabitOptions(IEnumerable<string> extra) =>
        EngineHabits.Concat(_draft.Activities.Select(a => a.Habit)).Concat(_draft.Supplies.Select(s => s.Habit)).Concat(extra)
            .Where(h => h.Length > 0).Distinct().Order().Select(h => (h, h)).ToList();

    /// <summary>Silhouettes are the sheets the art set's forms.json names.</summary>
    private static List<(string, string)> SilhouetteOptions(string current)
    {
        var names = new List<string>();
        var path = Path.Combine(CharacterArt.Folder, "forms.json");
        try
        {
            if (File.Exists(path))
                names.AddRange(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))?.Keys ?? Enumerable.Empty<string>());
        }
        catch (System.Text.Json.JsonException) { }
        if (!names.Contains(current)) names.Add(current);
        return names.Select(n => (n, n)).ToList();
    }

    private static string KindName(string id) => GatherKinds.FirstOrDefault(k => k.id == id).name ?? id;

    private static string NewId(string stem, IEnumerable<string> taken)
    {
        var set = taken.ToHashSet();
        if (!set.Contains(stem)) return stem;
        for (var i = 2; ; i++)
            if (!set.Contains($"{stem}-{i}")) return $"{stem}-{i}";
    }
}

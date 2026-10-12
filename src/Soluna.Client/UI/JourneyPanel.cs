using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;
using Soluna.Shared;

namespace Soluna.Client.UI;

internal sealed record AdventureRequest(AdventureAction Action, string? Arg = null);
internal sealed class JourneyPanel
{
    public bool Open { get; set; }
    public int Tab { get; set; }
    public Journey State { get; set; } = new();
    public AdventureContent Content { get; set; } = new();
    private readonly List<(Rectangle rect, AdventureRequest request, bool enabled)> _buttons = [];
    public Rectangle Bounds(Point screen) => new(screen.X - Math.Min(554, screen.X - 24) - 12, 62, Math.Min(554, screen.X - 24), Math.Min(630, screen.Y - 76));
    public AdventureRequest? Update(Input input, Point screen)
    {
        if (!Open || !input.LeftPressed) return null;
        var b = Bounds(screen); var m = input.Mouse.ToPoint();
        for (var i = 0; i < 4; i++)
            if (new Rectangle(b.X + 14 + i * (b.Width - 28) / 4, b.Y + 50, (b.Width - 28) / 4, 32).Contains(m)) { Tab = i; return null; }
        foreach (var button in _buttons) if (button.enabled && button.rect.Contains(m)) return button.request;
        return null;
    }
    public void Draw(SpriteBatch batch, Texture2D pixel, Fonts fonts, Point screen, bool nearBench, bool nearMerchant)
    {
        if (!Open) return;
        _buttons.Clear();
        var b = Bounds(screen); var x = b.X + 20; var y = b.Y + 102;
        Ui.Panel(batch, pixel, b, new Color(17, 25, 32) * .98f);
        Ui.Text(batch, fonts.Title, Content.Text("journalTitle", "DIÁRIO"), new Vector2(x, b.Y + 15), Theme.Sol);
        Ui.Text(batch, fonts.Small, "J / Esc · fechar", new Vector2(b.Right - 115, b.Y + 21), Theme.TextDim);
        string[] tabs = ["Esta vida", "Ofícios", "Trocas", "Memórias"];
        for (var i = 0; i < tabs.Length; i++)
        {
            var rect = new Rectangle(b.X + 14 + i * (b.Width - 28) / 4, b.Y + 50, (b.Width - 28) / 4 - 4, 32);
            batch.Draw(pixel, rect, i == Tab ? Theme.Luna * .2f : Theme.PanelRaised);
            if (i == Tab) batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), Theme.Luna);
            Ui.Text(batch, fonts.Small, tabs[i], new Vector2(rect.X + 12, rect.Y + 8), i == Tab ? Theme.Text : Theme.TextDim);
        }
        switch (Tab)
        {
            case 0:
                Text(State.CallingName + "  /  " + State.BloodlineName, Theme.Luna, true);
                Text(State.Trace, Theme.TextDim);
                y += 8;
                Text($"Vida {State.Legacy.Lives}   ·   {State.Gold} ouro   ·   Profundidade {State.DeepestFloor}", Theme.Sol);
                Text($"Sorte {State.Luck:+0;-0;0}  ·  as influências mudam com o mundo", Theme.Text);
                foreach (var source in State.LuckSources)
                    Text($"{source.Name}  {source.Value:+0;-0;0}", Theme.TextDim);
                y += 8;
                var supplies = Content.Supplies;
                for (var i = 0; i < supplies.Count; i++)
                {
                    var s = supplies[i];
                    Button(new Rectangle(x + i % 2 * 252, y + i / 2 * 32, 242, 27), $"{s.Name}  ×{State.Count(s.Id)}", new(AdventureAction.Use, s.Id), State.Count(s.Id) > 0);
                }
                y += (supplies.Count + 1) / 2 * 32 + 14;
                Text("Materiais", Theme.Sol);
                for (var i = 0; i < Content.Resources.Count; i++)
                {
                    var r = Content.Resources[i];
                    Ui.Text(batch, fonts.Small, $"{r.Name}: {State.Resources.GetValueOrDefault(r.Id)}", new Vector2(x + i % 2 * 252, y + i / 2 * 19), Theme.TextDim);
                }
                break;
            case 1:
                Text(nearBench ? "A bancada está pronta." : "Aproxime-se de uma bancada para fabricar.", nearBench ? Theme.System : Theme.TextDim);
                y += 5;
                foreach (var r in Content.Recipes)
                {
                    if (y + 46 > b.Bottom - 8) break;
                    var cost = string.Join(" · ", r.Costs.Select(c => $"{Content.ResourceName(c.Key)} {State.Resources.GetValueOrDefault(c.Key)}/{c.Value}"));
                    Button(new Rectangle(x, y, b.Width - 40, 43), r.Name + "  ·  " + r.Description, new(AdventureAction.Craft, r.Id), nearBench && Content.CanCraft(State, r));
                    Ui.Text(batch, fonts.Small, cost, new Vector2(x + 10, y + 23), Theme.TextDim);
                    y += 49;
                }
                break;
            case 2:
                var shop = Content.Shop;
                Text(shop.Name, Theme.Sol, true);
                Text(nearMerchant ? shop.Greeting : Content.Text("shopFar", "Encontre o mercador para negociar."), Theme.TextDim);
                Text($"Sua bolsa: {State.Gold} ouro", Theme.Text);
                y += 12;
                for (var i = 0; i < shop.Offers.Count && y + 48 < b.Bottom - 60; i++)
                {
                    var offer = shop.Offers[i];
                    Button(new Rectangle(x, y, b.Width - 40, 38), $"{offer.Name}  ·  {offer.Price} ouro", new(AdventureAction.Buy, i.ToString()), nearMerchant && State.Gold >= offer.Price);
                    y += 48;
                }
                Button(new Rectangle(x, y, b.Width - 40, 38), "Vender excedentes de materiais", new(AdventureAction.SellMaterials), nearMerchant); y += 52;
                Text($"Mantém {shop.KeepPerResource} unidades de cada material na bolsa.", Theme.TextDim);
                break;
            case 3:
                Text("O que atravessou a noite", Theme.Luna, true);
                Text($"{State.Legacy.Lives} vidas  ·  {State.Legacy.Kills} vitórias  ·  {State.Legacy.Catches} pescas", Theme.TextDim);
                Text("A morte desfaz nível, forma, posses e hábitos desta vida.", Theme.TextDim);
                Text("Memórias da conta concedem uma pequena resistência.", Theme.TextDim);
                y += 18;
                foreach (var memory in State.Legacy.Memories.Order()) Text("✦  " + Content.MemoryName(memory), Theme.Sol);
                if (State.Legacy.Memories.Count == 0) Text("Ainda não há ecos de outra vida.", Theme.TextDim);
                y += 20;
                Text("A forma acompanha seus hábitos. Ela não limita suas ações.", Theme.Text);
                break;
        }
        return;
        void Text(string text, Color color, bool large = false)
        {
            Ui.Text(batch, large ? fonts.Title : fonts.Small, text, new Vector2(x, y), color);
            y += large ? 30 : 21;
        }
        void Button(Rectangle rect, string label, AdventureRequest request, bool enabled)
        {
            batch.Draw(pixel, rect, enabled ? Theme.PanelRaised : Theme.Background);
            Ui.Outline(batch, pixel, rect, enabled ? Theme.Border : Theme.Border * .4f);
            Ui.Text(batch, fonts.Small, label, new Vector2(rect.X + 10, rect.Y + 6), enabled ? Theme.Text : Theme.TextDim * .6f);
            _buttons.Add((rect, request, enabled));
        }
    }
}

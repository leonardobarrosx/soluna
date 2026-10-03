using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;
using Soluna.Shared;

namespace Soluna.Client.UI;

/// <summary>
/// Shown when an admin drops a PNG on the window: the image, what it will be (tileset or character
/// sprite, guessed from its size), its name, and whether its license lets the game share it publicly.
/// Importing sends it to the server, which saves it in the game and hands it to every player.
/// </summary>
internal sealed class ImportDialog(Gui gui, Textures textures)
{
    private static readonly AssetKind[] Kinds = [AssetKind.Tileset, AssetKind.Sprite];
    private static readonly string[] KindNames = ["Tileset (mapas)", "Sprite de personagem (NPCs)"];

    private byte[]? _data;
    private Texture2D? _preview;
    private (int w, int h) _size;
    private string _file = "";
    private string _name = "";
    private int _kind;
    private bool _shareable;
    private string? _error;

    /// <summary>Kind, name, shareable and bytes of a file to upload.</summary>
    public Action<AssetKind, string, bool, byte[]>? Upload { get; init; }

    public bool Open => _data != null || _error != null;

    public void Show(string path)
    {
        Close();
        _file = Path.GetFileName(path);
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length > AssetFiles.MaxUploadBytes) _error = $"O arquivo passa de {AssetFiles.MaxUploadBytes / 1024 / 1024} MB.";
            else if (AssetFiles.PngSize(data) is not { } size) _error = "Só dá para importar imagens PNG.";
            else
            {
                _data = data;
                _size = size;
                _preview = textures.FromBytes(data);
                // Small 3:4 images are character sheets; anything else on the 32 grid is a tileset.
                _kind = size.w * 4 == size.h * 3 && size.w <= 288 ? 1 : 0;
                _name = Clean(Path.GetFileNameWithoutExtension(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _error = $"Não foi possível abrir o arquivo: {ex.Message}";
        }
    }

    public void Close()
    {
        _preview?.Dispose();
        _preview = null;
        _data = null;
        _error = null;
    }

    public void Draw(Point screen)
    {
        var panel = new Rectangle(screen.X / 2 - 360, screen.Y / 2 - 250, 720, 500);
        gui.Fill(new Rectangle(0, 0, screen.X, screen.Y), Color.Black * 0.55f);
        gui.Panel(panel, Theme.Background);
        gui.Title(new Vector2(panel.X + 20, panel.Y + 14), "Importar arquivo");
        gui.Label(new Vector2(panel.X + 20, panel.Y + 52), _file, Theme.TextDim, small: true);

        if (_data == null)
        {
            gui.Label(new Vector2(panel.X + 20, panel.Y + 90), _error ?? "", Theme.Danger);
            if (gui.Button(new Rectangle(panel.Right - 140, panel.Bottom - 52, 120, 32), "Fechar")) Close();
            return;
        }

        // The image on a dark box, so transparent pixels read as background.
        var box = new Rectangle(panel.X + 20, panel.Y + 80, 300, 340);
        gui.Fill(box, Theme.Panel);
        // Shrink big images to fit; blow small ones up by whole steps so pixel art stays crisp.
        var fit = Math.Min((float)box.Width / _size.w, (float)box.Height / _size.h);
        var scale = fit < 1 ? fit : MathF.Floor(fit);
        var shown = new Point((int)(_size.w * scale), (int)(_size.h * scale));
        gui.Image(_preview!, new Rectangle(0, 0, _size.w, _size.h), new Rectangle(box.Center.X - shown.X / 2, box.Center.Y - shown.Y / 2, shown.X, shown.Y));
        gui.Label(new Vector2(box.X, box.Bottom + 6), $"{_size.w} x {_size.h}", Theme.TextDim, small: true);

        var x = box.Right + 24;
        var width = panel.Right - 20 - x;
        var y = box.Y;
        _kind = gui.Cycle(Gui.Field(x, y, width), "O que é", KindNames, _kind);
        y += 46;
        _name = Clean(gui.TextField("import-name", Gui.Field(x, y, width), "Nome do arquivo", _name));
        y += 46;
        _shareable = gui.Toggle(Gui.Field(x, y, width), "A licença permite redistribuir publicamente", _shareable);
        y += 44;
        gui.Label(new Vector2(x, y), _shareable
            ? "Vai para a pasta pública: entra no repositório do jogo."
            : "Vai para uma pasta private: os jogadores recebem, mas fica\nfora do repositório (o .gitignore cuida disso).", Theme.TextDim, small: true);
        y += 48;

        var kind = Kinds[_kind];
        var problem = AssetFiles.Check(kind, _size) ?? (_name.Length == 0 ? "Dê um nome ao arquivo." : null);
        gui.Label(new Vector2(x, y), problem ?? $"Destino: {AssetFiles.Destination(kind, _name, _shareable)}", problem != null ? Theme.Danger : Theme.TextDim, small: true);
        if (problem == null && kind == AssetKind.Sprite)
            gui.Label(new Vector2(x, y + 22), "Aparece na lista de sprites do editor de NPCs.", Theme.TextDim, small: true);
        else if (problem == null)
            gui.Label(new Vector2(x, y + 22), "Aparece na paleta do editor de mapas (F1).", Theme.TextDim, small: true);

        if (gui.Button(new Rectangle(panel.Right - 290, panel.Bottom - 52, 120, 32), "Cancelar")) Close();
        if (gui.Button(new Rectangle(panel.Right - 160, panel.Bottom - 52, 140, 32), "Importar", primary: true, enabled: problem == null))
        {
            Upload?.Invoke(kind, _name, _shareable, _data);
            Close();
        }
    }

    /// <summary>Letters, digits, - and _, as the server accepts; spaces become _.</summary>
    private static string Clean(string name) =>
        new string(name.Replace(' ', '_').Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
}

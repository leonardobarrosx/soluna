using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Shared;
using T = Soluna.Shared.PlaceholderTiles;

namespace Soluna.Client.Graphics;

/// <summary>
/// Paints the built-in tileset and character sheets, so the engine runs with no art files.
/// Swap them for real art by dropping PNGs into assets/ (see assets/README.md).
/// </summary>
internal static class PlaceholderArt
{
    private const int S = Constants.TileSize;

    public static Texture2D Tileset(GraphicsDevice device)
    {
        var rows = (T.Count + T.Columns - 1) / T.Columns;
        var canvas = new Canvas(T.Columns * S, rows * S);
        var rng = new Random(1);

        for (var index = 0; index < T.Count; index++)
        {
            var ox = index % T.Columns * S;
            var oy = index / T.Columns * S;
            canvas.Origin = new Point(ox, oy);
            canvas.Clip = new Rectangle(ox, oy, S, S);
            PaintTile(canvas, index, rng);
        }
        return canvas.ToTexture(device);
    }

    /// <summary>Shown where a map points at a tileset this client does not have.</summary>
    public static Texture2D Missing(GraphicsDevice device)
    {
        var canvas = new Canvas(S, S);
        for (var y = 0; y < S; y++)
        for (var x = 0; x < S; x++)
            canvas.Set(x, y, (x / 8 + y / 8) % 2 == 0 ? new Color(60, 30, 60) : new Color(30, 20, 36));
        return canvas.ToTexture(device);
    }

    private static void PaintTile(Canvas c, int index, Random rng)
    {
        switch (index)
        {
            case T.Grass:
                c.Fill(0, 0, S, S, new Color(44, 72, 56));
                c.Speckle(0, 0, S, S, rng, 0.18f, new Color(54, 86, 64), new Color(36, 60, 48));
                Blades(c, rng, new Color(62, 98, 72), 6);
                break;

            case T.GrassDark:
                c.Fill(0, 0, S, S, new Color(42, 69, 54));
                c.Speckle(0, 0, S, S, rng, 0.24f, new Color(50, 80, 62), new Color(34, 56, 45));
                Blades(c, rng, new Color(52, 84, 64), 9);
                break;

            case T.Path:
                c.Fill(0, 0, S, S, new Color(86, 74, 62));
                c.Speckle(0, 0, S, S, rng, 0.22f, new Color(98, 86, 72), new Color(72, 62, 52));
                for (var i = 0; i < 4; i++) c.Ellipse(rng.Next(3, 29), rng.Next(3, 29), 1.8f, 1.2f, new Color(110, 100, 90));
                break;

            case T.Water:
                c.Fill(0, 0, S, S, new Color(26, 46, 78));
                c.Speckle(0, 0, S, S, rng, 0.12f, new Color(30, 54, 90));
                for (var y = 4; y < S; y += 9)
                {
                    var x0 = rng.Next(0, 12);
                    c.Fill(x0, y, 10, 1, new Color(60, 92, 132));
                    c.Fill(x0 + 14, y + 4, 7, 1, new Color(48, 78, 118));
                }
                break;

            case T.StoneWall:
                c.Fill(0, 0, S, S, new Color(30, 30, 38));
                for (var row = 0; row < 4; row++)
                {
                    var offset = row % 2 == 0 ? 0 : -8;
                    for (var x = offset; x < S; x += 16)
                    {
                        c.Fill(x + 1, row * 8 + 1, 14, 6, new Color(66, 66, 80));
                        c.Fill(x + 1, row * 8 + 1, 14, 1, new Color(84, 84, 100));
                        c.Fill(x + 1, row * 8 + 6, 14, 1, new Color(52, 52, 64));
                    }
                }
                break;

            case T.TreeTrunk:
                c.Ellipse(16, 29, 11, 3, new Color(0, 0, 0, 90));
                c.Fill(12, 6, 8, 22, new Color(66, 46, 36));
                c.Fill(12, 6, 2, 22, new Color(84, 60, 44));
                c.Fill(18, 6, 2, 22, new Color(50, 34, 28));
                Canopy(c, -S);
                break;

            case T.TreeCanopy:
                Canopy(c, 0);
                break;

            case T.Flowers:
                for (var i = 0; i < 5; i++)
                {
                    var x = rng.Next(3, 28);
                    var y = rng.Next(3, 28);
                    var petal = i % 2 == 0 ? new Color(186, 168, 236) : new Color(232, 192, 112);
                    c.Fill(x - 1, y, 3, 1, petal);
                    c.Fill(x, y - 1, 1, 3, petal);
                    c.Set(x, y, new Color(250, 240, 210));
                }
                break;

            case T.Rock:
                c.Ellipse(16, 26, 12, 4, new Color(0, 0, 0, 90));
                c.Ellipse(16, 19, 12, 9, new Color(78, 80, 92));
                c.Ellipse(14, 16, 8, 5, new Color(98, 100, 114));
                c.Ellipse(19, 23, 7, 3, new Color(62, 64, 74));
                break;

            case T.WoodFloor:
                c.Fill(0, 0, S, S, new Color(82, 60, 46));
                for (var y = 0; y < S; y += 8)
                {
                    c.Fill(0, y, S, 1, new Color(58, 42, 34));
                    c.Fill(rng.Next(4, 28), y + 1, 1, 7, new Color(64, 46, 36));
                }
                c.Speckle(0, 0, S, S, rng, 0.06f, new Color(92, 68, 52));
                break;
        }
    }

    private static void Blades(Canvas c, Random rng, Color color, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var x = rng.Next(1, 31);
            var y = rng.Next(2, 31);
            c.Set(x, y, color);
            c.Set(x, y - 1, color);
        }
    }

    /// <summary>
    /// The canopy is one blob that spans the canopy tile and the top of the trunk tile.
    /// <paramref name="shift"/> moves it so each tile paints its own slice.
    /// </summary>
    private static void Canopy(Canvas c, int shift)
    {
        var cy = 22 + shift;
        c.Circle(16, cy, 16, new Color(26, 54, 40));
        c.Circle(15, cy - 2, 14, new Color(34, 68, 48));
        c.Circle(11, cy - 6, 7, new Color(46, 86, 60));
        c.Circle(21, cy - 1, 5, new Color(40, 78, 54));
        c.Circle(9, cy - 8, 2, new Color(70, 116, 84));
    }

    private static readonly (Color tunic, Color hair)[] Palettes =
    [
        (new Color(92, 76, 168), new Color(40, 30, 28)),
        (new Color(176, 120, 52), new Color(214, 196, 150)),
        (new Color(52, 120, 110), new Color(90, 52, 36)),
        (new Color(150, 56, 72), new Color(26, 24, 30)),
        (new Color(70, 92, 140), new Color(180, 90, 50)),
        (new Color(110, 110, 124), new Color(200, 200, 214)),
    ];

    public static int CharacterCount => Palettes.Length;

    /// <summary>
    /// A 3x4 sheet in RPG Maker order: columns are step-left, idle, step-right;
    /// rows are down, left, right, up. Real character sheets in the same layout drop in.
    /// </summary>
    public static Texture2D Character(GraphicsDevice device, int variant)
    {
        var (tunic, hair) = Palettes[variant % Palettes.Length];
        var skin = new Color(226, 186, 150);
        var dark = new Color(28, 26, 36);
        var canvas = new Canvas(3 * S, 4 * S);

        for (var row = 0; row < 4; row++)
        for (var col = 0; col < 3; col++)
        {
            var dir = (Direction)row;
            canvas.Origin = new Point(col * S, row * S);
            canvas.Clip = new Rectangle(col * S, row * S, S, S);

            var step = col == 1 ? 0 : col == 0 ? -1 : 1;
            var bob = col == 1 ? 0 : 1;

            canvas.Ellipse(16, 29, 8, 2.5f, new Color(0, 0, 0, 100));

            // Legs swap on each step.
            canvas.Fill(12, 23 + (step < 0 ? -1 : 0), 3, 6, dark);
            canvas.Fill(17, 23 + (step > 0 ? -1 : 0), 3, 6, dark);

            canvas.Ellipse(16, 19 + bob, 7, 6, tunic);
            canvas.Fill(9, 20 + bob, 14, 4, tunic);
            canvas.Fill(9, 23 + bob, 14, 1, new Color(tunic.R / 2, tunic.G / 2, tunic.B / 2));

            canvas.Circle(16, 10 + bob, 7, skin);
            switch (dir)
            {
                case Direction.Down:
                    canvas.Ellipse(16, 6 + bob, 8, 4, hair);
                    canvas.Fill(13, 10 + bob, 2, 2, dark);
                    canvas.Fill(18, 10 + bob, 2, 2, dark);
                    break;
                case Direction.Up:
                    canvas.Circle(16, 9 + bob, 7.5f, hair);
                    break;
                case Direction.Left:
                    canvas.Ellipse(18, 7 + bob, 7, 5, hair);
                    canvas.Fill(12, 10 + bob, 2, 2, dark);
                    break;
                case Direction.Right:
                    canvas.Ellipse(14, 7 + bob, 7, 5, hair);
                    canvas.Fill(18, 10 + bob, 2, 2, dark);
                    break;
            }
        }
        return canvas.ToTexture(device);
    }

    /// <summary>Radial mask: clear in the middle, opaque at the edge. Drawn tinted as the night overlay.</summary>
    public static Texture2D LightMask(GraphicsDevice device, float inner, float outer)
    {
        const int size = 512;
        var data = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = (x + 0.5f) / size * 2 - 1;
            var dy = (y + 0.5f) / size * 2 - 1;
            var d = MathF.Sqrt(dx * dx + dy * dy);
            var t = Math.Clamp((d - inner) / (outer - inner), 0, 1);
            var a = t * t * (3 - 2 * t);
            data[y * size + x] = new Color(1f, 1f, 1f) * a;
        }
        var texture = new Texture2D(device, size, size);
        texture.SetData(data);
        return texture;
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

/// <summary>Imports art pages into engine-sized texture cells without rewriting their source files.
/// Integer cell boundaries and nearest sampling prevent bleeding between poses and tiles.</summary>
internal static class AtlasTextures
{
    public static Texture2D Load(GraphicsDevice device, string path)
    {
        using var stream = File.OpenRead(path);
        var original = Texture2D.FromStream(device, stream);
        var layout = TextureLayout.Read(path);
        if (layout == null) return original;

        using (original)
        {
            var width = layout.Columns * layout.CellWidth;
            var height = layout.Rows * layout.CellHeight;
            var output = new Color[width * height];
            var pixels = new Color[original.Width * original.Height];
            original.GetData(pixels);
            Sample(pixels, original.Width, new Rectangle(0, 0, original.Width, original.Height),
                output, width, new Rectangle(0, 0, width, height));
            foreach (var patch in layout.Patches)
            {
                var file = AssetFiles.Resolve(patch.File);
                // Keep the bundled fallback while a newly announced page is still downloading.
                if (file == null || !File.Exists(file)) continue;
                using var input = File.OpenRead(file);
                using var page = Texture2D.FromStream(device, input);
                var data = new Color[page.Width * page.Height];
                page.GetData(data);
                for (var i = 0; i < patch.Source.Length; i++)
                {
                    var index = patch.Source[i];
                    var x0 = index % patch.Columns * page.Width / patch.Columns;
                    var x1 = (index % patch.Columns + 1) * page.Width / patch.Columns;
                    var y0 = index / patch.Columns * page.Height / patch.Rows;
                    var y1 = (index / patch.Columns + 1) * page.Height / patch.Rows;
                    var target = patch.Target[i];
                    var from = patch.Rects.Length > 0
                        ? new Rectangle(patch.Rects[i][0], patch.Rects[i][1], patch.Rects[i][2], patch.Rects[i][3])
                        : new Rectangle(x0, y0, x1 - x0, y1 - y0);
                    if (from.Right > page.Width || from.Bottom > page.Height)
                        throw new InvalidDataException($"Atlas frame exceeds its source page: {patch.File}");
                    var cell = new Rectangle(target % layout.Columns * layout.CellWidth, target / layout.Columns * layout.CellHeight,
                        layout.CellWidth, layout.CellHeight);
                    for (var y = cell.Top; y < cell.Bottom; y++) Array.Clear(output, y * width + cell.X, cell.Width);
                    var dest = cell;
                    if (patch.Fit)
                    {
                        var scale = Math.Min((cell.Width - 2f * patch.Inset) / from.Width, (cell.Height - 2f * patch.Inset) / from.Height);
                        var w = Math.Max(1, (int)MathF.Round(from.Width * scale));
                        var h = Math.Max(1, (int)MathF.Round(from.Height * scale));
                        dest = new Rectangle(cell.X + (cell.Width - w) / 2, cell.Bottom - patch.Inset - h, w, h);
                    }
                    Sample(data, page.Width, from, output, width, dest);
                }
            }
            var result = new Texture2D(device, width, height);
            result.SetData(output);
            return result;
        }
    }

    private static void Sample(Color[] source, int sourceWidth, Rectangle from, Color[] target, int targetWidth, Rectangle to)
    {
        for (var y = 0; y < to.Height; y++)
        for (var x = 0; x < to.Width; x++)
        {
            var sx = from.X + Math.Min(from.Width - 1, (2 * x + 1) * from.Width / (2 * to.Width));
            var sy = from.Y + Math.Min(from.Height - 1, (2 * y + 1) * from.Height / (2 * to.Height));
            target[(to.Y + y) * targetWidth + to.X + x] = source[sy * sourceWidth + sx];
        }
    }
}

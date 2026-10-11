using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Text.Json;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

internal sealed record FormLook(string Tint = "#dddfe8", string Accent = "#c0c6de", string Silhouette = "blank", int Maturity = 0, string Bloodline = "unformed");

/// <summary>Original 32px sprites. Shape and pigmentation emerge from server-reported habits, never character creation choices.</summary>
internal sealed class EvolvingSprites(GraphicsDevice device, Textures textures) : IDisposable
{
    private readonly Dictionary<FormLook, Texture2D> _cache = [];
    private Dictionary<string, string>? _authored;
    public Texture2D Get(FormLook look)
    {
        if (_cache.TryGetValue(look, out var found)) return found;
        if (Authored(look) is { } art) return _cache[look] = art;
        var canvas = new Canvas(96, 128);
        var tint = Hex(look.Tint); var accent = Hex(look.Accent);
        var ink = new Color(28, 34, 47);
        for (var row = 0; row < 4; row++)
        for (var frame = 0; frame < 3; frame++)
        {
            canvas.Origin = new Point(frame * 32, row * 32);
            var bob = frame == 1 ? 0 : 1;
            var mature = look.Maturity > 0;
            var skin = mature ? Color.Lerp(tint, new Color(221, 225, 212), .65f) : new Color(230, 234, 229);
            // Silhouette, legs, arms, softly shaded neutral face.
            if (mature)
            {
                canvas.Fill(8, 15 + bob, 16, 13, ink);
                canvas.Fill(9, 16 + bob, 14, 10, tint * .65f);
                canvas.Fill(10, 17 + bob, 5, 9, tint);
            }
            canvas.Fill(11, 23, 4, 7 - (frame == 0 ? 1 : 0), mature ? ink : new Color(151, 165, 174));
            canvas.Fill(17, 23, 4, 7 - (frame == 2 ? 1 : 0), mature ? ink : new Color(151, 165, 174));
            canvas.Fill(11, 16 + bob, 10, 10, mature ? tint : new Color(207, 215, 214));
            canvas.Fill(8, 17 + bob, 3, 7, mature ? tint * .8f : skin * .8f);
            canvas.Fill(21, 17 + bob, 3, 7, mature ? tint * .8f : skin * .8f);
            canvas.Fill(8, 23 + bob, 3, 2, skin); canvas.Fill(21, 23 + bob, 3, 2, skin);
            canvas.Ellipse(16, 10 + bob, 8, 8, mature ? ink : new Color(119, 133, 150));
            canvas.Ellipse(16, 9 + bob, 6, 7, skin);
            canvas.Fill(11, 7 + bob, 3, 5, Color.Lerp(skin, Color.White, .4f));
            if (row != 3)
            {
                foreach (var x in row == 0 ? new[] { 13, 18 } : row == 1 ? [11] : [20])
                    canvas.Fill(x, 10 + bob, 1, mature ? 2 : 1, mature ? accent : new Color(146, 160, 164));
            }
            if (mature)
            {
                canvas.Fill(11, 20 + bob, 10, 2, ink); canvas.Fill(15, 20 + bob, 3, 2, accent);
                if (look.Silhouette is "hood" or "robe")
                {
                    canvas.Fill(9, 3 + bob, 14, 4, tint); canvas.Fill(8, 6 + bob, 3, 10, tint);
                    canvas.Fill(21, 6 + bob, 3, 10, tint * .7f);
                    canvas.Fill(12, 4 + bob, 7, 1, Color.Lerp(tint, accent, .4f));
                }
                if (look.Silhouette == "plate")
                {
                    canvas.Fill(9, 16 + bob, 5, 4, accent); canvas.Fill(19, 16 + bob, 5, 4, accent * .65f);
                    canvas.Fill(13, 17 + bob, 6, 3, Color.Lerp(tint, accent, .5f));
                }
                if (look.Silhouette == "blade")
                {
                    canvas.Fill(row == 1 ? 6 : 25, 15, 2, 13, new Color(174, 188, 193));
                    canvas.Fill(row == 1 ? 4 : 23, 22, 6, 2, accent);
                }
                if (look.Maturity >= 2 && look.Silhouette is not "hood" and not "robe")
                {
                    canvas.Fill(10, 3 + bob, 12, 3, tint * .7f); canvas.Fill(9, 6 + bob, 3, 3, tint);
                    canvas.Fill(11, 2 + bob, 4, 2, tint); canvas.Fill(17, 1 + bob, 3, 3, tint);
                }
                if (look.Bloodline == "wildborn")
                {
                    canvas.Fill(6, 3 + bob, 2, 8, accent); canvas.Fill(24, 3 + bob, 2, 8, accent);
                    canvas.Fill(4, 4 + bob, 4, 2, accent); canvas.Fill(25, 4 + bob, 4, 2, accent);
                }
                if (look.Bloodline == "emberkin")
                { canvas.Fill(9, 1 + bob, 3, 5, accent); canvas.Fill(21, 1 + bob, 3, 5, accent); }
                if (look.Bloodline == "moonbound")
                { canvas.Fill(13, 0, 7, 1, accent); canvas.Fill(12, 1, 1, 2, accent); canvas.Fill(20, 1, 1, 2, accent); }
            }
        }
        return _cache[look] = canvas.ToTexture(device);
    }

    private Texture2D? Authored(FormLook look)
    {
        if (_authored == null)
        {
            var path = Path.Combine(CharacterArt.Folder, "forms.json");
            _authored = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : [];
        }
        var silhouette = look.Maturity == 0 ? "blank" : look.Silhouette;
        if (!_authored.TryGetValue(silhouette, out var file) || textures.Sheet(file) is not { } sheet
            || sheet.Width != 96 || sheet.Height != 128) return null;
        var pixels = new Color[sheet.Width * sheet.Height]; sheet.GetData(pixels);
        var tint = Hex(look.Tint).ToVector3();
        if (look.Maturity > 0)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i]; if (c.A == 0) continue;
                var alpha = c.A / 255f;
                var rgb = c.ToVector3() / alpha;
                var brightness = (rgb.X + rgb.Y + rgb.Z) / 3;
                // Dye the neutral cloth/steel midtones while preserving ivory faces and dark contours.
                if (brightness is > .19f and < .73f && Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z)) - Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z)) < .15f)
                {
                    var dyed = Vector3.Clamp(tint * (brightness / .42f), Vector3.Zero, Vector3.One);
                    rgb = Vector3.Lerp(rgb, dyed, .72f);
                }
                if (look.Bloodline != "unformed" && brightness > .73f)
                    rgb = Vector3.Lerp(rgb, tint, .2f);
                pixels[i] = new Color(rgb * alpha) with { A = c.A };
            }
        }
        var canvas = new Canvas(96, 128); canvas.CopyFrom(pixels);
        var accent = Hex(look.Accent);
        if (look.Maturity > 0)
        for (var row = 0; row < 4; row++)
        for (var frame = 0; frame < 3; frame++)
        {
            canvas.Origin = new Point(frame * 32, row * 32);
            if (look.Bloodline == "moonbound")
            {
                canvas.Fill(12, 0, 8, 1, accent); canvas.Fill(10, 1, 2, 1, accent); canvas.Fill(20, 1, 2, 1, accent);
            }
            if (look.Bloodline == "wildborn")
            {
                canvas.Fill(8, 1, 2, 8, accent); canvas.Fill(22, 1, 2, 8, accent);
                canvas.Fill(6, 2, 3, 2, accent); canvas.Fill(23, 3, 3, 2, accent);
            }
            if (look.Bloodline == "emberkin")
            {
                canvas.Fill(9, 1, 2, 5, accent); canvas.Fill(21, 1, 2, 5, accent);
                canvas.Fill(10, 0, 1, 3, accent); canvas.Fill(20, 0, 1, 3, accent);
            }
        }
        return canvas.ToTexture(device);
    }

    public void Reload() { Dispose(); _authored = null; }
    private static Color Hex(string s) => uint.TryParse(s.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out var v)
        ? new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v) : Color.White;
    public void Trim() { if (_cache.Count > 128) Dispose(); }
    public void Dispose() { foreach (var t in _cache.Values) t.Dispose(); _cache.Clear(); }
}

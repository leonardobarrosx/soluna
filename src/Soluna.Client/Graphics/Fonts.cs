using FontStashSharp;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

/// <summary>
/// Runtime TTF fonts through FontStashSharp, which avoids the MonoGame content pipeline.
/// Uses assets/fonts/*.ttf when there is one, otherwise a system font.
/// </summary>
internal sealed class Fonts
{
    private static readonly string[] SystemFonts =
    [
        @"C:\Windows\Fonts\segoeui.ttf",
        @"C:\Windows\Fonts\arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
    ];

    private readonly FontSystem _system = new();

    public Fonts()
    {
        var folder = Path.Combine(DataPaths.Assets, "fonts");
        var own = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.ttf").Order().FirstOrDefault() : null;
        var path = own ?? SystemFonts.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("No font found. Put a .ttf file in assets/fonts.");
        _system.AddFont(File.ReadAllBytes(path));

        Small = _system.GetFont(13);
        Body = _system.GetFont(15);
        Title = _system.GetFont(22);
    }

    public SpriteFontBase Small { get; }
    public SpriteFontBase Body { get; }
    public SpriteFontBase Title { get; }
}

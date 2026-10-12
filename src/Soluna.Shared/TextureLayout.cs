using System.Text.Json;

namespace Soluna.Shared;

/// <summary>Optional sidecar for assembling native engine frames from higher-resolution art pages.
/// Source PNGs remain unchanged; the client samples each cell independently at load time.</summary>
public sealed class TextureLayout
{
    public int Columns { get; set; } = 3;
    public int Rows { get; set; } = 4;
    public int CellWidth { get; set; } = 32;
    public int CellHeight { get; set; } = 32;
    public List<AtlasPatch> Patches { get; set; } = [];
    public TerrainEdges? Terrain { get; set; }

    public static TextureLayout? Read(string imagePath)
    {
        var path = imagePath + ".layout.json";
        if (!File.Exists(path)) return null;
        var layout = JsonSerializer.Deserialize<TextureLayout>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        layout?.Validate();
        return layout;
    }

    public void Validate()
    {
        if (Columns is < 1 or > 64 || Rows is < 1 or > 64 || CellWidth is < 1 or > 128 || CellHeight is < 1 or > 128
            || (long)Columns * Rows * CellWidth * CellHeight > 4_194_304)
            throw new InvalidDataException("Atlas output dimensions exceed the supported limits.");
        if (Terrain is { } terrain && new[] { terrain.GrassTile }.Concat(terrain.Grass).Concat(terrain.BlendInto).Concat(terrain.Water)
            .Any(i => i < 0 || i >= Columns * Rows))
            throw new InvalidDataException("Terrain transition references a tile outside its atlas.");
        if (Terrain?.Blends.Any(b => b.Width is < 1 or > 12 || new[] { b.Tile }.Concat(b.From).Concat(b.Into)
            .Any(i => i < 0 || i >= Columns * Rows)) == true)
            throw new InvalidDataException("Terrain blend references an invalid tile or edge width.");
        foreach (var patch in Patches)
        {
            if (string.IsNullOrWhiteSpace(patch.File) || patch.Columns is < 1 or > 64 || patch.Rows is < 1 or > 64
                || patch.Source.Length != patch.Target.Length || patch.Source.Length == 0
                || patch.Source.Any(i => i < 0 || i >= patch.Columns * patch.Rows)
                || patch.Target.Any(i => i < 0 || i >= Columns * Rows))
                throw new InvalidDataException("Atlas patch has an invalid source grid or cell mapping.");
            if (patch.Rects.Length != 0 && (patch.Rects.Length != patch.Source.Length
                || patch.Rects.Any(r => r.Length != 4 || r[0] < 0 || r[1] < 0 || r[2] <= 0 || r[3] <= 0)))
                throw new InvalidDataException("Atlas patch rectangles must match its mapped frames.");
            if (patch.Inset < 0 || patch.Inset * 2 >= Math.Min(CellWidth, CellHeight))
                throw new InvalidDataException("Atlas padding leaves no room for a frame.");
        }
    }
}

/// <summary>Optional grass-over-surface transitions for the ground layer, using tile indices.</summary>
public sealed class TerrainEdges
{
    public int GrassTile { get; set; }
    public int[] Grass { get; set; } = [];
    public int[] BlendInto { get; set; } = [];
    public int[] Water { get; set; } = [];
    public List<TerrainBlend> Blends { get; set; } = [];
}

/// <summary>A material painted over neighboring material edges. Rules compose in listed order.</summary>
public sealed class TerrainBlend
{
    public int Tile { get; set; }
    public int[] From { get; set; } = [];
    public int[] Into { get; set; } = [];
    public int Width { get; set; } = 5;
}

public sealed class AtlasPatch
{
    // Relative to the game's assets folder, so the regular asset synchronization also transfers art pages.
    public string File { get; set; } = "";
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int[] Source { get; set; } = [];
    public int[] Target { get; set; } = [];
    public int[][] Rects { get; set; } = [];
    public bool Fit { get; set; }
    public int Inset { get; set; }
    /// <summary>Average source pixels when reducing detailed ground art to native cells.</summary>
    public bool Average { get; set; }
}

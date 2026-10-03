namespace Soluna.Shared;

/// <summary>
/// Picks the right piece of an LPC terrain sheet (3 columns x 6 rows) for a cell, from which
/// neighbours share the terrain. Rows 0-1 (columns 1-2) are the inner corners, rows 2-4 the
/// outer edges around a centre, row 5 plain fills. Shapes should be at least two cells thick.
/// </summary>
public static class Autotile
{
    public const int Centre = 10;
    public static readonly int[] Fills = [15, 16, 17];

    public static int Resolve(Func<int, int, bool> has, int x, int y, Random? rng = null)
    {
        bool n = has(x, y - 1), s = has(x, y + 1), w = has(x - 1, y), e = has(x + 1, y);

        if (!n && !w) return 6;
        if (!n && !e) return 8;
        if (!s && !w) return 12;
        if (!s && !e) return 14;
        if (!n) return 7;
        if (!s) return 13;
        if (!w) return 9;
        if (!e) return 11;

        // Surrounded on all four sides: a missing diagonal means a concave (inner) corner.
        if (!has(x - 1, y - 1)) return 5;
        if (!has(x + 1, y - 1)) return 4;
        if (!has(x - 1, y + 1)) return 2;
        if (!has(x + 1, y + 1)) return 1;

        return rng == null ? Centre : Fills[rng.Next(Fills.Length)];
    }
}

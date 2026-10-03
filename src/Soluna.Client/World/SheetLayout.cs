using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;
using Soluna.Shared;

namespace Soluna.Client.World;

/// <summary>
/// Frame picking for the two sheet layouts the engine reads:
/// LPC (9 columns, rows up/left/down/right, column 0 standing, 1-8 a walk cycle) and
/// RPG Maker style (3 columns step/stand/step, rows down/left/right/up).
/// </summary>
internal static class SheetLayout
{
    private const int S = Constants.TileSize;

    public static bool IsLpc(Texture2D sheet) => sheet.Width / CharacterSprites.Columns == sheet.Height / CharacterSprites.Rows;

    public static Rectangle Frame(Texture2D sheet, Direction dir, bool moving, float progress, bool leftFoot)
    {
        if (IsLpc(sheet))
        {
            var size = sheet.Width / CharacterSprites.Columns;
            var row = dir switch { Direction.Up => 0, Direction.Left => 1, Direction.Down => 2, _ => 3 };
            // One tile is half a walk cycle: four frames, the other half on the next step.
            var col = moving ? 1 + (leftFoot ? 0 : 4) + Math.Min(3, (int)(progress * 4)) : 0;
            return new Rectangle(col * size, row * size, size, size);
        }

        var fw = sheet.Width / 3;
        var fh = sheet.Height / 4;
        var column = moving && progress < 0.5f ? (leftFoot ? 0 : 2) : 1;
        return new Rectangle(column * fw, (int)dir * fh, fw, fh);
    }

    /// <summary>Where to draw a frame relative to its tile so the feet land on the tile's bottom edge.</summary>
    public static Vector2 Offset(Texture2D sheet)
    {
        if (IsLpc(sheet))
        {
            var size = sheet.Width / CharacterSprites.Columns;
            // LPC feet sit a few pixels above the frame's bottom.
            return new Vector2((S - size) / 2f, S - size + 3);
        }
        var fw = sheet.Width / 3;
        var fh = sheet.Height / 4;
        return new Vector2((S - fw) / 2f, S - fh);
    }

    /// <summary>Distance from the drawn frame's top to the top of the head.</summary>
    public static float HeadTop(Texture2D sheet) => IsLpc(sheet) ? 12 : 0;
}

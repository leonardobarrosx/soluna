using Microsoft.Xna.Framework;
using Soluna.Shared;

namespace Soluna.Client.World;

/// <summary>
/// A character on the tile grid. The logical tile changes the moment a move starts;
/// the drawn position slides from the previous tile over <see cref="Constants.WalkTimeMs"/>.
/// </summary>
internal sealed class Character(int id, string name, Appearance look, Equipment equipment)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public Appearance Look { get; } = look;
    public Equipment Equipment { get; set; } = equipment;

    public int TileX { get; private set; }
    public int TileY { get; private set; }
    public Direction Dir { get; set; }

    private int _fromX, _fromY;

    /// <summary>0 when a step starts, 1 once it is done (and while standing).</summary>
    public float Progress { get; private set; } = 1;

    /// <summary>Flips every step, so walk cycles alternate feet.</summary>
    public bool LeftFoot { get; private set; }

    public bool Moving => Progress < 1;

    /// <summary>Health for the bar over the head; MaxHp 0 means unknown (no bar until the first hit).</summary>
    public int Hp { get; set; }
    public int MaxHp { get; set; }

    /// <summary>Goes from 1 to 0 over an attack swing; the sprite leans toward the target meanwhile.</summary>
    public float Lunge { get; private set; }

    private const float LungeSeconds = 0.18f;

    public void Swing(Direction dir)
    {
        Dir = dir;
        Lunge = 1;
    }

    /// <summary>Extra drawing offset while swinging: a quick lean of a few pixels toward the facing direction.</summary>
    public Vector2 LungeOffset
    {
        get
        {
            if (Lunge <= 0) return Vector2.Zero;
            var (dx, dy) = Dir.Delta();
            return new Vector2(dx, dy) * (MathF.Sin(MathF.PI * Lunge) * 6);
        }
    }

    public void Place(int x, int y, Direction dir)
    {
        TileX = _fromX = x;
        TileY = _fromY = y;
        Dir = dir;
        Progress = 1;
    }

    public void StartMove(Direction dir, int toX, int toY)
    {
        _fromX = TileX;
        _fromY = TileY;
        TileX = toX;
        TileY = toY;
        Dir = dir;
        Progress = 0;
        LeftFoot = !LeftFoot;
    }

    /// <summary>Advances the walk. Returns the milliseconds left over if the step finished this frame.</summary>
    public float Update(float elapsedMs)
    {
        if (Lunge > 0) Lunge = Math.Max(0, Lunge - elapsedMs / 1000f / LungeSeconds);
        if (!Moving) return 0;
        Progress += elapsedMs / Constants.WalkTimeMs;
        if (Progress < 1) return 0;

        var leftover = (Progress - 1) * Constants.WalkTimeMs;
        Progress = 1;
        return leftover;
    }

    /// <summary>Top-left of the tile the character is drawn on, in world pixels.</summary>
    public Vector2 Position
    {
        get
        {
            var from = new Vector2(_fromX, _fromY);
            var to = new Vector2(TileX, TileY);
            return Vector2.Lerp(from, to, Progress) * Constants.TileSize;
        }
    }
}

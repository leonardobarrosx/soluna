using Microsoft.Xna.Framework;
using Soluna.Shared;

namespace Soluna.Client.World;

/// <summary>
/// A character on the tile grid. The logical tile changes the moment a move starts;
/// the drawn position slides from the previous tile over <see cref="Constants.WalkTimeMs"/>.
/// </summary>
internal sealed class Character(int id, string name, byte sprite)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public byte Sprite { get; } = sprite;

    public int TileX { get; private set; }
    public int TileY { get; private set; }
    public Direction Dir { get; set; }

    private int _fromX, _fromY;
    private float _progress = 1;
    private bool _leftFoot;

    public bool Moving => _progress < 1;

    public void Place(int x, int y, Direction dir)
    {
        TileX = _fromX = x;
        TileY = _fromY = y;
        Dir = dir;
        _progress = 1;
    }

    public void StartMove(Direction dir, int toX, int toY)
    {
        _fromX = TileX;
        _fromY = TileY;
        TileX = toX;
        TileY = toY;
        Dir = dir;
        _progress = 0;
        _leftFoot = !_leftFoot;
    }

    /// <summary>Advances the walk. Returns the milliseconds left over if the step finished this frame.</summary>
    public float Update(float elapsedMs)
    {
        if (!Moving) return 0;
        _progress += elapsedMs / Constants.WalkTimeMs;
        if (_progress < 1) return 0;

        var leftover = (_progress - 1) * Constants.WalkTimeMs;
        _progress = 1;
        return leftover;
    }

    /// <summary>Top-left of the tile the character is drawn on, in world pixels.</summary>
    public Vector2 Position
    {
        get
        {
            var from = new Vector2(_fromX, _fromY);
            var to = new Vector2(TileX, TileY);
            return Vector2.Lerp(from, to, _progress) * Constants.TileSize;
        }
    }

    /// <summary>Column in a 3x4 sheet: 0 and 2 are steps, 1 is standing.</summary>
    public int Frame => Moving && _progress < 0.5f ? (_leftFoot ? 0 : 2) : 1;
}

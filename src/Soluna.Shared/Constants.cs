namespace Soluna.Shared;

public static class Constants
{
    public const string GameName = "Soluna";
    public const string ConnectionKey = "soluna-dev";
    public const int DefaultPort = 7171;

    // Same grid as Crystalshire: 32x32 tiles, which also fits the free 32px tilesets.
    public const int TileSize = 32;

    // Milliseconds a character takes to cross one tile.
    public const int WalkTimeMs = 240;

    public const int MaxNameLength = 16;
    public const int MaxChatLength = 120;

    // Accounts, Crystalshire style: one login, a few characters.
    public const int MaxCharacters = 3;
    public const int MinUserLength = 3;
    public const int MaxUserLength = 20;
    public const int MinPasswordLength = 6;
    public const int MaxPasswordLength = 64;
}

public enum Direction : byte
{
    Down = 0,
    Left = 1,
    Right = 2,
    Up = 3,
}

public static class DirectionExtensions
{
    public static (int dx, int dy) Delta(this Direction dir) => dir switch
    {
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        Direction.Right => (1, 0),
        Direction.Up => (0, -1),
        _ => (0, 0),
    };
}

namespace Soluna.Shared;

/// <summary>
/// Tile indices of the built-in placeholder tileset, which the client paints in code
/// so the engine runs before any real art is dropped into assets/.
/// </summary>
public static class PlaceholderTiles
{
    public const string TilesetName = "placeholder";
    public const int Columns = 8;

    public const int Grass = 0;
    public const int GrassDark = 1;
    public const int Path = 2;
    public const int Water = 3;
    public const int StoneWall = 4;
    public const int TreeTrunk = 5;
    public const int TreeCanopy = 6;
    public const int Flowers = 7;
    public const int Rock = 8;
    public const int WoodFloor = 9;

    public const int Count = 10;
}

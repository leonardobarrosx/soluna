using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;
using Soluna.Shared;

namespace Soluna.Client.World;

internal sealed class MapRenderer(Textures textures, Sprites sprites)
{
    private const int S = Constants.TileSize;

    /// <summary>Draws layers [from, to) over the tiles the camera can see.</summary>
    public void DrawLayers(SpriteBatch batch, MapData map, Camera camera, int from, int to)
    {
        var (x0, y0, x1, y1) = VisibleTiles(map, camera);
        for (var layer = from; layer < to; layer++)
        {
            var cells = map.Layers[layer];
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var tile = cells[map.Index(x, y)];
                if (tile != TileRef.Empty) DrawTile(batch, map, tile, new Vector2(x * S, y * S), Color.White);
            }
        }
    }

    public void DrawTile(SpriteBatch batch, MapData map, int tile, Vector2 position, Color tint)
    {
        var set = TileRef.Tileset(tile);
        if (set < 0 || set >= map.Tilesets.Count) return;

        DrawTile(batch, textures.Tileset(map.Tilesets[set]), TileRef.Index(tile), position, tint);
    }

    public void DrawTile(SpriteBatch batch, Texture2D texture, int index, Vector2 position, Color tint)
    {
        var columns = Math.Max(1, texture.Width / S);
        var source = new Rectangle(index % columns * S, index / columns * S, S, S);
        if (source.Bottom > texture.Height) source = new Rectangle(0, 0, Math.Min(S, texture.Width), Math.Min(S, texture.Height));
        batch.Draw(texture, position, source, tint);
    }

    public void DrawCharacter(SpriteBatch batch, Character character)
    {
        var sheet = sprites.SheetFor(character.Look, character.Equipment);
        var frame = SheetLayout.Frame(sheet, character.Dir, character.Moving, character.Progress, character.LeftFoot);
        var shadow = textures.Shadow;
        batch.Draw(shadow, character.Position + new Vector2((S - shadow.Width) / 2f, S - shadow.Height + 1), Color.White);
        batch.Draw(sheet, character.Position + SheetLayout.Offset(sheet), frame, Color.White);
    }

    public static (int x0, int y0, int x1, int y1) VisibleTiles(MapData map, Camera camera)
    {
        var view = camera.WorldSize;
        var x0 = Math.Max(0, (int)MathF.Floor(camera.Position.X / S) - 1);
        var y0 = Math.Max(0, (int)MathF.Floor(camera.Position.Y / S) - 1);
        var x1 = Math.Min(map.Width - 1, (int)MathF.Ceiling((camera.Position.X + view.X) / S) + 1);
        var y1 = Math.Min(map.Height - 1, (int)MathF.Ceiling((camera.Position.Y + view.Y) / S) + 1);
        return (x0, y0, x1, y1);
    }
}

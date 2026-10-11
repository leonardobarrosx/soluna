using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Soluna.Client.Graphics;
using Soluna.Shared;

namespace Soluna.Client.World;

/// <summary>Data-authored grass edges with deterministic pixel contours. Collision and tile IDs remain unchanged.</summary>
internal sealed class TerrainTransitions(Textures textures)
{
    private const int S = Constants.TileSize;
    private readonly Dictionary<(Texture2D atlas, int index, int mask), Texture2D> _edges = [];
    private readonly Dictionary<Texture2D, Color[]> _pixels = [];

    public void Draw(SpriteBatch batch, MapData map, int tile, int x, int y)
    {
        var set = TileRef.Tileset(tile);
        if (set < 0 || set >= map.Tilesets.Count) return;
        var spec = textures.Terrain(map.Tilesets[set]);
        var index = TileRef.Index(tile);
        if (spec == null || !spec.BlendInto.Contains(index)) return;
        bool Grass(int dx, int dy)
        {
            if (!map.InBounds(x + dx, y + dy)) return false;
            var neighbor = map.GetTile(MapLayer.Ground, x + dx, y + dy);
            return neighbor != TileRef.Empty && TileRef.Tileset(neighbor) == set && spec.Grass.Contains(TileRef.Index(neighbor));
        }
        var mask = (Grass(0, -1) ? 1 : 0) | (Grass(1, 0) ? 2 : 0) | (Grass(0, 1) ? 4 : 0) | (Grass(-1, 0) ? 8 : 0)
            | (Grass(-1, -1) ? 16 : 0) | (Grass(1, -1) ? 32 : 0) | (Grass(1, 1) ? 64 : 0) | (Grass(-1, 1) ? 128 : 0);
        if (mask == 0) return;
        var atlas = textures.Tileset(map.Tilesets[set]);
        var key = (atlas, index, mask);
        if (!_edges.TryGetValue(key, out var edge))
        {
            // Texture hot-reload invalidates its generated contours too.
            foreach (var old in _edges.Keys.Where(k => k.atlas.IsDisposed).ToArray())
            { _edges[old].Dispose(); _edges.Remove(old); _pixels.Remove(old.atlas); }
            if (!_pixels.TryGetValue(atlas, out var source))
            { source = new Color[atlas.Width * atlas.Height]; atlas.GetData(source); _pixels[atlas] = source; }
            var cols = Math.Max(1, atlas.Width / S);
            var ox = spec.GrassTile % cols * S; var oy = spec.GrassTile / cols * S;
            if (ox + S > atlas.Width || oy + S > atlas.Height) return;
            var pixels = new Color[S * S];
            var water = spec.Water.Contains(index);
            for (var py = 0; py < S; py++)
            for (var px = 0; px < S; px++)
            {
                var distance = 100;
                if ((mask & 1) != 0) distance = Math.Min(distance, py);
                if ((mask & 2) != 0) distance = Math.Min(distance, 31 - px);
                if ((mask & 4) != 0) distance = Math.Min(distance, 31 - py);
                if ((mask & 8) != 0) distance = Math.Min(distance, px);
                if ((mask & 16) != 0) distance = Math.Min(distance, px + py);
                if ((mask & 32) != 0) distance = Math.Min(distance, 31 - px + py);
                if ((mask & 64) != 0) distance = Math.Min(distance, 62 - px - py);
                if ((mask & 128) != 0) distance = Math.Min(distance, px + 31 - py);
                var contour = 2 + ((px * 17 + py * 7 + px / 3 * 11) % 4);
                if (distance <= contour)
                    pixels[py * S + px] = source[(oy + py) * atlas.Width + ox + px];
                else if (distance == contour + 1)
                    pixels[py * S + px] = water ? new Color(92, 111, 90) : new Color(45, 54, 41, 255);
                else if (water && distance == contour + 2)
                    pixels[py * S + px] = new Color(42, 72, 73);
            }
            edge = new Texture2D(atlas.GraphicsDevice, S, S); edge.SetData(pixels); _edges[key] = edge;
        }
        batch.Draw(edge, new Vector2(x * S, y * S), Color.White);
    }
}

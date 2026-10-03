using Microsoft.Xna.Framework.Graphics;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

/// <summary>Picks the sheet for a character: the composed LPC paper doll, or placeholder art without it.</summary>
internal sealed class Sprites(CharacterSprites lpc, Textures textures)
{
    public Texture2D SheetFor(Appearance look, Equipment equipment) =>
        lpc.Available ? lpc.Get(look, equipment) : textures.Character(look.GetHashCode() & 0xFF);

    public void Trim() => lpc.Trim();

    public void Clear() => lpc.Clear();
}

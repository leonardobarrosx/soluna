using Microsoft.Xna.Framework.Graphics;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

/// <summary>Picks the sheet for a character: the composed paper doll, or placeholder art without it.</summary>
internal sealed class Sprites(CharacterSprites dolls, Textures textures)
{
    public Texture2D SheetFor(Appearance look, Equipment equipment) =>
        dolls.Available ? dolls.Get(look, equipment) : textures.Character(look.GetHashCode() & 0xFF);

    public void Trim() => dolls.Trim();

    public void Clear() => dolls.Clear();
}

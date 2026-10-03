using Microsoft.Xna.Framework.Graphics;
using Soluna.Shared;

namespace Soluna.Client.Graphics;

/// <summary>Picks the sheet for a character: the composed paper doll, or placeholder art without it.</summary>
internal sealed class Sprites(CharacterSprites dolls, Textures textures)
{
    public Texture2D SheetFor(Appearance look, Equipment equipment) =>
        dolls.Available ? dolls.Get(look, equipment) : textures.Character(look.GetHashCode() & 0xFF);

    /// <summary>A look wearing a part that is not saved as an item yet, for editor previews.</summary>
    public Texture2D Preview(Appearance look, Equipment equipment, string sheet, string? color) =>
        dolls.Available ? dolls.Get(look, equipment, sheet, color) : SheetFor(look, equipment);

    public IEnumerable<string> PartIds => dolls.SheetIds;

    public void Trim() => dolls.Trim();

    public void Clear() => dolls.Clear();
}

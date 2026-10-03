using LiteNetLib.Utils;

namespace Soluna.Shared;

/// <summary>
/// Choices offered at character creation. Values are LPC sheet ids and palette variant names
/// (see assets/characters/lpc/catalog.json); labels are what the player sees.
/// </summary>
public static class CharacterOptions
{
    public static readonly (string id, string label)[] Bodies = [("male", "Masculino"), ("female", "Feminino")];

    public static readonly (string id, string label)[] Skins =
    [
        ("light", "Clara"), ("amber", "Âmbar"), ("olive", "Oliva"), ("taupe", "Morena clara"),
        ("bronze", "Bronze"), ("brown", "Morena"), ("black", "Negra"),
        ("lavender", "Lavanda"), ("blue", "Azul"), ("green", "Verde"),
    ];

    public static readonly (string id, string label)[] HairStyles =
    [
        ("hair_plain", "Liso"), ("hair_messy1", "Bagunçado"), ("hair_parted", "Repartido"),
        ("hair_bangs", "Franja"), ("hair_pixie", "Pixie"), ("hair_bob", "Chanel"),
        ("hair_long", "Longo"), ("hair_ponytail", "Rabo de cavalo"), ("hair_afro", "Black power"),
        ("hair_spiked", "Espetado"),
    ];

    public static readonly (string id, string label)[] HairColors =
    [
        ("black", "Preto"), ("raven", "Graúna"), ("dark_brown", "Castanho escuro"), ("chestnut", "Castanho"),
        ("light_brown", "Castanho claro"), ("blonde", "Loiro"), ("platinum", "Platinado"), ("white", "Branco"),
        ("gray", "Grisalho"), ("redhead", "Ruivo"), ("ginger", "Acobreado"), ("rose", "Rosa"),
        ("purple", "Roxo"), ("blue", "Azul"), ("green", "Verde"),
    ];

    public static readonly (string id, string label)[] EyeColors =
    [
        ("brown", "Castanhos"), ("blue", "Azuis"), ("green", "Verdes"), ("gray", "Cinzas"),
        ("purple", "Roxos"), ("yellow", "Âmbar"), ("red", "Vermelhos"), ("orange", "Laranja"),
    ];

    public static string HeadFor(string body) => body == "female" ? "heads_human_female" : "heads_human_male";
}

/// <summary>How a character was made at creation: indices into <see cref="CharacterOptions"/>.</summary>
public sealed record Appearance(byte Body, byte Skin, byte Hair, byte HairColor, byte Eyes)
{
    public string BodyId => CharacterOptions.Bodies[Body].id;
    public string SkinId => CharacterOptions.Skins[Skin].id;
    public string HairId => CharacterOptions.HairStyles[Hair].id;
    public string HairColorId => CharacterOptions.HairColors[HairColor].id;
    public string EyesId => CharacterOptions.EyeColors[Eyes].id;

    public bool IsValid =>
        Body < CharacterOptions.Bodies.Length
        && Skin < CharacterOptions.Skins.Length
        && Hair < CharacterOptions.HairStyles.Length
        && HairColor < CharacterOptions.HairColors.Length
        && Eyes < CharacterOptions.EyeColors.Length;

    public static Appearance Random(Random rng) => new(
        (byte)rng.Next(CharacterOptions.Bodies.Length),
        (byte)rng.Next(7), // natural skin tones only
        (byte)rng.Next(CharacterOptions.HairStyles.Length),
        (byte)rng.Next(CharacterOptions.HairColors.Length),
        (byte)rng.Next(CharacterOptions.EyeColors.Length));

    public void Write(NetDataWriter w)
    {
        w.Put(Body);
        w.Put(Skin);
        w.Put(Hair);
        w.Put(HairColor);
        w.Put(Eyes);
    }

    public static Appearance Read(NetDataReader r) => new(r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte(), r.GetByte());
}

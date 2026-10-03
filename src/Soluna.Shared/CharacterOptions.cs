using LiteNetLib.Utils;

namespace Soluna.Shared;

/// <summary>
/// Choices offered at character creation. Ids are chibi layer names (see
/// assets/characters/chibi/catalog.json) or colours; labels are what the player sees.
/// </summary>
public static class CharacterOptions
{
    public static readonly (string id, string label)[] Bodies = [("m", "Masculino"), ("f", "Feminino")];

    public static readonly (string id, string label)[] Skins =
    [
        ("#f6d2b4", "Clara"), ("#eab793", "Pêssego"), ("#d39a6f", "Dourada"), ("#b57a52", "Morena clara"),
        ("#94603d", "Morena"), ("#6e432a", "Negra"),
        ("#b8c4e8", "Lunar"), ("#9fd1b0", "Silvestre"),
    ];

    public static readonly (string id, string label)[] HairStyles =
    [
        ("hair_messy", "Bagunçado"), ("hair_hero", "Herói"), ("hair_straight", "Liso"),
        ("hair_medium", "Médio"), ("hair_ponytail", "Rabo de cavalo"),
    ];

    public static readonly (string id, string label)[] HairColors =
    [
        ("#2a2228", "Preto"), ("#4a3226", "Castanho escuro"), ("#7a4e30", "Castanho"), ("#b07a45", "Mel"),
        ("#e2c065", "Loiro"), ("#efe3c2", "Platinado"), ("#c9c9d4", "Grisalho"), ("#b4462c", "Ruivo"),
        ("#d97e9a", "Rosa"), ("#7c5cc4", "Lilás"), ("#4f7fc9", "Azul"), ("#5f9e6e", "Verde"),
    ];

    public static readonly (string id, string label)[] EyeColors =
    [
        ("eyes_brown", "Castanhos"), ("eyes_dark", "Escuros"), ("eyes_blue", "Azuis"), ("eyes_green", "Verdes"),
    ];
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
        (byte)rng.Next(6), // natural skin tones only
        (byte)rng.Next(CharacterOptions.HairStyles.Length),
        (byte)rng.Next(9), // natural hair colours only
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

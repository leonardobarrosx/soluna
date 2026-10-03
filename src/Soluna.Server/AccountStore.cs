using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Soluna.Shared;

namespace Soluna.Server;

/// <summary>A saved character: everything needed to put it back in the world.</summary>
internal sealed class CharacterSave
{
    public string Name { get; set; } = "";
    public Appearance Look { get; set; } = new(0, 0, 0, 0, 0);
    public int[] Equipment { get; set; } = new int[Shared.Equipment.SlotCount];
    public List<int> Inventory { get; set; } = [];
    public int MapId { get; set; } = MapStore.StartMapId;
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public Direction Dir { get; set; }
    public int Level { get; set; } = 1;
    public int Exp { get; set; }

    /// <summary>-1 until first saved: a new character starts full.</summary>
    public int Hp { get; set; } = -1;
    public int Mp { get; set; } = -1;

    public Shared.Equipment ToEquipment()
    {
        var equipment = new Shared.Equipment();
        for (var slot = 0; slot < Shared.Equipment.SlotCount && slot < Equipment.Length; slot++)
            equipment[(EquipSlot)slot] = Equipment[slot];
        return equipment;
    }
}

internal sealed class Account
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public int Iterations { get; set; }

    /// <summary>0 is a player; anything above can edit maps and use admin commands.</summary>
    public byte Access { get; set; }

    public CharacterSave?[] Characters { get; set; } = new CharacterSave?[Constants.MaxCharacters];
}

/// <summary>
/// Accounts as one JSON file each under data/accounts (ignored by git). Passwords are kept
/// only as salted PBKDF2-SHA256 hashes.
/// </summary>
internal sealed partial class AccountStore
{
    private const int Iterations = 100_000;
    private const int HashBytes = 32;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly HashSet<string> _characterNames = new(StringComparer.OrdinalIgnoreCase);

    public AccountStore(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            var account = Read(file);
            foreach (var character in account?.Characters ?? [])
            {
                if (character != null) _characterNames.Add(character.Name);
            }
        }
        Log.Info($"{Directory.EnumerateFiles(folder, "*.json").Count()} accounts, {_characterNames.Count} characters.");
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex UsernamePattern();

    public static string? CheckUsername(string username) =>
        username.Length < Constants.MinUserLength || username.Length > Constants.MaxUserLength
            ? $"O usuário precisa ter de {Constants.MinUserLength} a {Constants.MaxUserLength} caracteres."
            : !UsernamePattern().IsMatch(username)
                ? "Use só letras, números e _ no usuário."
                : null;

    public static string? CheckPassword(string password) =>
        password.Length < Constants.MinPasswordLength || password.Length > Constants.MaxPasswordLength
            ? $"A senha precisa ter de {Constants.MinPasswordLength} a {Constants.MaxPasswordLength} caracteres."
            : null;

    public bool Exists(string username) => File.Exists(PathFor(username));

    /// <summary>Creates an account. The very first one becomes admin, so a fresh server has someone who can edit maps.</summary>
    public Account Create(string username, string password)
    {
        var first = !Directory.EnumerateFiles(_folder, "*.json").Any();
        var salt = RandomNumberGenerator.GetBytes(16);
        var account = new Account
        {
            Username = username,
            Salt = Convert.ToBase64String(salt),
            Iterations = Iterations,
            PasswordHash = Convert.ToBase64String(Hash(password, salt, Iterations)),
            Access = first ? (byte)1 : (byte)0,
        };
        Save(account);
        if (first) Log.Info($"First account '{username}' created as admin.");
        return account;
    }

    /// <summary>The account if the password matches, otherwise null.</summary>
    public Account? Authenticate(string username, string password)
    {
        if (Read(PathFor(username)) is not { } account) return null;
        var expected = Convert.FromBase64String(account.PasswordHash);
        var actual = Hash(password, Convert.FromBase64String(account.Salt), account.Iterations);
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? account : null;
    }

    public bool NameTaken(string name) => _characterNames.Contains(name);

    public void ClaimName(string name) => _characterNames.Add(name);

    public void ReleaseName(string name) => _characterNames.Remove(name);

    public void Save(Account account)
    {
        var path = PathFor(account.Username);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(account, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static byte[] Hash(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);

    private string PathFor(string username) => Path.Combine(_folder, $"{username.ToLowerInvariant()}.json");

    private static Account? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<Account>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Warn($"Unreadable account file {path}: {ex.Message}");
            return null;
        }
    }
}

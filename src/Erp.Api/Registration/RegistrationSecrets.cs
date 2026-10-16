using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Erp.Api.Registration;

internal readonly record struct ApprovalToken(string Value, string Hash)
{
    private const int MaxLength = 128;

    public static ApprovalToken Create()
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return new ApprovalToken(value, HashOf(value));
    }

    public static bool TryHash(string? value, out string hash)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxLength)
        {
            hash = string.Empty;
            return false;
        }

        hash = HashOf(trimmed);
        return true;
    }

    private static string HashOf(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

// Meets the user pool policy: at least 12 characters with upper case, lower case, digits, and symbols.
internal static class InitialPassword
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#%+-=?@_";
    private const int Length = 20;

    public static string Create()
    {
        var all = Upper + Lower + Digits + Symbols;
        var characters = new char[Length];
        characters[0] = Pick(Upper);
        characters[1] = Pick(Lower);
        characters[2] = Pick(Digits);
        characters[3] = Pick(Symbols);
        for (var index = 4; index < Length; index++)
        {
            characters[index] = Pick(all);
        }

        RandomNumberGenerator.Shuffle(characters.AsSpan());
        return new string(characters);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}

using System.Security.Cryptography;
using System.Text;

namespace SpeakSafe.Core;

public static class Hasher
{
    private const int Iterations = 100_000;

    public static string Hash(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string secret, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2" || !int.TryParse(p[1], out var it)) return false;
        var salt = Convert.FromBase64String(p[2]);
        var expected = Convert.FromBase64String(p[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, it, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

public static class Codes
{
    // No 0/O, 1/I/L: codes get read out over the phone and written on paper.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Random(int length, string alphabet = Alphabet) =>
        string.Create(length, alphabet, (span, abc) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = abc[RandomNumberGenerator.GetInt32(abc.Length)];
        });

    public static string CaseCode() => "SS-" + Random(4);
    public static string Pin() => Random(4, "0123456789");
    public static string Recovery() => $"{Random(3)}-{Random(3)}-{Random(3)}";
    public static string StoredFileName() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static string NormalizeCode(string? raw)
    {
        var s = (raw ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        if (s.Length == 4) s = "SS-" + s;
        if (s.StartsWith("SS") && s.Length == 6) s = "SS-" + s[2..];
        return s;
    }
}

/// <summary>Encrypts narratives, messages and evidence at rest. The web app uses ASP.NET Data Protection.</summary>
public interface ICipher
{
    string Protect(string plain);
    string Unprotect(string cipher);
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] cipher);
}

/// <summary>Stores encrypted evidence bytes under a random name.</summary>
public interface IEvidenceFiles
{
    Task SaveAsync(string storedName, byte[] data);
    Task<byte[]?> ReadAsync(string storedName);
}

public sealed class MemoryEvidenceFiles : IEvidenceFiles
{
    private readonly Dictionary<string, byte[]> _files = [];
    private readonly Lock _gate = new();
    public Task SaveAsync(string storedName, byte[] data) { lock (_gate) _files[storedName] = data; return Task.CompletedTask; }
    public Task<byte[]?> ReadAsync(string storedName) { lock (_gate) return Task.FromResult(_files.GetValueOrDefault(storedName)); }
}

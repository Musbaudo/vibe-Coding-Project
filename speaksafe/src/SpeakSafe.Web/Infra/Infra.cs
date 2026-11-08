using Microsoft.AspNetCore.DataProtection;
using SpeakSafe.Core;

namespace SpeakSafe.Web.Infra;

/// <summary>
/// Encrypts narratives, messages and evidence with ASP.NET Data Protection. The keys live in App_Data\keys:
/// back that folder up with the database, because encrypted rows can't be read without it.
/// </summary>
public sealed class DataProtectionCipher(IDataProtectionProvider provider) : ICipher
{
    private readonly IDataProtector _text = provider.CreateProtector("SpeakSafe.Text.v1");
    private readonly IDataProtector _files = provider.CreateProtector("SpeakSafe.Files.v1");

    public string Protect(string plain) => _text.Protect(plain);
    public string Unprotect(string cipher) => _text.Unprotect(cipher);
    public byte[] Protect(byte[] plain) => _files.Protect(plain);
    public byte[] Unprotect(byte[] cipher) => _files.Unprotect(cipher);
}

/// <summary>Encrypted evidence files under App_Data\evidence, sharded by the first two characters of the random name.</summary>
public sealed class FileEvidenceFiles(string root) : IEvidenceFiles
{
    private string PathFor(string storedName)
    {
        if (storedName.Length < 8 || storedName.Any(ch => !char.IsAsciiHexDigitLower(ch) && !char.IsAsciiDigit(ch)))
            throw new SpeakException("File not found.", 404);
        return Path.Combine(root, storedName[..2], storedName + ".bin");
    }

    public async Task SaveAsync(string storedName, byte[] data)
    {
        var p = PathFor(storedName);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        await File.WriteAllBytesAsync(p, data);
    }

    public async Task<byte[]?> ReadAsync(string storedName)
    {
        var p = PathFor(storedName);
        return File.Exists(p) ? await File.ReadAllBytesAsync(p) : null;
    }
}

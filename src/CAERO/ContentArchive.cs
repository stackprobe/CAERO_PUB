using System.IO.Compression;
using System.Security.Cryptography;

namespace Caero;

// Shared with the build-time packer. This key discourages casual extraction;
// a client that decrypts its own assets cannot keep the key secret from analysis.
internal static class ContentArchive
{
    private static ReadOnlySpan<byte> Magic => "CAEROPK1"u8;
    private static byte[] Key => Convert.FromHexString("74CF6D80B125A9E306F278CA91D35E4B8A90C6FD524170BE36E8AB92D07F15C3");
    private const int NonceSize = 12, TagSize = 16, HeaderSize = 8 + NonceSize + TagSize;

    internal static byte[] Encode(IReadOnlyDictionary<string, byte[]> files)
    {
        using var plaintext = new MemoryStream();
        using (var zip = new ZipArchive(plaintext, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in files.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                ValidateName(name);
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(bytes);
            }
        byte[] result = new byte[checked(HeaderSize + (int)plaintext.Length)];
        Magic.CopyTo(result);
        var nonce = result.AsSpan(8, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, plaintext.GetBuffer().AsSpan(0, (int)plaintext.Length), result.AsSpan(HeaderSize),
            result.AsSpan(8 + NonceSize, TagSize), Magic);
        return result;
    }

    internal static IReadOnlyDictionary<string, byte[]> Decode(byte[] package)
    {
        if (package.Length < HeaderSize || !package.AsSpan(0, 8).SequenceEqual(Magic))
            throw new InvalidDataException("Unsupported or truncated Content.dat.");
        byte[] plaintext = new byte[package.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(Key, TagSize);
            aes.Decrypt(package.AsSpan(8, NonceSize), package.AsSpan(HeaderSize),
                package.AsSpan(8 + NonceSize, TagSize), plaintext, Magic);
        }
        catch (CryptographicException ex)
        { throw new InvalidDataException("Content.dat is damaged or has been modified.", ex); }
        using var stream = new MemoryStream(plaintext, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            ValidateName(entry.FullName);
            total = checked(total + entry.Length);
            if (total > 256L * 1024 * 1024) throw new InvalidDataException("Content.dat exceeds the asset size limit.");
            using var input = entry.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            if (!files.TryAdd(entry.FullName, output.ToArray()))
                throw new InvalidDataException("Duplicate asset in Content.dat: " + entry.FullName);
        }
        return files;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains(':')
            || name.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Invalid asset name: " + name);
    }
}

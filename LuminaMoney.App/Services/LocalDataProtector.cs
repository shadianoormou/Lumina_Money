using System.Security.Cryptography;
using System.Text;

namespace LuminaMoney.App.Services;

/// <summary>
/// Protects sensitive local text with an AES-256-GCM key held by the platform
/// Keychain/Keystore. Identifiers and numeric fields stay queryable in SQLite.
/// </summary>
public sealed class LocalDataProtector
{
    private const string Prefix = "enc:v1:";

    public async Task<byte[]> GetOrCreateKeyAsync(string profileId)
    {
        var keyName = $"lumina.local_key.{profileId}";
        var stored = await SecureStorage.Default.GetAsync(keyName);
        if (!string.IsNullOrWhiteSpace(stored)) return Convert.FromBase64String(stored);

        var key = RandomNumberGenerator.GetBytes(32);
        await SecureStorage.Default.SetAsync(keyName, Convert.ToBase64String(key));
        return key;
    }

    public string Protect(string? value, byte[] key)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal)) return value ?? "";
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);
        var packed = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, packed, nonce.Length + tag.Length, cipher.Length);
        return Prefix + Convert.ToBase64String(packed);
    }

    public string Unprotect(string? value, byte[] key)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return value ?? "";
        var packed = Convert.FromBase64String(value[Prefix.Length..]);
        if (packed.Length < 28) throw new CryptographicException("Local protected value is invalid.");
        var nonce = packed.AsSpan(0, 12);
        var tag = packed.AsSpan(12, 16);
        var cipher = packed.AsSpan(28);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
}

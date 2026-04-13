using System.Security.Cryptography;
using System.Text;

namespace EmployeeApi.Services;

/// <summary>
/// AES-256-GCM для полей [App_Messages].[Text] и [MetaJson]. Префикс enc1: в БД.
/// Ключ: Chat:MessageEncryptionKey — Base64, ровно 32 байта. Пустой ключ — шифрование отключено.
/// </summary>
public sealed class ChatMessageCipher
{
    public const string Prefix = "enc1:";
    private readonly byte[]? _key;

    public ChatMessageCipher(IConfiguration configuration)
    {
        var b64 = configuration["Chat:MessageEncryptionKey"]?.Trim();
        if (string.IsNullOrEmpty(b64)) return;
        try
        {
            var k = Convert.FromBase64String(b64);
            _key = k.Length == 32 ? k : null;
        }
        catch
        {
            _key = null;
        }
    }

    public bool IsEnabled => _key != null;

    public string ProtectField(string? plain)
    {
        if (_key == null || plain == null) return plain ?? "";
        if (plain.Length == 0) return plain;
        if (plain.StartsWith(Prefix, StringComparison.Ordinal)) return plain;
        return Prefix + EncryptToBase64(plain);
    }

    public string? ProtectFieldNullable(string? plain)
    {
        if (plain == null) return null;
        return ProtectField(plain);
    }

    public string UnprotectField(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored ?? "";
        if (_key == null || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        try
        {
            return DecryptFromBase64(stored.AsSpan(Prefix.Length));
        }
        catch
        {
            return stored;
        }
    }

    public string? UnprotectFieldNullable(string? stored)
    {
        if (stored == null) return null;
        return UnprotectField(stored);
    }

    private string EncryptToBase64(string plain)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key!, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        var combined = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, combined, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length + cipher.Length, tag.Length);
        return Convert.ToBase64String(combined);
    }

    private string DecryptFromBase64(ReadOnlySpan<char> base64Chars)
    {
        var bytes = Convert.FromBase64String(base64Chars.ToString());
        if (bytes.Length < 12 + 16) throw new InvalidOperationException("short");
        var nonce = bytes.AsSpan(0, 12);
        var tag = bytes.AsSpan(bytes.Length - 16);
        var cipher = bytes.AsSpan(12, bytes.Length - 12 - 16);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key!, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}

using System.Security.Cryptography;

namespace LightningAgent.Engine.Security;

/// <summary>
/// Encrypts and decrypts HODL invoice preimages at rest using AES-256-GCM.
/// When no encryption key is configured, falls back to plaintext (development only).
/// </summary>
public static class PreimageProtector
{
    private static byte[]? _key;

    /// <summary>
    /// Initializes the protector with a 32-byte encryption key.
    /// Call once at startup. If never called, preimages are stored in plaintext.
    /// </summary>
    public static void Initialize(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Encryption key must be exactly 32 bytes (256 bits).", nameof(key));
        _key = key;
    }

    /// <summary>
    /// Initializes from a hex-encoded key string. If null/empty, encryption is disabled.
    /// </summary>
    public static void Initialize(string? hexKey)
    {
        if (string.IsNullOrWhiteSpace(hexKey))
        {
            _key = null;
            return;
        }
        Initialize(Convert.FromHexString(hexKey));
    }

    public static bool IsEnabled => _key is not null;

    /// <summary>
    /// Encrypts a hex-encoded preimage. Returns "enc:" prefixed base64 ciphertext,
    /// or the original plaintext if encryption is not configured.
    /// </summary>
    public static string Protect(string preimageHex)
    {
        if (_key is null)
            return preimageHex;

        var plaintext = Convert.FromHexString(preimageHex);
        var nonce = RandomNumberGenerator.GetBytes(12); // 96-bit nonce for GCM
        var tag = new byte[16]; // 128-bit auth tag
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: nonce(12) + tag(16) + ciphertext(32) = 60 bytes
        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, nonce.Length);
        ciphertext.CopyTo(combined, nonce.Length + tag.Length);

        return "enc:" + Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Decrypts a protected preimage back to hex. Handles both encrypted ("enc:" prefix)
    /// and plaintext preimages for backwards compatibility.
    /// </summary>
    public static string Unprotect(string stored)
    {
        if (!stored.StartsWith("enc:"))
            return stored; // Already plaintext hex — backwards compatible

        if (_key is null)
            throw new InvalidOperationException(
                "Cannot decrypt preimage: encryption key is not configured. " +
                "Set Escrow:EncryptionKey in configuration.");

        var combined = Convert.FromBase64String(stored[4..]); // Skip "enc:" prefix
        var nonce = combined[..12];
        var tag = combined[12..28];
        var ciphertext = combined[28..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Convert.ToHexString(plaintext).ToLowerInvariant();
    }
}

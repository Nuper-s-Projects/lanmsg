using System.Security.Cryptography;
using System.Text;
using LanMsg.Core.Models;

public sealed class CryptoService
{
    private readonly byte[] _encKey;
    private readonly byte[] _authKey;
    private readonly HashSet<string> _usedNonces = new();
    private readonly object _nonceLock = new();

    public CryptoService(byte[] encKey, byte[] authKey)
    {
        _encKey = encKey;
        _authKey = authKey;
    }

    public static byte[] DeriveKeys(string groupCode)
    {
        var salt = Encoding.UTF8.GetBytes(LanDefaults.AppSalt);
        var raw = Rfc2898DeriveBytes.Pbkdf2(
            groupCode,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            64);
        return raw;
    }

    public static (byte[] encKey, byte[] authKey) SplitKeys(byte[] derived)
    {
        var enc = derived.Take(32).ToArray();
        var auth = derived.Skip(32).Take(32).ToArray();
        return (enc, auth);
    }

    public static string CreateVerifier(string groupCode)
    {
        var derived = DeriveKeys(groupCode);
        var hash = SHA256.HashData(derived);
        return Convert.ToBase64String(hash);
    }

    public static bool VerifyGroupCode(string groupCode, string verifier)
    {
        if (string.IsNullOrWhiteSpace(groupCode) || string.IsNullOrWhiteSpace(verifier))
            return false;
        return CreateVerifier(groupCode) == verifier;
    }

    public static CryptoService FromGroupCode(string groupCode)
    {
        var derived = DeriveKeys(groupCode);
        var (enc, auth) = SplitKeys(derived);
        return new CryptoService(enc, auth);
    }

    public static string ProtectGroupCode(string groupCode)
    {
        var bytes = Encoding.UTF8.GetBytes(groupCode);
        var entropy = Encoding.UTF8.GetBytes(LanDefaults.AppSalt);
        var protectedBytes = ProtectedData.Protect(bytes, entropy, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string? UnprotectGroupCode(string protectedValue)
    {
        try
        {
            var bytes = Convert.FromBase64String(protectedValue);
            var entropy = Encoding.UTF8.GetBytes(LanDefaults.AppSalt);
            var plain = ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public byte[] EncryptPayload(byte[] plain, out byte[] nonce)
    {
        nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_encKey);
        aes.Encrypt(nonce, plain, cipher, tag);
        var combined = new byte[cipher.Length + tag.Length];
        Buffer.BlockCopy(cipher, 0, combined, 0, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, cipher.Length, tag.Length);
        return combined;
    }

    public byte[] DecryptPayload(byte[] combined, byte[] nonce)
    {
        if (combined.Length < 16)
            throw new CryptographicException("Ciphertext too short");

        var cipherLen = combined.Length - 16;
        var cipher = combined.Take(cipherLen).ToArray();
        var tag = combined.Skip(cipherLen).Take(16).ToArray();
        var plain = new byte[cipherLen];
        using var aes = new AesGcm(_encKey);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    public byte[] Sign(byte[] data)
    {
        using var hmac = new HMACSHA256(_authKey);
        return hmac.ComputeHash(data);
    }

    public bool Verify(byte[] data, byte[] signature)
    {
        var expected = Sign(data);
        return CryptographicOperations.FixedTimeEquals(expected, signature);
    }

    public bool CheckReplay(string nonce, DateTimeOffset timestamp)
    {
        var now = DateTimeOffset.UtcNow;
        if (Math.Abs((now - timestamp).TotalMinutes) > LanDefaults.ReplayWindow.TotalMinutes)
            return false;

        lock (_nonceLock)
        {
            if (_usedNonces.Contains(nonce))
                return false;
            _usedNonces.Add(nonce);
            if (_usedNonces.Count > 5000)
                _usedNonces.Clear();
        }
        return true;
    }
}

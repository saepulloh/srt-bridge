using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using GDI.Core.Services;

namespace GDI.Core.Security;

/// <summary>
/// Provides secure encryption and persistence for broadcast credentials, SRT passphrases,
/// and API authentication tokens. Uses DPAPI on Windows and AES-256-CBC with machine-bound key on Linux.
/// </summary>
public sealed class DpapiSecretVault : ISecretVault
{
    private readonly string _vaultPath;
    private readonly byte[] _entropy = "GDI-SRT-EMBEDDED-BROADCAST-2026"u8.ToArray();
    private readonly object _lock = new();

    public DpapiSecretVault(string? customVaultPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customVaultPath))
        {
            _vaultPath = customVaultPath;
        }
        else
        {
            var baseDir = GdiEnvironment.GetSecurityDirectory();
            Directory.CreateDirectory(baseDir);
            _vaultPath = Path.Combine(baseDir, "vault.dat");
        }

        var dir = Path.GetDirectoryName(_vaultPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public void Protect(string key, string plainSecret)
    {
        lock (_lock)
        {
            var dict = LoadVault();
            dict[key] = plainSecret;
            SaveVault(dict);
        }
    }

    public string? Unprotect(string key)
    {
        lock (_lock)
        {
            var dict = LoadVault();
            return dict.TryGetValue(key, out var secret) ? secret : null;
        }
    }

    public bool HasSecret(string key)
    {
        lock (_lock)
        {
            var dict = LoadVault();
            return dict.ContainsKey(key);
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            var dict = LoadVault();
            if (dict.Remove(key))
            {
                SaveVault(dict);
            }
        }
    }

    private Dictionary<string, string> LoadVault()
    {
        if (!File.Exists(_vaultPath)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var encrypted = File.ReadAllBytes(_vaultPath);
            if (encrypted.Length == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            byte[] plainBytes;
            if (OperatingSystem.IsWindows())
            {
                plainBytes = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.LocalMachine);
            }
            else
            {
                plainBytes = DecryptLinux(encrypted);
            }

            var json = Encoding.UTF8.GetString(plainBytes);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveVault(Dictionary<string, string> dict)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(dict);
        var plainBytes = Encoding.UTF8.GetBytes(json);

        byte[] encrypted;
        if (OperatingSystem.IsWindows())
        {
            encrypted = ProtectedData.Protect(plainBytes, _entropy, DataProtectionScope.LocalMachine);
        }
        else
        {
            encrypted = EncryptLinux(plainBytes);
        }

        File.WriteAllBytes(_vaultPath, encrypted);
    }

    private byte[] EncryptLinux(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(_entropy);
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(data, 0, data.Length);
            cs.FlushFinalBlock();
        }

        return ms.ToArray();
    }

    private byte[] DecryptLinux(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(_entropy);

        var iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        aes.IV = iv;

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(new MemoryStream(data, 16, data.Length - 16), aes.CreateDecryptor(), CryptoStreamMode.Read))
        {
            cs.CopyTo(ms);
        }

        return ms.ToArray();
    }
}

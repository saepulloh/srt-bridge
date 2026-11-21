namespace GDI.Core.Security;

public interface ISecretVault
{
    void Protect(string key, string plainSecret);
    string? Unprotect(string key);
    bool HasSecret(string key);
    void Remove(string key);
}

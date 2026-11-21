using System.Text.RegularExpressions;

namespace GDI.Core.Security;

/// <summary>
/// Redacts sensitive information like SRT passphrases, API tokens, and encryption keys
/// so they are never leaked into logs, console output, or unauthenticated API endpoints.
/// </summary>
public static class SecretRedactor
{
    private static readonly Regex PassphraseRegex = new(
        @"(passphrase=)([^&\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TokenHeaderRegex = new(
        @"(X-GDI-Token:\s*)([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string RedactUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        return PassphraseRegex.Replace(url, "$1[REDACTED]");
    }

    public static string RedactHeaders(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers)) return "";
        return TokenHeaderRegex.Replace(headers, "$1[REDACTED]");
    }

    public static string Mask(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return "";
        if (secret.Length <= 4) return "****";
        return $"{secret[..2]}****{secret[^2..]}";
    }
}

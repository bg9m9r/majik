using System.Security.Cryptography;
using System.Text;

namespace Majik.Server.Matches;

/// <summary>Verifies GitHub webhook delivery signatures
/// (<c>X-Hub-Signature-256</c>) against the configured shared secret.</summary>
public static class GitHubWebhookVerifier
{
    /// <summary>Constant-time compare of the X-Hub-Signature-256 header against
    /// HMAC-SHA256(body, secret).</summary>
    public static bool IsValid(string body, string? signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(signatureHeader)) return false;
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = "sha256=" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signatureHeader));
    }
}

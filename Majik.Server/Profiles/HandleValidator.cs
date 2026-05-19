using System.Text.RegularExpressions;

namespace Majik.Server.Profiles;

public enum HandleValidationOutcome
{
    Ok,
    InvalidFormat,
    Reserved,
}

/// <summary>Result of a handle validation. Carries the normalized
/// (lowercased) handle and the display (original casing) handle when
/// the outcome is <see cref="HandleValidationOutcome.Ok"/>.</summary>
public sealed record HandleValidation(
    HandleValidationOutcome Outcome,
    string Normalized = "",
    string Display = "");

/// <summary>Server-authoritative handle validation. Client mirrors the
/// same regex for inline UX but the server rules here are the source of
/// truth.</summary>
public static class HandleValidator
{
    private static readonly Regex HandleRegex =
        new("^[A-Za-z0-9_-]{3,20}$", RegexOptions.Compiled);

    // Returns 409 in the API (intentional misdirection — see spec).
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "system", "bot", "majik",
    };

    public static HandleValidation Validate(string? input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (!HandleRegex.IsMatch(trimmed))
        {
            return new HandleValidation(HandleValidationOutcome.InvalidFormat);
        }

        if (Reserved.Contains(trimmed))
        {
            return new HandleValidation(HandleValidationOutcome.Reserved);
        }

        return new HandleValidation(
            HandleValidationOutcome.Ok,
            Normalized: trimmed.ToLowerInvariant(),
            Display: trimmed);
    }
}

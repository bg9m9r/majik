using System.Text.RegularExpressions;
using Majik.Core.Costs;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 702.62 — parses the printed "Suspend N—[cost]" oracle clause into a
/// <see cref="SuspendAlternativeCost"/>. Standalone helper rather than a
/// full <c>OracleSpellBinder</c> integration: suspend is rare enough
/// (and templated similarly enough) that bots and tests can call this
/// directly when binding a known suspend card. A binder-level hookup
/// (auto-discovery from Scryfall rows + bot probe surfacing the cost) is
/// a follow-up — see <c>RiftBoltFactory</c>'s class doc for the gap.
/// </summary>
public static class SuspendOracleParser
{
    // Matches "Suspend N—{cost}" (em-dash) or "Suspend N-{cost}" (hyphen),
    // case-insensitive. The cost portion is one or more curly-brace mana
    // symbols; the parser hands the raw substring to ManaCost.Parse which
    // is permissive about braces.
    private static readonly Regex Pattern = new(
        @"\bSuspend\s+(?<n>\d+)\s*[—\-]\s*(?<cost>(?:\{[^}]+\})+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Try to extract a suspend cost from <paramref name="oracleText"/>.
    /// Returns null when the clause isn't present or malformed.
    /// </summary>
    public static SuspendAlternativeCost? TryParse(string? oracleText)
    {
        if (string.IsNullOrWhiteSpace(oracleText)) return null;

        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;

        if (!int.TryParse(m.Groups["n"].Value, out var n)) return null;
        if (n < 0) return null;

        var costText = m.Groups["cost"].Value;
        ManaCost cost;
        try { cost = ManaCost.Parse(costText); }
        catch { return null; }

        return new SuspendAlternativeCost(n, cost);
    }
}

using System.Text.RegularExpressions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.CardData;

/// <summary>
/// Parser for the "Enchant X" oracle clause on Aura spells.
///
/// CR 702.5b — "Enchant" is an ability that defines what an Aura can be
/// attached to. The oracle line "Enchant &lt;object&gt;" sets the legal
/// target type for the Aura's cast-time target (CR 303.4a / 303.4c).
///
/// ## What this covers (v1)
///
/// Single-noun forms only:
///   - "Enchant creature"
///   - "Enchant land"
///   - "Enchant artifact"
///   - "Enchant enchantment"
///   - "Enchant planeswalker"
///   - "Enchant permanent"
///
/// Plurals ("Enchant creatures") are accepted defensively but not part of
/// the printed corpus.
///
/// ## What this does NOT cover (deferred)
///
///   - Multi-word restrictions: "Enchant nonbasic land", "Enchant creature
///     you control", "Enchant creature with flying", "Enchant legendary
///     creature", etc. These return null so the caller can fall back to
///     a hand-wired predicate.
///   - "Enchant player" — current Aura target API is
///     <c>Func&lt;Permanent, bool&gt;</c>; player-targeting auras (Curse
///     of …) require a wider engine change. <see cref="NotSupportedException"/>
///     is thrown so the caller is forced to confront the gap rather than
///     silently degrade to "no predicate".
/// </summary>
public static class AuraEnchantClauseParser
{
    // Match the "Enchant <noun>" clause on its own line/segment. We anchor
    // on word boundaries + a trailing terminator (end-of-line, period, or
    // end-of-string) so we don't accidentally swallow continuations like
    // "Enchant creature with flying". The trailing-terminator requirement
    // is what makes "Enchant nonbasic land" return null in v1.
    private static readonly Regex EnchantClause = new(
        @"(?im)^\s*[*_]?\s*Enchant\s+(?<noun>[a-z]+)\s*[*_]?\s*(?:[.\r\n]|$)",
        RegexOptions.Compiled);

    /// <summary>
    /// Given an Aura's oracle text, returns a <see cref="Permanent"/>
    /// legality predicate matching the "Enchant X" clause. Returns null
    /// when no recognised single-noun clause is found (caller should fall
    /// back to a hand-wired predicate).
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown for "Enchant player"
    /// — see class-level remarks.</exception>
    public static Func<Permanent, bool>? ParseTargetPredicate(string? oracleText)
    {
        if (string.IsNullOrWhiteSpace(oracleText)) return null;

        var match = EnchantClause.Match(oracleText);
        if (!match.Success) return null;

        var noun = match.Groups["noun"].Value.ToLowerInvariant();

        // Normalise simple plurals — defensive; not part of the printed
        // corpus but cheap.
        if (noun.EndsWith('s') && noun.Length > 1)
        {
            noun = noun[..^1];
        }

        return noun switch
        {
            "creature" => p => p.HasType(CardType.Creature),
            "land" => p => p.HasType(CardType.Land),
            "artifact" => p => p.HasType(CardType.Artifact),
            "enchantment" => p => p.HasType(CardType.Enchantment),
            "planeswalker" => p => p.HasType(CardType.Planeswalker),
            "permanent" => _ => true,
            "player" => throw new NotSupportedException(
                "Enchant player is not yet supported — Aura target API is " +
                "Func<Permanent, bool>. CR 303.4a — player-targeting auras " +
                "require a wider engine change."),
            _ => null,
        };
    }
}

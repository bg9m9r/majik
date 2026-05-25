using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.ValueObjects;

namespace Majik.Core.Rules;

/// <summary>
/// CR 702.16 helpers — answer "does this permanent have protection from
/// X?". Quality strings on <see cref="ProtectionAbility"/> are matched
/// case-insensitively. Common buckets:
///   - colour: "white" "blue" "black" "red" "green" "all colors"
///   - type: "creatures" "artifacts" "enchantments" "lands"
///           "planeswalkers" "instants" "sorceries"
///   - name: any other free-form string
/// </summary>
public static class Protection
{
    public static bool HasProtectionFromColor(ICard card, ManaColor color)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        var name = color switch
        {
            ManaColor.White => "white",
            ManaColor.Blue => "blue",
            ManaColor.Black => "black",
            ManaColor.Red => "red",
            ManaColor.Green => "green",
            _ => "",
        };
        return Qualities(card).Any(q =>
            q == name || q == "all colors" || q == "everything");
    }

    public static bool HasProtectionFromCardType(ICard card, CardType type)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        var plural = type switch
        {
            CardType.Creature => "creatures",
            CardType.Artifact => "artifacts",
            CardType.Enchantment => "enchantments",
            CardType.Land => "lands",
            CardType.Planeswalker => "planeswalkers",
            CardType.Instant => "instants",
            CardType.Sorcery => "sorceries",
            _ => "",
        };
        return Qualities(card).Any(q => q == plural);
    }

    /// <summary>
    /// CR 702.16 — true if any <see cref="ProtectionAbility"/> on
    /// <paramref name="card"/> matches the resolving
    /// <paramref name="source"/> spell via its
    /// <see cref="ProtectionAbility.SpellPredicate"/> (used by
    /// "protection from coloured spells" / "from multicoloured spells"
    /// shapes where the colour / type / name string surface doesn't
    /// reduce the clause to a single token). Plain colour / type / name
    /// protections without a predicate are skipped here — callers fall
    /// back to <see cref="HasProtectionFromColor"/> /
    /// <see cref="HasProtectionFromCardType"/> for those.
    /// </summary>
    public static bool HasProtectionFromSpell(ICard card, Majik.Core.Spells.ISpell source)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (source == null) throw new ArgumentNullException(nameof(source));
        foreach (var p in card.Abilities.OfType<ProtectionAbility>())
        {
            // Skip conditional clauses whose gate is currently false
            // (Etched Champion outside Metalcraft).
            if (!p.IsCurrentlyActive) continue;
            if (p.SpellPredicate is { } pred && pred(source)) return true;
        }
        return false;
    }

    /// <summary>
    /// Active-only qualities scan — conditional protection clauses
    /// (e.g. Etched Champion's Metalcraft rider) whose
    /// <see cref="ProtectionAbility.IsActive"/> closure currently returns
    /// false are filtered out, so the colour / type lookups behave as if
    /// the clause is not in effect. Always-on protections (IsActive
    /// null) are unaffected.
    /// </summary>
    private static IEnumerable<string> Qualities(ICard card) =>
        card.Abilities.OfType<ProtectionAbility>()
            .Where(p => p.IsCurrentlyActive)
            .Select(p => p.Quality);
}

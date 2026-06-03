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
    /// CR 702.16 — protection from a creature <em>subtype</em>
    /// (Baneslayer Angel — "protection from Demons and from Dragons";
    /// CR 205.3 / CR 702.16). True if any active
    /// <see cref="ProtectionAbility"/> on <paramref name="card"/> names a
    /// subtype the <paramref name="source"/> currently has.
    ///
    /// CR 613.1d — the source's subtypes can be changed by a Layer-4
    /// type-changing effect; a <see cref="Permanent"/> source therefore reads
    /// its <em>effective</em> subtype set (so a creature animated into a Dragon
    /// is protected against, and one whose Dragon type is removed is not). A
    /// non-permanent source (an instant/sorcery with a creature subtype — rare,
    /// but legal) falls back to its printed subtypes.
    ///
    /// Quality matching is by enum name, case-insensitively, tolerant of the
    /// printed plural form ("Demons" → <see cref="CardSubtype.Demon"/>). Only
    /// protection qualities that resolve to a known <see cref="CardSubtype"/>
    /// participate — colour / card-type / name qualities are skipped here and
    /// handled by their dedicated helpers.
    /// </summary>
    public static bool HasProtectionFromSubtype(ICard card, ICard source)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (source == null) throw new ArgumentNullException(nameof(source));

        var sourceSubtypes = source is Permanent perm
            ? perm.GetEffectiveSubtypes()
            : (IReadOnlySet<CardSubtype>)source.Subtypes.ToHashSet();
        if (sourceSubtypes.Count == 0) return false;

        foreach (var quality in Qualities(card))
        {
            if (TryParseSubtypeQuality(quality, out var subtype)
                && sourceSubtypes.Contains(subtype))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Maps a normalised protection-quality token to a <see cref="CardSubtype"/>.
    /// Accepts the printed plural form ("demons", "dragons", "zombies",
    /// "elves") by stripping a trailing "es"/"s"; the enum names are the
    /// canonical singular ("Demon", "Dragon"). Returns false for tokens that
    /// don't name a creature subtype (colours, card types, names).
    /// </summary>
    private static bool TryParseSubtypeQuality(string quality, out CardSubtype subtype)
    {
        subtype = default;
        if (string.IsNullOrWhiteSpace(quality)) return false;

        // Exact singular match first (Enum.TryParse is case-insensitive).
        if (Enum.TryParse(quality, ignoreCase: true, out subtype)
            && Enum.IsDefined(subtype))
        {
            return true;
        }

        // Depluralise the printed form: "dragons" -> "dragon",
        // "zombies" -> "zombie", "elves" -> not handled (no irregulars in the
        // covered set). Try "-es" then "-s".
        string singular = quality;
        if (quality.EndsWith("ies", StringComparison.Ordinal))
        {
            singular = quality[..^3] + "y"; // "faeries" -> "faery" (no enum); harmless
        }
        else if (quality.EndsWith("s", StringComparison.Ordinal))
        {
            singular = quality[..^1];
        }

        return Enum.TryParse(singular, ignoreCase: true, out subtype)
            && Enum.IsDefined(subtype);
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

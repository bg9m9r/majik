using Majik.Core.Abilities;
using Majik.Core.Cards;

namespace Majik.Core.CardData;

/// <summary>
/// CR 702.33 / 702.32 — predicate for "a card with a kicker ability".
///
/// <para>
/// Several cards reference the printed-keyword set "a card with a kicker
/// ability" (e.g. Murasa Sproutling's ETB return, or "you may put a card
/// with a kicker ability …" effects). CR 109.3 / 702.33 — a card "has a
/// kicker ability" iff its printed rules text contains a Kicker (CR 702.33)
/// or Multikicker (CR 702.32) ability. This applies in any zone the card
/// is observed in (graveyard, hand, exile), so the test reads the card's
/// printed keyword markers rather than any cast-time "was kicked" sentinel.
/// </para>
///
/// <para>
/// The engine marks a card's printed kicker ability with a
/// <see cref="KeywordAbility"/> "Kicker" / "Multikicker" marker — attached
/// via <see cref="Majik.Core.CardData.Definitions.CardDefBuilder.WithKeyword"/>
/// (e.g. Vines of Vastwood, Burst Lightning) or directly by a named factory.
/// This is the same observable marker <see cref="Creature.HasEffectiveKeyword"/>
/// reads for evergreen keywords, so the kicker-ability predicate slots onto
/// the existing keyword-marker seam — no new card metadata surface.
/// </para>
///
/// <para>
/// Note: this reads <em>printed</em> markers, NOT
/// <see cref="Card.WasKicked"/>. "A card with a kicker ability" is about the
/// card's identity (does it print Kicker), independent of whether any cast
/// of it actually paid the kicker. A card can have a kicker ability and not
/// have been kicked, and vice versa is impossible.
/// </para>
/// </summary>
public static class KickerAbilityDetector
{
    /// <summary>CR 702.33 — the Kicker keyword-marker name.</summary>
    public const string KickerKeyword = "Kicker";

    /// <summary>CR 702.32 — the Multikicker keyword-marker name (a card
    /// with multikicker also "has a kicker ability" per CR 702.32a).</summary>
    public const string MultikickerKeyword = "Multikicker";

    /// <summary>
    /// True iff <paramref name="card"/> has a printed kicker ability —
    /// it carries a <see cref="KeywordAbility"/> marker whose keyword is
    /// "Kicker" or "Multikicker" (case-insensitive, CR 702.32a / 702.33).
    /// Null card → false.
    /// </summary>
    public static bool HasKickerAbility(ICard? card)
    {
        if (card is not Card concrete) return false;

        return concrete.Abilities
            .OfType<KeywordAbility>()
            .Any(k =>
                string.Equals(k.Keyword, KickerKeyword, StringComparison.OrdinalIgnoreCase)
                || string.Equals(k.Keyword, MultikickerKeyword, StringComparison.OrdinalIgnoreCase));
    }
}

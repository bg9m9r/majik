using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.74 — Evoke. "You may cast this spell for its evoke cost. If you do,
/// it's sacrificed when it enters the battlefield."
///
/// Casting via this alternative cost:
///   1. Replaces the spell's printed mana cost with <see cref="AlternativeManaCost"/>.
///   2. Optionally requires exiling a card of a given color from hand (used by
///      the Modern Horizons 2 incarnation cycle — Solitude, Endurance, Fury,
///      Grief, Subtlety — whose evoke cost is "exile a [color] card from your
///      hand"). When <see cref="PitchColor"/> is non-null, <see cref="ExiledCard"/>
///      must be supplied and is exiled by <see cref="OnResolved"/>.
///   3. On resolution, sets <see cref="Creature.EvokeWasPaid"/> on the spell's
///      Creature so the printed "When ~ enters, if evoke was paid, sacrifice it"
///      trigger (CR 702.74b) can fire after the card enters the battlefield.
///
/// The "sacrifice when it enters" trigger itself is NOT registered by this
/// cost — it's a printed triggered ability on the creature (see
/// <see cref="Majik.Core.Keywords.EvokeFactory"/>). The cost only flips the
/// flag that the intervening-if reads.
/// </summary>
public sealed class EvokeAlternativeCost : IAlternativeCost
{
    /// <summary>Mana portion of the evoke cost. May be
    /// <see cref="ManaCost.Zero"/> when the entire evoke cost is "exile a
    /// card" (e.g. Solitude — "Evoke—Exile a white card from your hand").</summary>
    public ManaCost AlternativeManaCost { get; }

    /// <summary>When non-null, the evoke cost also requires exiling a card of
    /// this color from the caster's hand (CR 117.11 + CR 701.21). Null when
    /// the evoke cost is purely mana (e.g. classic Lorwyn evokers like Mulldrifter:
    /// "Evoke {3}{U}").</summary>
    public ManaColor? PitchColor { get; }

    /// <summary>The card the caster chose to exile to pay the pitch portion
    /// of the evoke cost. Required iff <see cref="PitchColor"/> is set.</summary>
    public ICard? ExiledCard { get; }

    public string Description =>
        PitchColor.HasValue
            ? (AlternativeManaCost.IsZero
                ? $"Evoke — Exile a {PitchColor} card from your hand"
                : $"Evoke {AlternativeManaCost}, Exile a {PitchColor} card from your hand")
            : $"Evoke {AlternativeManaCost}";

    /// <summary>Pure-mana evoke (e.g. "Evoke {3}{U}"). No card-exile component.</summary>
    public EvokeAlternativeCost(ManaCost evokeManaCost)
    {
        AlternativeManaCost = evokeManaCost ?? throw new ArgumentNullException(nameof(evokeManaCost));
        PitchColor = null;
        ExiledCard = null;
    }

    /// <summary>Pitch-style evoke (CR 702.74 + CR 117.11). The caller supplies
    /// the color requirement and the card chosen for pitch. <paramref name="evokeManaCost"/>
    /// may be <see cref="ManaCost.Zero"/> (Solitude-style) or non-zero
    /// (hypothetical "Evoke {1}{W}, exile a white card" variants).</summary>
    public EvokeAlternativeCost(ManaCost evokeManaCost, ManaColor pitchColor, ICard exiledCard)
    {
        AlternativeManaCost = evokeManaCost ?? throw new ArgumentNullException(nameof(evokeManaCost));
        PitchColor = pitchColor;
        ExiledCard = exiledCard ?? throw new ArgumentNullException(nameof(exiledCard));
    }

    /// <summary>
    /// Evoke is announced at the same step as the normal cast (CR 601.2b),
    /// so the spell's card must still be in the caster's hand. When a pitch
    /// component is required, the chosen <see cref="ExiledCard"/> must also
    /// be a hand-resident card of the right color owned by the caster.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        // Spell card itself must be in hand (CR 601.2 / CR 702.74).
        if (card.Zone != ZoneType.Hand) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;

        // Pitch validation (mirrors ExileColoredCardAlternativeCost).
        if (PitchColor.HasValue)
        {
            if (ExiledCard is null) return false;
            if (!ReferenceEquals(ExiledCard.Owner, caster)) return false;
            if (ExiledCard.Zone != ZoneType.Hand) return false;
            if (ReferenceEquals(ExiledCard, card)) return false; // can't pitch the spell itself
            if (!CardColors.GetColors(ExiledCard).Contains(PitchColor.Value)) return false;
        }

        return true;
    }

    /// <summary>
    /// Two side-effects on resolution:
    ///   (1) Flip <see cref="Creature.EvokeWasPaid"/> so the printed evoke
    ///       sacrifice trigger (CR 702.74b) has a true intervening-if when it
    ///       evaluates after the card enters the battlefield.
    ///   (2) Exile the pitched card (if any). Idempotent — safe if the pitched
    ///       card has already moved elsewhere.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (card is Creature creature)
        {
            creature.EvokeWasPaid = true;
        }

        if (PitchColor.HasValue && ExiledCard != null && ExiledCard.Zone == ZoneType.Hand)
        {
            caster.Zones.Hand.RemoveCard(ExiledCard);
            caster.Zones.Exile.AddCard(ExiledCard);
            ExiledCard.SetZone(ZoneType.Exile);
        }
    }
}

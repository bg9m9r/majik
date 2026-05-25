using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Exile two [color] cards from your hand rather than pay this spell's
/// mana cost." Used by the Coldsnap pitch cycle (Soul Spike, Surging Sentinels
/// and friends): a single-card pitch isn't enough, the printed cost demands
/// exactly two same-colored cards.
///
/// CR 117.11 (alternative costs) + CR 701.21 (exile a card).
///
/// Differences vs. <see cref="ExileColoredCardAlternativeCost"/>:
///   * Two cards instead of one.
///   * Both cards must satisfy the colour predicate.
///   * The two cards must be distinct references AND distinct from the
///     spell being cast (CR 601.2b — the spell itself isn't a legal pitch
///     candidate, and "two cards" means two physically different cards).
///
/// Differences vs. <see cref="PitchAlternativeCost"/>:
///   * No "if it's not your turn" timing restriction (Coldsnap-cycle pitch
///     spells are legal any time, unlike the Force cycle).
///   * No life rider — the exile of two cards IS the entire cost.
///
/// Casting flow:
///   1. Player selects two distinct colour-matching cards from their hand.
///   2. <see cref="CanCastFor"/> validates ownership, zone, colour, and
///      distinctness for both cards.
///   3. <see cref="AlternativeManaCost"/> is <see cref="ManaCost.Zero"/> —
///      no mana is paid; the exile is the entire cost.
///   4. <see cref="OnResolved"/> exiles both pitched cards (idempotent —
///      safe if either pitched card has already moved elsewhere).
/// </summary>
public sealed class ExileTwoColoredCardsAlternativeCost : IAlternativeCost
{
    /// <summary>The required colour both pitched cards must share.</summary>
    public ManaColor RequiredColor { get; }

    /// <summary>First pitched card. Must be in caster's hand and the
    /// required colour at announce time.</summary>
    public ICard FirstExiledCard { get; }

    /// <summary>Second pitched card. Must be in caster's hand, the required
    /// colour, and a distinct reference from <see cref="FirstExiledCard"/>.</summary>
    public ICard SecondExiledCard { get; }

    public string Description =>
        $"Exile two {RequiredColor} cards from your hand";

    /// <summary>No mana is paid — the exile of two cards is the entire cost
    /// (CR 117.11).</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public ExileTwoColoredCardsAlternativeCost(
        ManaColor requiredColor,
        ICard firstExiledCard,
        ICard secondExiledCard)
    {
        RequiredColor = requiredColor;
        FirstExiledCard = firstExiledCard
            ?? throw new ArgumentNullException(nameof(firstExiledCard));
        SecondExiledCard = secondExiledCard
            ?? throw new ArgumentNullException(nameof(secondExiledCard));
    }

    /// <summary>
    /// Both pitched cards must be:
    ///   • owned by the caster,
    ///   • currently in the caster's hand (CR 601.2 — legal pitch
    ///     candidates at announce time),
    ///   • the required colour (CR 105 — colour derived from mana cost),
    ///   • distinct from the spell being cast, and
    ///   • distinct from each other.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (ReferenceEquals(FirstExiledCard, SecondExiledCard)) return false;
        if (ReferenceEquals(FirstExiledCard, card)) return false;
        if (ReferenceEquals(SecondExiledCard, card)) return false;

        if (!IsLegalPitchCandidate(FirstExiledCard, caster)) return false;
        if (!IsLegalPitchCandidate(SecondExiledCard, caster)) return false;

        return true;
    }

    private bool IsLegalPitchCandidate(ICard pitch, Player caster)
    {
        if (!ReferenceEquals(pitch.Owner, caster)) return false;
        if (pitch.Zone != ZoneType.Hand) return false;
        return CardColors.GetColors(pitch).Contains(RequiredColor);
    }

    /// <summary>
    /// Exile both pitched cards after resolution (CR 701.21). Idempotent —
    /// each card is moved only if still in the caster's hand at OnResolved
    /// time. Safe against rare interactions that may have already moved the
    /// pitched cards elsewhere between cast and resolve.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        ExileIfStillInHand(FirstExiledCard, caster);
        ExileIfStillInHand(SecondExiledCard, caster);
    }

    private static void ExileIfStillInHand(ICard pitch, Player caster)
    {
        if (pitch.Zone != ZoneType.Hand) return;
        caster.Zones.Hand.RemoveCard(pitch);
        caster.Zones.Exile.AddCard(pitch);
        pitch.SetZone(ZoneType.Exile);
    }
}

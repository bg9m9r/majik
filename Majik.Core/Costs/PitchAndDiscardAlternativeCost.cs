using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Exile a [color] card from your hand and discard a card rather than pay
/// this spell's mana cost." — the Foil / Commandeer / Misdirection (with
/// rider) family alternative cost (CR 117.11 + CR 701.21 + CR 701.16a).
///
/// Foil (Prophecy, {1}{U}{U}) prints:
///   "You may exile a blue card from your hand and discard a card rather
///    than pay this spell's mana cost."
///
/// Differences vs. <see cref="ExileColoredCardAlternativeCost"/>:
///   * Additionally discards a second hand card on resolve (CR 701.16a).
///   * Both the pitched card AND the discard pick must be distinct hand
///     cards (and neither may be the spell being cast — CR 601.2 only
///     allows hand-zone picks; the spell itself moves to the stack first).
///   * <see cref="AlternativeManaCost"/> is <see cref="ManaCost.Zero"/> —
///     the exile + discard is the entire cost; no mana paid.
///
/// Foil prints no "if it's not your turn" timing gate, so this primitive
/// (like <see cref="ExileColoredCardAlternativeCost"/>) has no context
/// predicate.
///
/// ## v1 notes
/// - The <see cref="DiscardedCard"/> is supplied at construction time —
///   same posture as <see cref="ExileColoredCardAlternativeCost.ExiledCard"/>.
///   A bot probe that enumerates (exile, discard) pairs is deferred until
///   the bot shows it cares about Foil at the EV layer (mirrors Snapback /
///   Pyrokinesis / Soul Spike — see <c>PitchAltCostProbe.DefaultLookup</c>).
/// </summary>
public sealed class PitchAndDiscardAlternativeCost : IAlternativeCost
{
    /// <summary>The required color of the exiled hand card (Blue for Foil).</summary>
    public ManaColor RequiredColor { get; }

    /// <summary>The card the caster chose to pitch (exile).</summary>
    public ICard ExiledCard { get; }

    /// <summary>The card the caster chose to discard.</summary>
    public ICard DiscardedCard { get; }

    public string Description =>
        $"Exile a {RequiredColor} card from your hand and discard a card";

    /// <summary>No mana is paid — the exile + discard is the entire cost
    /// (CR 117.11).</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public PitchAndDiscardAlternativeCost(
        ManaColor requiredColor,
        ICard exiledCard,
        ICard discardedCard)
    {
        ArgumentNullException.ThrowIfNull(exiledCard);
        ArgumentNullException.ThrowIfNull(discardedCard);
        if (ReferenceEquals(exiledCard, discardedCard))
        {
            throw new ArgumentException(
                "Exiled and discarded card must be distinct hand cards.",
                nameof(discardedCard));
        }
        RequiredColor = requiredColor;
        ExiledCard = exiledCard;
        DiscardedCard = discardedCard;
    }

    /// <summary>
    /// Both picks must be:
    ///   • owned by the caster,
    ///   • currently in the caster's hand,
    /// and the exile pick must additionally be of the required color
    /// (CR 105 — color derived from mana cost). The discard pick can be
    /// any card. Neither may be the spell being cast.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (!ReferenceEquals(ExiledCard.Owner, caster)) return false;
        if (!ReferenceEquals(DiscardedCard.Owner, caster)) return false;
        if (ExiledCard.Zone != ZoneType.Hand) return false;
        if (DiscardedCard.Zone != ZoneType.Hand) return false;
        if (ReferenceEquals(ExiledCard, card)) return false;
        if (ReferenceEquals(DiscardedCard, card)) return false;
        if (ReferenceEquals(ExiledCard, DiscardedCard)) return false;
        if (!CardColors.GetColors(ExiledCard).Contains(RequiredColor)) return false;
        return true;
    }

    /// <summary>
    /// Apply the cost side-effect after resolution: exile the pitched card
    /// (CR 701.21) and discard the discard pick (CR 701.16a — hand →
    /// graveyard). Idempotent — safe if either card has already moved.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (ExiledCard.Zone == ZoneType.Hand)
        {
            caster.Zones.Hand.RemoveCard(ExiledCard);
            caster.Zones.Exile.AddCard(ExiledCard);
            ExiledCard.SetZone(ZoneType.Exile);
        }
        if (DiscardedCard.Zone == ZoneType.Hand)
        {
            caster.Zones.Hand.RemoveCard(DiscardedCard);
            caster.Zones.Graveyard.AddCard(DiscardedCard);
            // Zone.AddCard sets card.Zone — no manual SetZone needed.
        }
    }
}

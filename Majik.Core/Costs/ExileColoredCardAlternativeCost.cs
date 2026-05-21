using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Exile a [color] card from your hand rather than pay this spell's mana
/// cost." Used by pitch spells (Force of Vigor, Force of Will, Force of
/// Negation, Force of Despair, etc.).
///
/// CR 117.11 (alternative costs) + CR 701.21 (exile a card).
///
/// Casting flow:
///   1. Player selects a card of the required color from their hand
///      (the <paramref name="exiledCard"/> constructor argument).
///   2. <see cref="CanCastFor"/> confirms the card is in the caster's hand
///      and has the required color.
///   3. <see cref="AlternativeManaCost"/> is <see cref="ManaCost.Zero"/> —
///      the exile IS the cost; no mana is paid.
///   4. <see cref="OnResolved"/> exiles the pitched card.
///
/// v1 note: Force of Vigor's "if it's not your turn" timing restriction is
/// not enforced here — <see cref="SpellCastFlow"/> would need a timing-check
/// hook to wire that up. Track separately when SpellCastFlow gains
/// opponent-turn context checks.
/// </summary>
public sealed class ExileColoredCardAlternativeCost : IAlternativeCost
{
    /// <summary>The required color, expressed as a <see cref="ManaColor"/>
    /// (e.g. <see cref="ManaColor.Green"/> for Force of Vigor).</summary>
    public ManaColor RequiredColor { get; }

    /// <summary>The card the caster chose to pitch.</summary>
    public ICard ExiledCard { get; }

    public string Description => $"Exile a {RequiredColor} card from your hand";

    /// <summary>No mana is paid — the exile is the entire cost (CR 117.11).</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public ExileColoredCardAlternativeCost(ManaColor requiredColor, ICard exiledCard)
    {
        RequiredColor = requiredColor;
        ExiledCard = exiledCard ?? throw new ArgumentNullException(nameof(exiledCard));
    }

    /// <summary>
    /// The pitched card must be:
    ///   • owned by the caster,
    ///   • currently in the caster's hand (CR 601.2 — the card is a legal
    ///     pitch candidate at announce time), and
    ///   • the required color (CR 105 — color derived from mana cost).
    /// The <paramref name="card"/> parameter (the spell being cast) is
    /// intentionally unused — pitch spells impose no zone restriction on
    /// themselves beyond the default hand rule enforced elsewhere.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (!ReferenceEquals(ExiledCard.Owner, caster)) return false;
        if (ExiledCard.Zone != ZoneType.Hand) return false;
        return CardColors.GetColors(ExiledCard).Contains(RequiredColor);
    }

    /// <summary>
    /// Exile the pitched card after resolution (CR 701.21).
    /// The pitched card has been sitting in the caster's hand on the stack;
    /// move it to exile now that the spell has resolved.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (ExiledCard.Zone == ZoneType.Hand)
        {
            caster.Zones.Hand.RemoveCard(ExiledCard);
        }
        caster.Zones.Exile.AddCard(ExiledCard);
        ExiledCard.SetZone(ZoneType.Exile);
    }
}

using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// Shoal-cycle alternative cost (Nourishing Shoal, Disrupting Shoal, …):
///
///   "You may exile a green card with mana value X from your hand rather
///    than pay this spell's mana cost."
///
/// CR 118.9 (alternative cost) + CR 701.21 (exile).
///
/// Casting flow:
///   1. Caller (or agent) chooses X for the spell.
///   2. Caller picks a green card from hand whose MV equals X and
///      constructs <see cref="ExileGreenMVXAlternativeCost"/>
///      (<paramref name="requiredManaValue"/>, <paramref name="exiledCard"/>).
///   3. <see cref="CanCastFor"/> confirms the card is in hand, is green,
///      and has <c>ManaCostValue.TotalValue == RequiredManaValue</c>.
///   4. <see cref="AlternativeManaCost"/> is <see cref="ManaCost.Zero"/> —
///      the exile IS the entire cost; no mana is paid (CR 118.9).
///      The cast flow still adds the declared X as generic mana as usual
///      (CR 202.3b), but because <see cref="AlternativeManaCost"/> is
///      Zero, the only mana actually owed is the X generic portion, which
///      is also waived here (X is paid "by exiling the card", not with
///      mana).  Callers must pass X = 0 to the mana prompt or use
///      <see cref="ManaPayment.Empty"/> — see factory xmldoc for the
///      full wiring note.
///   5. <see cref="OnResolved"/> exiles the chosen hand card (CR 701.21).
///
/// Colour note: Nourishing Shoal is green; the cycle generalises via
/// <see cref="RequiredColor"/>, but the current engine wires only the
/// green variant. Future Shoal siblings (Disrupting Shoal = blue, etc.)
/// can reuse this class with the appropriate <see cref="ManaColor"/>.
/// </summary>
public sealed class ExileGreenMVXAlternativeCost : IAlternativeCost
{
    /// <summary>The required color of the exiled card (green for Nourishing
    /// Shoal; other colors for cycle siblings).</summary>
    public ManaColor RequiredColor { get; }

    /// <summary>The mana value the exiled card must equal — set to the
    /// declared X at cast time (CR 107.3b).</summary>
    public int RequiredManaValue { get; }

    /// <summary>The card the caster chose to exile.</summary>
    public ICard ExiledCard { get; }

    public string Description =>
        $"Exile a {RequiredColor} card with mana value {RequiredManaValue} from your hand";

    /// <summary>No mana is paid — the exile is the entire cost (CR 118.9).
    /// The cast flow will still add the generic X portion on top of Zero;
    /// callers must wire X = 0 to the mana-payment prompt when this
    /// alt-cost is active so the resulting ManaCost (0 + X = X generic)
    /// is covered by the exile rather than by tapping mana sources.</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public ExileGreenMVXAlternativeCost(ManaColor requiredColor, int requiredManaValue, ICard exiledCard)
    {
        if (requiredManaValue < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredManaValue));
        RequiredColor = requiredColor;
        RequiredManaValue = requiredManaValue;
        ExiledCard = exiledCard ?? throw new ArgumentNullException(nameof(exiledCard));
    }

    /// <summary>
    /// The pitched card must:
    ///   • be owned by the caster (CR 601.2 — must be the caster's hand),
    ///   • be currently in the caster's hand (CR 601.2b),
    ///   • be the required color (CR 105 — color from printed mana cost), and
    ///   • have mana value equal to the declared X (CR 107.3b / Shoal oracle).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (!ReferenceEquals(ExiledCard.Owner, caster)) return false;
        if (ExiledCard.Zone != ZoneType.Hand) return false;
        if (ReferenceEquals(ExiledCard, card)) return false;
        if (!CardColors.GetColors(ExiledCard).Contains(RequiredColor)) return false;
        if (ExiledCard is not Card concreteCard) return false;
        return concreteCard.ManaCostValue.TotalValue == RequiredManaValue;
    }

    /// <summary>
    /// Exile the chosen card from hand after the spell resolves (CR 701.21).
    /// Idempotent — safe if the card has already moved elsewhere.
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

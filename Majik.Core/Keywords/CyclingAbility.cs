using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.31 — "{cost}, Discard this card: Draw a card."
/// Activated ability of the card itself; can be activated only while
/// the card is in its owner's hand. Mana-cost variant simplified to a
/// mana cost; the discard is handled here.
///
/// This MVP is "self-contained" — it doesn't go on the stack and resolves
/// immediately. Full Cycling is an activated ability that uses the stack
/// (CR 602); upgrade once activated-ability flow lands in Phase 15.
/// </summary>
public sealed class CyclingAbility : IAbility
{
    public ICard Source { get; }
    public ManaCost Cost { get; }

    public CyclingAbility(ICard source, ManaCost cost)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Cost = cost ?? throw new ArgumentNullException(nameof(cost));
    }

    /// <summary>
    /// Pay the cycling cost, discard the card, draw one. Returns true if
    /// the activation succeeded.
    /// </summary>
    public bool Activate(Player controller)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (Source.Zone != ZoneType.Hand) return false;

        if (!controller.PayMana(Cost)) return false;

        // Discard self.
        controller.Zones.Hand.RemoveCard(Source);
        controller.Zones.Graveyard.AddCard(Source);
        Source.SetZone(ZoneType.Graveyard);

        // Draw one.
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            controller.TriedToDrawFromEmptyLibrary = true;
            return true;
        }
        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
        return true;
    }
}

using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wrenn and Six (Modern Horizons, {R}{G}).
///
/// Legendary Planeswalker — Wrenn, starting loyalty 3.
/// Oracle text:
///   "+1: Return up to one target land card from your graveyard to your hand.
///    −1: Lands you control gain reach and '{T}: This land deals 1 damage to
///         any target' until end of turn.
///    −7: You get an emblem with 'Instant and sorcery cards in your graveyard
///         have retrace.'"
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 3, Wrenn subtype, mana cost {R}{G}.
/// - <b>+1</b>: returns the first land card in controller's graveyard to
///   hand. v1 auto-pick — LoyaltyAbility doesn't yet declare
///   <see cref="TargetRequest"/>s. If no land is in the graveyard the
///   effect no-ops (still legal — "up to one").
/// - <b>-7 emblem</b>: creates an <see cref="Emblem"/> in the controller's
///   command zone with a placeholder "retrace" ability marker. The
///   emblem's ability is a no-op static; the engine doesn't yet model
///   retrace as a runtime cost-replacement, so the emblem is structural
///   only (it shows up in <see cref="Player.Emblems"/> for log/UI).
///
/// ## Deferred (v1 gaps)
/// - <b>-1 lands-gain-reach-and-ping</b>: requires granting both a keyword
///   (Reach on a non-creature) and a new activated ability to a batch of
///   permanents until end of turn. The engine doesn't yet have an
///   "until end of turn — grant ability to controlled lands" continuous-
///   effect primitive. v1 wires the loyalty cost with a no-op body so the
///   loyalty change still applies.
/// - <b>Retrace runtime</b>: the -7 emblem records its source but the
///   actual retrace cost-replacement (CR 702.81 — discard a land card as
///   an additional cost to cast an instant or sorcery from the graveyard)
///   isn't wired. The emblem is shape-only.
/// - <b>Targeting prompt</b>: +1 picks the first land in graveyard
///   deterministically rather than via the agent.
/// </summary>
public static class WrennAndSixFactory
{
    /// <summary>
    /// Construct Wrenn and Six. The +1 lands-from-graveyard effect operates
    /// purely on the owner's graveyard so no resolver is needed. -7 creates
    /// an emblem in the owner's command zone (CR 114).
    /// </summary>
    public static Planeswalker Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var wrenn = new Planeswalker(
            name: "Wrenn and Six",
            manaCost: "{R}{G}",
            startingLoyalty: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Wrenn });

        wrenn.SetOwner(owner);
        wrenn.SetController(owner);

        // -- +1: Return up to one target land card from your graveyard to
        //        your hand. v1 auto-picks the first land in the graveyard.
        wrenn.AddAbility(new LoyaltyAbility(wrenn, +1, () =>
        {
            var pick = owner.Zones.Graveyard.GetCards()
                .FirstOrDefault(c => c.HasType(CardType.Land));
            if (pick == null) return; // "up to one" — empty is legal
            owner.Zones.Graveyard.RemoveCard(pick);
            owner.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
        }));

        // -- -1: Lands you control gain reach and "{T}: This land deals 1
        //        damage to any target" until end of turn. v1: no-op body
        //        (no until-EOT batch-grant primitive yet).
        wrenn.AddAbility(new LoyaltyAbility(wrenn, -1, () => { /* deferred */ }));

        // -- -7 ultimate: emblem with retrace-grant. v1: structural emblem
        //        only — the retrace cost-replacement isn't wired.
        wrenn.AddAbility(new LoyaltyAbility(wrenn, -7, () =>
        {
            var emblem = new Emblem(
                controller: owner,
                sourceName: "Wrenn and Six — retrace emblem",
                abilities: Array.Empty<IAbility>());
            owner.AddEmblem(emblem);
        }));

        return wrenn;
    }
}

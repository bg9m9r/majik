using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dredger's Insight (Duskmourn).
///
/// Enchantment — {1}{G}. Oracle text:
///   "Whenever one or more artifact and/or creature cards leave your graveyard,
///    you gain 1 life.
///    When this enchantment enters, mill four cards. You may put an artifact,
///    creature, or land card from among the milled cards into your hand."
///
/// ## Implemented (v1)
/// - ETB trigger: mill 4, auto-pick first artifact/creature/land card among the
///   milled cards → caster's hand.
/// - Lifegain trigger: whenever an artifact or creature card leaves the
///   controller's graveyard (any destination), gain 1 life.
///   Wired via <see cref="EventTriggerCondition{CardMovedEvent}"/> on
///   <c>FromZone == Graveyard</c> + type filter.
///
/// ## Deferred (v1 gaps)
/// - "You may put …" is optional — v1 always picks if a qualifying card is
///   present (opt-out awaits agent prompt system).
/// - The lifegain trigger groups multiple simultaneous leavers into one
///   trigger event per the oracle text ("one or more … leave"). v1 fires once
///   per individual card move; over-counting is possible if multiple cards
///   leave simultaneously. Batching awaits a zone-change batch event.
/// - Active zone check: the trigger is active only while Dredger's Insight is
///   on the battlefield (default <see cref="TriggeredAbility.ActiveZones"/>).
/// </summary>
public static class DredgersInsightFactory
{
    /// <summary>
    /// Construct Dredger's Insight owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var enchant = new Enchantment("Dredger's Insight", "1G");
        enchant.SetOwner(owner);
        enchant.SetController(owner);

        // --------------------------------------------------------------------
        // ETB trigger: mill 4, may put first artifact/creature/land to hand.
        // CR 701.13 — mill N. CR 603 — ETB triggered ability.
        // --------------------------------------------------------------------
        var etbEffect = new Effect(
            "Dredger's Insight: mill 4, pick a/c/l",
            () =>
            {
                var milled = MillAction.Apply(owner, 4);
                var pick = milled.FirstOrDefault(c =>
                    c.HasType(CardType.Artifact) ||
                    c.HasType(CardType.Creature) ||
                    c.HasType(CardType.Land));

                if (pick != null)
                {
                    // Move from graveyard to hand.
                    owner.Zones.Graveyard.RemoveCard(pick);
                    owner.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: enchant,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(enchant),
            effects: new IEffect[] { etbEffect });

        enchant.AddAbility(etbTrigger);

        // --------------------------------------------------------------------
        // Lifegain trigger: whenever an artifact or creature card leaves
        // the controller's graveyard (to any zone), gain 1 life.
        // CR 603.1: triggered abilities use "when/whenever/at".
        // CR 700.4: a card "leaves" a zone when a CardMovedEvent fires with
        //   FromZone == that zone.
        // --------------------------------------------------------------------
        var lifegainEffect = new Effect(
            "Dredger's Insight: gain 1 life",
            () => owner.GainLife(1));

        var lifegainTrigger = new TriggeredAbility(
            source: enchant,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.FromZone == ZoneType.Graveyard
                && ReferenceEquals(e.Card.Owner, owner)
                && (e.Card.HasType(CardType.Artifact) || e.Card.HasType(CardType.Creature))),
            effects: new IEffect[] { lifegainEffect });

        enchant.AddAbility(lifegainTrigger);

        return enchant;
    }
}

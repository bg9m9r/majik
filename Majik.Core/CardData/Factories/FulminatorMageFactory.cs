using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fulminator Mage (Shadowmoor, {B/R}{B/R}).
///
/// Creature — Elemental Shaman 2/2. Oracle text:
///   "Sacrifice Fulminator Mage: Destroy target nonbasic land."
///
/// ## Implemented (v1)
/// - 2/2 Elemental Shaman with hybrid mana cost {B/R}{B/R}. Cost string
///   follows the same hybrid-pip format the engine already parses for
///   Boros Reckoner ("{R/W}{R/W}{R/W}") — `ManaCostValue` derives colour
///   identity (B + R) from the pip set.
/// - <b>{Sacrifice Fulminator Mage}: Destroy target nonbasic land</b> —
///   wired as an <see cref="ActivatedAbility"/> with no mana / tap cost.
///   A <see cref="TargetRequest"/> declares a 1..1 "target nonbasic land"
///   slot so the activating player's agent picks a nonbasic land at
///   activation time (CR 602.2b). The self-sacrifice is performed inline
///   by the effect closure (mirrors <see cref="WastelandFactory"/>'s
///   sac-self-then-destroy posture — <see cref="AdditionalCost.Sacrifice"/>'s
///   Pay() is still a no-op stub, so we route the move through the
///   resolution body). The resolution effect filters out non-land,
///   basic-land, off-battlefield, and orphan-owner picks (CR 608.2b —
///   illegal target makes the ability's effect do nothing) and moves the
///   chosen land to its owner's graveyard via raw zone manipulation.
/// - <b>Instant speed</b>: Fulminator Mage's activated ability has no
///   sorcery-speed restriction (CR 602.5b — printed activation timing is
///   the default instant-speed unless the oracle text says otherwise).
///   Same posture as Wasteland's second ability.
///
/// ## Deferred (v1 gaps)
/// - <b>AdditionalCost.Sacrifice zone-move TODO</b>: the shared sacrifice
///   cost is still a no-op stub, so we route the self-sac through the
///   effect closure directly — same trick Wasteland / Engineered
///   Explosives / Mishra's Bauble use. The mage moves to its owner's
///   graveyard at resolution, ahead of the destroy step on the chosen
///   target.
/// - <b>Agent target legality filtering</b>: <c>ActionValidator</c> does
///   not yet restrict the agent's target list to nonbasic lands. The
///   resolution-time guard catches illegal picks (CR 608.2b); the tests
///   exercise both the legal nonbasic-land path and the basic-land
///   fizzle path.
/// - <b>ZoneService routing</b>: raw zone manipulation (mirrors Wasteland
///   / Karakas / Teferi -3 bounce). Destroy → graveyard does not emit
///   <see cref="Majik.Core.Events.CardMovedEvent"/> via this path. Wire
///   ZoneService through when the broader destroy-pipeline pass lands.
/// </summary>
public static class FulminatorMageFactory
{
    public const string CardName = "Fulminator Mage";

    /// <summary>
    /// Construct Fulminator Mage owned and controlled by
    /// <paramref name="owner"/>. The activated sacrifice-destroy ability
    /// is attached to the card shape; resolution uses raw zone
    /// manipulation (no ZoneService routing).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{B/R}{B/R}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice Fulminator Mage: Destroy target nonbasic land.
        //
        // CR 602 — activated ability with a single target requirement
        // (Rule 602.2b). No mana / tap cost — sacrifice is the entire
        // activation cost. Mirrors WastelandFactory's destroy-nonbasic-
        // land shape (sans the {T} tap component).
        //
        // The self-sacrifice is performed inside the effect closure
        // because AdditionalCost.Sacrifice's Pay() is a no-op stub.
        //
        // The resolution effect reads ChosenTargets and gates on
        // Land + !Basic + on-battlefield + owner != null at resolution
        // (CR 608.2b — illegal target → effect does nothing).
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            "Fulminator Mage: destroy target nonbasic land",
            () =>
            {
                if (destroyAbility == null) return;

                // Self-sacrifice happens as part of the activated ability's
                // resolution (the cost was paid on activation; visible state
                // catches up here while AdditionalCost.Sacrifice is a stub).
                SacrificeToOwnersGraveyard(card);

                if (destroyAbility.ChosenTargets.Count == 0) return;
                if (destroyAbility.ChosenTargets[0].Count == 0) return;

                var chosen = destroyAbility.ChosenTargets[0][0];
                if (chosen is not ICard target) return;
                if (!target.HasType(CardType.Land)) return;
                if (target.HasSupertype(CardSupertype.Basic)) return;
                if (target.Owner == null) return;
                if (target.Zone != ZoneType.Battlefield) return;

                DestroyToOwnersGraveyard(target);
            });

        destroyAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: System.Array.Empty<ICost>(),
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonbasic land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: System.Array.Empty<object>()),
            });

        card.AddAbility(destroyAbility);

        return card;
    }

    /// <summary>
    /// Move <paramref name="self"/> from the battlefield to its owner's
    /// graveyard as the sacrifice payment for the activated ability.
    /// Mirrors <see cref="WastelandFactory"/>'s self-sac helper.
    /// </summary>
    private static void SacrificeToOwnersGraveyard(Creature self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Move the destroyed target <paramref name="card"/> from the
    /// battlefield to its owner's graveyard. Mirrors the destroy
    /// primitive used by <see cref="WastelandFactory"/> / Boseiju's
    /// channel and the destroy-land spell templates.
    /// </summary>
    private static void DestroyToOwnersGraveyard(ICard card)
    {
        var ownerOfCard = card.Owner;
        if (ownerOfCard == null) return;

        var holder = card.Controller ?? ownerOfCard;
        holder.Zones.Battlefield.RemoveCard(card);
        ownerOfCard.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}

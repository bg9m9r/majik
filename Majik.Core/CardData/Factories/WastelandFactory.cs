using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wasteland (Tempest / reprints).
///
/// Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {T}, Sacrifice Wasteland: Destroy target nonbasic land."
///
/// ## Implemented (v1)
/// - Land identity.
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack).
/// - <b>{T}, Sacrifice Wasteland: Destroy target nonbasic land</b> — wired
///   as an <see cref="ActivatedAbility"/> with <see cref="AdditionalCost.Tap"/>
///   plus an inline sacrifice-self payment. A <see cref="TargetRequest"/> is
///   declared so the activating player's agent picks a nonbasic land at
///   activation time (Rule 602.2b). The resolution effect filters out
///   non-land and basic-land picks (CR 608.2b — illegal target makes the
///   ability's effect do nothing) and moves the chosen land to its owner's
///   graveyard via raw zone manipulation (mirrors KarakasFactory's bounce
///   shape).
/// - <b>Instant speed</b>: Wasteland's second ability has no sorcery-speed
///   restriction (CR 602.5b — printed activation timing is the default
///   instant-speed unless the oracle text says otherwise).
///
/// ## Deferred (v1 gaps)
/// - <b>AdditionalCost.Sacrifice zone-move TODO</b>: the shared sacrifice
///   cost is still a no-op stub (see <see cref="AdditionalCost"/> Pay), so
///   we route the self-sac through the effect closure directly — same
///   trick Engineered Explosives + Mishra's Bauble use. The land moves to
///   its owner's graveyard at resolution, ahead of the destroy step on the
///   chosen target.
/// - <b>Agent target legality filtering</b>: <c>ActionValidator</c> does
///   not yet restrict the agent's target list to nonbasic lands. The
///   resolution-time guard catches illegal picks (CR 608.2b); the tests
///   exercise both the legal and the basic-land paths.
/// - <b>ZoneService routing</b>: raw zone manipulation (mirrors Karakas /
///   Teferi -3 bounce). Destroy → graveyard does not emit
///   <see cref="Majik.Core.Events.CardMovedEvent"/> via this path. Wire
///   ZoneService through when the broader destroy-pipeline pass lands.
/// </summary>
[CardName("Wasteland")]
public static class WastelandFactory
{
    public const string CardName = "Wasteland";

    /// <summary>
    /// Construct Wasteland owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}, Sacrifice Wasteland: Destroy target nonbasic land.
        //
        // CR 602 — activated ability with a single target requirement
        // (Rule 602.2b). Costs:
        //   - {T} via AdditionalCost.Tap(land)
        // The self-sacrifice is performed inside the effect closure
        // because AdditionalCost.Sacrifice's Pay() is a no-op stub.
        //
        // The resolution effect reads ChosenTargets and gates on
        // Land + !Basic at resolution (CR 608.2b — illegal target →
        // effect does nothing).
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            "Wasteland: destroy target nonbasic land",
            () =>
            {
                if (destroyAbility == null) return;

                // Self-sacrifice happens as part of the activated ability's
                // resolution (the cost was paid on activation; visible state
                // catches up here while AdditionalCost.Sacrifice is a stub).
                SacrificeToOwnersGraveyard(land);

                if (destroyAbility.ChosenTargets.Count == 0) return;
                if (destroyAbility.ChosenTargets[0].Count == 0) return;

                var chosen = destroyAbility.ChosenTargets[0][0];
                if (chosen is not ICard card) return;
                if (!card.HasType(CardType.Land)) return;
                if (card.HasSupertype(CardSupertype.Basic)) return;
                if (card.Owner == null) return;
                if (card.Zone != ZoneType.Battlefield) return;

                DestroyToOwnersGraveyard(card);
            });

        destroyAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonbasic land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(destroyAbility);

        return land;
    }

    /// <summary>
    /// Move <paramref name="self"/> from the battlefield to its owner's
    /// graveyard as the sacrifice payment for the activated ability.
    /// </summary>
    private static void SacrificeToOwnersGraveyard(Land self)
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
    /// battlefield to its owner's graveyard. Mirrors the destroy primitive
    /// used by Boseiju's channel and the destroy-land spell templates.
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

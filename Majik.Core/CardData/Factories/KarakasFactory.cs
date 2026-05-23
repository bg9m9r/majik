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
/// Named-card factory for Karakas (Legends / reprints).
///
/// Legendary Land.
/// Oracle text:
///   "{T}: Add {W}.
///    {T}: Return target legendary creature to its owner's hand."
///
/// ## Implemented (v1)
/// - Legendary Land identity (no printed subtypes).
/// - <b>{T}: Add {W}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack). Allowed even on your own legendary creatures (the bounce
///   ability is owner-of-target agnostic; CR 109.2 / 117.x — "any target"
///   is legality, not ownership).
/// - <b>{T}: Return target legendary creature to its owner's hand</b> —
///   wired as an <see cref="ActivatedAbility"/> with an
///   <see cref="AdditionalCost.Tap"/> cost. A <see cref="TargetRequest"/>
///   is declared so the activating player's agent picks a legendary
///   creature at activation time (Rule 602.2b). The resolution effect
///   filters out non-legendary picks (CR 608.2b — illegal target makes
///   the ability's effect that involves the target do nothing) and
///   returns the chosen creature to its owner's hand via raw zone
///   manipulation.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for target legality at activation</b>: the
///   ActionValidator does not currently filter the target list by the
///   <c>"legendary"</c> rider — the resolution-time guard catches illegal
///   picks but the agent could still "pick" a non-legendary creature.
///   The tests exercise both paths.
/// - <b>ZoneService routing</b>: the bounce uses raw zone manipulation
///   (mirrors the bounce in <see cref="TeferiTimeRavelerFactory"/>). LTB
///   triggers via <see cref="Majik.Core.Events.CardMovedEvent"/> are not
///   emitted by this path. Wire ZoneService through when the broader
///   bounce-pipeline pass lands.
/// </summary>
public static class KarakasFactory
{
    public const string CardName = "Karakas";

    /// <summary>
    /// Construct Karakas owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            CardName,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {W}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        // ----------------------------------------------------------------
        // {T}: Return target legendary creature to its owner's hand.
        // CR 602 — activated ability with a single target requirement
        // (Rule 602.2b). The resolution effect reads ChosenTargets and
        // gates on the legendary supertype + creature type at resolution
        // (CR 608.2b — illegal target → effect does nothing).
        // ----------------------------------------------------------------
        ActivatedAbility? bounceAbility = null;
        var bounceEffect = new Effect(
            "Karakas: return target legendary creature to owner's hand",
            () =>
            {
                // ChosenTargets is parallel to TargetRequests; we declared
                // exactly one request, so read [0][0].
                if (bounceAbility == null) return;
                if (bounceAbility.ChosenTargets.Count == 0) return;
                if (bounceAbility.ChosenTargets[0].Count == 0) return;

                var chosen = bounceAbility.ChosenTargets[0][0];
                if (chosen is not Creature creature) return;
                if (!creature.HasSupertype(CardSupertype.Legendary)) return;
                if (creature.Owner == null) return;

                BounceToOwnersHand(creature);
            });

        bounceAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { bounceEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target legendary creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(bounceAbility);

        return land;
    }

    /// <summary>
    /// Move <paramref name="creature"/> from its current zone to its
    /// owner's hand. Mirrors the bounce primitive used by Teferi, Time
    /// Raveler -3 and the Control bounce-target spell template.
    /// </summary>
    private static void BounceToOwnersHand(Creature creature)
    {
        var ownerOfCreature = creature.Owner;
        if (ownerOfCreature == null) return;

        // Remove from whichever zone it currently sits in. Realistically
        // a legendary creature target is on the battlefield; the other
        // branches are defensive.
        var holder = creature.Controller ?? ownerOfCreature;
        switch (creature.Zone)
        {
            case ZoneType.Battlefield:
                holder.Zones.Battlefield.RemoveCard(creature);
                break;
            case ZoneType.Graveyard:
                ownerOfCreature.Zones.Graveyard.RemoveCard(creature);
                break;
            case ZoneType.Exile:
                ownerOfCreature.Zones.Exile.RemoveCard(creature);
                break;
        }

        ownerOfCreature.Zones.Hand.AddCard(creature);
        creature.SetZone(ZoneType.Hand);
    }
}

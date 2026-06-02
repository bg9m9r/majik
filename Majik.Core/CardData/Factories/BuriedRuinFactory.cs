using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Buried Ruin (Scars of Mirrodin).
///
/// Land.
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice this land: Return target artifact card from your
///    graveyard to your hand."
///
/// Buried Ruin is a gate-free sibling of <see cref="EncroachingWastesFactory"/>
/// / <see cref="TectonicEdgeFactory"/>: a {C}-producing utility land whose
/// second ability sacrifices itself for a one-shot effect on a target. The
/// single delta from Encroaching Wastes is the payload — instead of destroying
/// a nonbasic land it returns a target <b>artifact</b> card from the
/// controller's graveyard to their hand (the grave-to-hand recursion primitive
/// of <see cref="UnderworldCookbookFactory"/>, restricted to artifacts).
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes / supertypes), materialised from the
///   embedded JSON definition (<c>buried-ruin.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, which also supplies the
///   <b>{T}: Add {C}</b> mana ability (CR 605.1 — mana abilities do not use
///   the stack).
/// - <b>{2}, {T}, Sacrifice this land: Return target artifact card from your
///   graveyard to your hand.</b> — an <see cref="ActivatedAbility"/> with:
///     - <see cref="ManaCostCost"/> {2}
///     - <see cref="AdditionalCost.Tap"/>
///     - self-sacrifice inlined in the resolution closure (Encroaching Wastes
///       posture, since <see cref="AdditionalCost.Sacrifice"/>'s zone-move
///       primitive is still a stub).
///   A 1..1 <see cref="TargetRequest"/> declares "target artifact card in
///   your graveyard"; the resolution body honours an agent-set chosen target
///   and falls back to the first artifact card in the controller's graveyard
///   (single-arg dispatcher posture — mirrors
///   <see cref="UnderworldCookbookFactory"/>'s first-match fallback), then
///   gates on (a) Artifact type, (b) the controller's graveyard, (c) still in
///   the graveyard at resolution (CR 608.2b — illegal target → the return half
///   does nothing; the cost was already paid so the self-sac still stands).
///
/// ## Deferred (v1 gaps — shared with the Encroaching Wastes / Underworld
/// Cookbook families)
/// - <b>AdditionalCost.Sacrifice</b>: self-sac payment is inlined into the
///   resolution closure until the shared primitive ships a zone-move
///   side-effect. The {2} + {T} are paid by the cost layer at activation; the
///   visible self-sac catches up at resolution ahead of the return step.
/// - <b>Agent target legality filtering / target prompt</b>: ActionValidator
///   does not yet narrow the candidate pool to artifact cards in the
///   controller's graveyard; the resolution-time guard catches illegal picks
///   (CR 608.2b) and the deterministic first-match fallback stands in for an
///   agent-driven choice.
/// - <b>ZoneService routing</b>: raw zone manipulation on the
///   <see cref="Create(Player)"/> path; the grave → hand move does not emit
///   <see cref="Majik.Core.Events.CardMovedEvent"/> via this path. Mirrors
///   the no-ZoneService overload of Underworld Cookbook.
/// </summary>
[CardName("Buried Ruin")]
public static class BuriedRuinFactory
{
    public const string CardName = "Buried Ruin";
    public const string Slug = "buried-ruin";

    /// <summary>Mana cost portion of the graveyard-return activation
    /// (CR 117.1 — everything before the colon is the cost).</summary>
    public const string GraveyardReturnManaCost = "{2}";

    /// <summary>
    /// Construct Buried Ruin owned and controlled by <paramref name="owner"/>.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {C} mana ability). The return-via-sacrifice ability is
        // layered on below — it is not expressible in the current JSON
        // AbilityDefinition schema (same posture as Encroaching Wastes).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice this land: Return target artifact card from
        // your graveyard to your hand.
        //
        // CR 602 — activated ability with a single target requirement
        // (CR 602.2b). Costs (CR 117.1 — everything before the colon):
        //   - {2} via ManaCostCost
        //   - {T} via AdditionalCost.Tap(land)
        //   - Sacrifice this land — inlined into the effect closure because
        //     AdditionalCost.Sacrifice's Pay() is a no-op stub (Encroaching
        //     Wastes posture). The sac is part of the already-paid cost, so it
        //     runs regardless of target legality.
        //
        // The resolution effect reads ChosenTargets (falling back to the
        // first artifact card in the controller's graveyard) and gates on
        // Artifact + controller's graveyard at resolution (CR 608.2b —
        // illegal target → the return half does nothing for that target).
        // ----------------------------------------------------------------
        ActivatedAbility? returnAbility = null;
        var returnEffect = new Effect(
            $"{CardName}: return target artifact card from your graveyard to your hand",
            () =>
            {
                if (returnAbility == null) return;

                // Self-sacrifice (the cost was paid on activation; visible
                // state catches up here while AdditionalCost.Sacrifice is a
                // stub). Part of the cost, so it runs even if the target is
                // illegal.
                SacrificeToOwnersGraveyard(land);

                // CR 110.2 — "your graveyard" / "your hand" resolve to the
                // ability's controller.
                var controller = land.Controller ?? owner;

                ICard? picked = null;

                // 1) Honour an agent-set target (production path).
                if (returnAbility.ChosenTargets.Count > 0
                    && returnAbility.ChosenTargets[0].Count > 0
                    && returnAbility.ChosenTargets[0][0] is ICard chosen)
                {
                    picked = chosen;
                }

                // 2) Deterministic fallback — first artifact card in the
                //    controller's graveyard (single-arg dispatcher posture,
                //    mirrors Underworld Cookbook's first-match fallback).
                picked ??= controller.Zones.Graveyard.GetCards()
                    .FirstOrDefault(c => c.HasType(CardType.Artifact));

                // Empty graveyard / no artifact card — clean no-op (CR 608.2b).
                if (picked == null) return;

                // CR 608.2b — target must still be a legal artifact card in
                // the controller's graveyard at resolution time.
                if (picked.Zone != ZoneType.Graveyard) return;
                if (!picked.HasType(CardType.Artifact)) return;
                if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;

                controller.Zones.Graveyard.RemoveCard(picked);
                controller.Zones.Hand.AddCard(picked);
                picked.SetZone(ZoneType.Hand);
            });

        returnAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(GraveyardReturnManaCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { returnEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Where(c => c.HasType(CardType.Artifact))
                        .Cast<object>()
                        .ToList()),
            });

        land.AddAbility(returnAbility);

        return land;
    }

    /// <summary>
    /// Move <paramref name="self"/> from the battlefield to its owner's
    /// graveyard as the sacrifice payment for the activated ability
    /// (CR 701.16 — to sacrifice a permanent is to move it to its owner's
    /// graveyard from the battlefield).
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
}

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
/// Named-card factory for Encroaching Wastes (Theros).
///
/// Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {4}, {T}, Sacrifice this land: Destroy target nonbasic land."
///
/// Encroaching Wastes is a gate-free sibling of <see cref="WastelandFactory"/>
/// / <see cref="TectonicEdgeFactory"/> / <see cref="FieldOfRuinFactory"/>: a
/// {C}-producing utility land whose second ability sacrifices itself to
/// destroy a nonbasic land. It is implemented with the same destroy
/// primitives as Wasteland. The only delta from Wasteland is the activation
/// cost — Encroaching Wastes adds a {4} generic mana cost on top of the
/// {T} + self-sacrifice (CR 602.1 — the activation cost is everything before
/// the colon). Unlike Tectonic Edge there is NO CR 602.5b activation gate.
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes / supertypes), materialised from the
///   embedded JSON definition (<c>encroaching-wastes.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, which also supplies the
///   <b>{T}: Add {C}</b> mana ability (CR 605.1 — mana abilities do not use
///   the stack).
/// - <b>{4}, {T}, Sacrifice this land: Destroy target nonbasic land.</b> —
///   an <see cref="ActivatedAbility"/> with:
///     - <see cref="ManaCostCost"/> {4}
///     - <see cref="AdditionalCost.Tap"/>
///     - self-sacrifice inlined in the resolution closure (Wasteland posture,
///       since <see cref="AdditionalCost.Sacrifice"/>'s zone-move primitive is
///       still a stub).
///   A 1..1 <see cref="TargetRequest"/> declares "target nonbasic land"; the
///   resolution body gates on (a) Land type, (b) NOT Basic supertype, (c) on
///   the battlefield (CR 608.2b — illegal target → the destroy half does
///   nothing; the cost was already paid so the self-sac still stands).
///
/// ## Deferred (v1 gaps — shared with the Wasteland / Field of Ruin family)
/// - <b>AdditionalCost.Sacrifice</b>: self-sac payment is inlined into the
///   resolution closure until the shared primitive ships a zone-move
///   side-effect. The {4} + {T} are paid by the cost layer at activation; the
///   visible self-sac catches up at resolution ahead of the destroy step.
/// - <b>Agent target legality filtering</b>: ActionValidator does not yet
///   narrow the candidate pool to nonbasic lands; the resolution-time guard
///   catches illegal picks (CR 608.2b).
/// - <b>ZoneService routing</b>: raw zone manipulation (mirrors Wasteland);
///   the destroy → graveyard move does not emit
///   <see cref="Majik.Core.Events.CardMovedEvent"/> via this path. Wire
///   ZoneService through when the broader destroy-pipeline pass lands.
/// </summary>
[CardName("Encroaching Wastes")]
public static class EncroachingWastesFactory
{
    public const string CardName = "Encroaching Wastes";
    public const string Slug = "encroaching-wastes";

    /// <summary>
    /// Construct Encroaching Wastes owned and controlled by
    /// <paramref name="owner"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {C} mana ability). The destroy-via-sacrifice ability is
        // layered on below — it is not expressible in the current JSON
        // AbilityDefinition schema (same posture as Restless Spire).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {4}, {T}, Sacrifice this land: Destroy target nonbasic land.
        //
        // CR 602 — activated ability with a single target requirement
        // (Rule 602.2b). Costs (CR 602.1 — everything before the colon):
        //   - {4} via ManaCostCost
        //   - {T} via AdditionalCost.Tap(land)
        //   - Sacrifice this land — inlined into the effect closure because
        //     AdditionalCost.Sacrifice's Pay() is a no-op stub (Wasteland
        //     posture). The sac is part of the already-paid cost, so it runs
        //     regardless of target legality.
        //
        // The resolution effect reads ChosenTargets and gates on
        // Land + !Basic at resolution (CR 608.2b — illegal target → the
        // effect does nothing for that target).
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            $"{CardName}: destroy target nonbasic land",
            () =>
            {
                if (destroyAbility == null) return;

                // Self-sacrifice (the cost was paid on activation; visible
                // state catches up here while AdditionalCost.Sacrifice is a
                // stub). Part of the cost, so it runs even if the target is
                // illegal.
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
            costs: new ICost[]
            {
                new ManaCostCost("{4}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonbasic land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
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
    /// Move the destroyed target <paramref name="card"/> from the battlefield
    /// to its owner's graveyard. Mirrors Wasteland's destroy primitive.
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

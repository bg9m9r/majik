using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Bones (Tenth Edition / many reprints,
/// Creature — Skeleton {2}{B} 1/1).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{3}{B}, {T}: Target creature gains swampwalk until end of turn.
///    (It can't be blocked as long as defending player controls a Swamp.)"
///   "{1}{B}: Regenerate this creature."
///
/// ## Base shape (JSON)
/// Name, Creature — Skeleton type line, mana cost {2}{B}, and the 1/1 body
/// are materialised from the embedded JSON definition
/// (<c>restless-bones.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Both activated abilities are
/// layered on here because the JSON <c>AbilityDefinition</c> schema
/// expresses neither a tap-cost / target-grant ability nor a regenerate
/// ability yet (same posture as <see cref="GhituEncampmentFactory"/>'s
/// animate ability).
///
/// ## {3}{B}, {T}: Target creature gains swampwalk until end of turn
/// (CR 602 activated ability, CR 514.2 cleanup expiry.) Same shape as
/// <see cref="ShadowspearFactory"/>'s "{1}, {T}: Target creature loses …
/// until end of turn", swapping the keyword-strip for a keyword-grant:
///   - Cost: <see cref="ManaCostCost"/>("{3}{B}") + <see cref="AdditionalCost.Tap"/>.
///   - Target: 1..1 "target creature" — any creature on the battlefield
///     (printed wording is unrestricted, including the controller's own
///     and opponents').
///   - Resolution registers one
///     <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Swampwalk"
///     (CR 702.14 — landwalk; Layer 6 ability addition, CR 613.1f) against
///     the supplied <see cref="ContinuousEffectsService"/>. The effect
///     expires in the cleanup step (CR 514.2) via the base
///     <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> flag. Without a
///     service the activation no-ops (shape-only path).
///
/// ## {1}{B}: Regenerate this creature
/// (CR 701.18 — "Regenerate [self]" creates a regeneration shield,
/// CR 701.15a.) Wired as an <see cref="ActivatedAbility"/> whose sole cost
/// is <see cref="ManaCostCost"/>("{1}{B}"); resolution calls
/// <see cref="Permanent.AddRegenerationShield"/> on Restless Bones. The
/// next time it would be destroyed this turn the shield consumes the
/// destroy, taps it, and clears damage (CR 701.15c). Shields stack across
/// activations and clear during cleanup (CR 514.2). Same shape as
/// <see cref="MortivoreFactory"/>'s "{B}: Regenerate Mortivore".
///
/// ## v1 posture (shared with the landwalk family)
/// - <b>Swampwalk combat enforcement (CR 702.14b)</b> — the granted
///   keyword is visible on the target creature's keyword set, but the
///   combat declare-blockers step doesn't yet consult landwalk markers to
///   reject blocks when the defending player controls a Swamp. Same gap as
///   every landwalk variant (Lord of Atlantis Islandwalk / Goblin King
///   Mountainwalk). The marker is stamped; the combat helper reads it when
///   ready.
/// </summary>
[CardName("Restless Bones")]
public static class RestlessBonesFactory
{
    public const string CardName = "Restless Bones";
    public const string Slug = "restless-bones";
    public const string SwampwalkCost = "{3}{B}";
    public const string RegenerateCost = "{1}{B}";

    /// <summary>
    /// Construct Restless Bones with no live continuous-effects service
    /// (the shape / dispatcher path). Both activated abilities are attached
    /// so the card surface is complete; the swampwalk-grant ability's
    /// resolve closure no-ops without a
    /// <see cref="ContinuousEffectsService"/>. Suitable for unit / shape
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct Restless Bones. When <paramref name="continuousEffects"/>
    /// is supplied, the swampwalk-grant activated ability registers a
    /// <see cref="GrantKeywordUntilEndOfTurnEffect"/> against it per
    /// activation; the regenerate ability is independent of the service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the swampwalk grant
    /// is registered against. May be null — the grant ability still resolves
    /// but records no continuous effect (shape-only path).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature —
        // Skeleton, {2}{B}, 1/1). Both activated abilities are layered on
        // below — neither is expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {3}{B}, {T}: Target creature gains swampwalk until end of turn.
        // CR 602 (activated ability) + CR 702.14 (landwalk) + CR 613.1f
        // (Layer 6 ability addition) + CR 514.2 (cleanup expiry).
        // ----------------------------------------------------------------
        ActivatedAbility? swampwalkAbility = null;
        var swampwalkEffect = new Effect(
            $"{CardName}: target creature gains swampwalk until EOT",
            () =>
            {
                if (swampwalkAbility == null) return;
                if (continuousEffects == null) return; // shape-only path
                if (swampwalkAbility.ChosenTargets.Count == 0) return;
                if (swampwalkAbility.ChosenTargets[0].Count == 0) return;
                if (swampwalkAbility.ChosenTargets[0][0] is not Creature creature) return;

                // CR 608.2b — resolution-time legality. The rules engine
                // drops illegal targets from ChosenTargets; double-check the
                // zone here for defence in depth.
                if (creature.Zone != ZoneType.Battlefield) return;

                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, "Swampwalk"));
            });

        swampwalkAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(SwampwalkCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { swampwalkEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(swampwalkAbility);

        // ----------------------------------------------------------------
        // {1}{B}: Regenerate this creature.
        // CR 701.18 — "Regenerate [self]" = create a regeneration shield on
        // the source (CR 701.15a). Activated ability, regular speed, any
        // number of times per turn (shields stack and clear at EOT).
        // ----------------------------------------------------------------
        var regenerateEffect = new Effect(
            $"{CardName}: regenerate self",
            () => card.AddRegenerationShield());

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(RegenerateCost) },
            effects: new IEffect[] { regenerateEffect }));

        return card;
    }
}

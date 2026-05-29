using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Trashmaster (Mercadian Masques, {2}{R}{R}).
///
/// Creature — Goblin Warrior 3/3. Oracle text (verified against Scryfall):
///   "Other Goblins you control get +1/+1.
///    Sacrifice a Goblin: Destroy target artifact."
///
/// The base shape (name, Creature, Goblin + Warrior subtypes, {2}{R}{R},
/// 3/3) is materialised from the embedded JSON definition
/// (<c>goblin-trashmaster.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="IngotChewerFactory"/>. The JSON <c>AbilityDefinition</c>
/// schema doesn't express lord statics or targeted destroy effects, so the
/// anthem static + the sacrifice activated ability are layered on top here.
///
/// ## Implemented (v1)
///
/// ### Lord static — "Other Goblins you control get +1/+1"
/// CR 613.7c (P/T layer). Wired verbatim from
/// <see cref="GoblinChieftainFactory"/>'s lord static via
/// <see cref="LordStaticEffect"/>: <c>matchingSubtype: Goblin</c>,
/// <c>power: 1, toughness: 1</c>, no granted keywords (Trashmaster grants
/// no Haste, unlike Chieftain), <c>includeSelf: false</c> (the "Other"
/// clause — Trashmaster doesn't pump itself), <c>opponentsOnly: false</c>
/// (controller-scoped — CR 109.5 "you"). Multiple copies stack. The effect
/// only registers when a live <see cref="ContinuousEffectsService"/> is
/// supplied; <see cref="LordStaticEffect.IsActive"/> short-circuits on LTB.
///
/// ### "Sacrifice a Goblin: Destroy target artifact"
/// CR 602 — activated ability. The cost is "sacrifice a Goblin" (a Goblin
/// creature the controller controls); the engine's generic
/// <see cref="Costs.AdditionalCost"/> sacrifice payment is a no-op stub, so
/// the effect closure performs the sacrifice directly — same posture as
/// <see cref="AuraOfSilenceFactory"/>. A 1..1 <see cref="TargetRequest"/>
/// for "target artifact" is declared so the activator picks an artifact at
/// activation (CR 602.2b). On resolution:
/// <list type="number">
///   <item>Sacrifice the first eligible controller Goblin (deterministic v1
///     — same picker posture as
///     <see cref="Costs.SacrificeAGoblinAdditionalCost"/>). If no Goblin is
///     available the cost is unpayable and the whole effect is a clean
///     no-op.</item>
///   <item>The chosen target is still an artifact on the battlefield
///     (CR 608.2b — illegal target → destroy does nothing, but the cost was
///     still paid).</item>
///   <item>If legal: destroy via
///     <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///     <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///     cancels per CR 702.12; active regeneration shield consumed per
///     CR 701.15).</item>
/// </list>
///
/// Note Trashmaster can sacrifice ITSELF to pay the cost (it is a Goblin) —
/// the deterministic picker walks the controller's Goblins on the
/// battlefield, which includes Trashmaster; the test fixture seeds a
/// separate fodder Goblin so the picker order is observable.
///
/// ## Deferred (v1 gaps — same posture as <see cref="AuraOfSilenceFactory"/>)
/// - <b>Real agent-driven sacrifice choice</b>: the cost picks the first
///   eligible Goblin deterministically rather than prompting the controller.
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter activation targets to "artifact"; the resolution-time guard
///   handles illegal targets (CR 608.2b).
/// - <b>Generic sacrifice payment</b>: the effect closure performs the zone
///   move directly because <see cref="Costs.AdditionalCost.Pay"/> is a stub.
/// </summary>
[CardName("Goblin Trashmaster")]
public static class GoblinTrashmasterFactory
{
    public const string CardName = "Goblin Trashmaster";
    public const string Slug = "goblin-trashmaster";

    /// <summary>
    /// Construct Goblin Trashmaster with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord static is not
    /// registered, so other Goblins don't yet receive +1/+1. The activated
    /// ability is always attached. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Goblin Trashmaster. When
    /// <paramref name="continuousEffects"/> is supplied, the
    /// "Other Goblins you control get +1/+1" <see cref="LordStaticEffect"/>
    /// is registered against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the +1/+1
    /// static against. May be null — no live anthem.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Goblin + Warrior subtypes, {2}{R}{R}, 3/3). The JSON carries no
        // abilities — the lord static + sacrifice ability are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Other Goblins you control get +1/+1." CR 613.7c. Verbatim
        // GoblinChieftain lord static minus the granted keywords. includeSelf
        // is false (the "Other" clause); controller-scoped (not
        // opponentsOnly). Multiple copies stack.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Goblin,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false));
        }

        // ----------------------------------------------------------------
        // "Sacrifice a Goblin: Destroy target artifact." CR 602 — activated
        // ability. Pure sacrifice cost (no mana). The destroy follows the
        // AuraOfSilence resolution shape; the sacrifice is performed in the
        // closure because the generic AdditionalCost.Pay path is a stub.
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice a Goblin + destroy target artifact",
            () =>
            {
                // CR 602.1b — pay the cost first. No Goblin to sacrifice ⇒
                // the ability could not have been activated; clean no-op.
                if (!SacrificeAGoblin(owner)) return;

                if (sacAbility == null
                    || sacAbility.ChosenTargets.Count == 0
                    || sacAbility.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                if (sacAbility.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-target check at resolution: must still
                // be an artifact on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact)) return;

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // active regeneration shield (CR 701.15) is consumed.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
            });

        sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: null,
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// Pay the "Sacrifice a Goblin" cost: move the first eligible Goblin the
    /// controller controls from the battlefield to its owner's graveyard
    /// (CR 701.16). Deterministic v1 — same picker posture as
    /// <see cref="Costs.SacrificeAGoblinAdditionalCost"/>. Returns false if
    /// no Goblin is available (cost unpayable).
    /// </summary>
    private static bool SacrificeAGoblin(Player controller)
    {
        var goblin = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.HasSubtype(CardSubtype.Goblin));
        if (goblin == null) return false;

        // CR 701.16 — a sacrificed permanent goes to its owner's graveyard.
        var graveOwner = goblin.Owner ?? controller;
        controller.Zones.Battlefield.RemoveCard(goblin);
        graveOwner.Zones.Graveyard.AddCard(goblin);
        goblin.SetZone(ZoneType.Graveyard);
        return true;
    }
}

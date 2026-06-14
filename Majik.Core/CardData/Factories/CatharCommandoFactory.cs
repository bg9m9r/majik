using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cathar Commando (Innistrad: Midnight Hunt, {1}{W}).
///
/// Creature — Human Soldier 3/1. Oracle text (verified against Scryfall):
///   "Flash
///    {1}, Sacrifice this creature: Destroy target artifact or enchantment."
///
/// ## Shape source
/// Card identity (name, {1}{W}, 3/1, Creature — Human Soldier) is loaded from
/// <c>Majik.Core/CardData/Cards/cathar-commando.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="AmbushViperFactory"/>. The Flash keyword marker and the activated
/// ability are attached in code below (the JSON ability schema does not yet
/// express keyword markers or activated abilities).
///
/// ## Implemented (v1)
/// - 3/1 Creature — Human Soldier at {1}{W}. Color identity white (the {W} pip
///   per CR 202.2c). Mana value 2 (CR 202.3).
/// - <b>Flash</b> (CR 702.8): <see cref="KeywordAbility"/> marker read by
///   <c>TimingRules</c> to allow casting at instant speed — same wire-up shape
///   as <see cref="AmbushViperFactory"/> / <see cref="MysticSnakeFactory"/>.
/// - <b>{1}, Sacrifice this creature: Destroy target artifact or
///   enchantment</b> — a single <see cref="ActivatedAbility"/> (CR 602) whose
///   cost is a <see cref="ManaCostCost"/>("{1}") plus
///   <see cref="AdditionalCost.Sacrifice"/> on the commando itself, with a
///   1..1 <see cref="TargetRequest"/> for "target artifact or enchantment"
///   (Intent: <see cref="BotIntent.Removal"/>). This is the same body as
///   <see cref="CausticCaterpillarFactory"/> with an added {1} mana component.
///   On resolution:
///   <list type="number">
///     <item>Sacrifice the commando (battlefield → owner's graveyard — the
///       generic <see cref="AdditionalCost.Pay"/> sacrifice path is a no-op
///       stub, so the effect closure performs the zone move directly, mirroring
///       <see cref="CausticCaterpillarFactory"/>).</item>
///     <item>Chosen target is still on the battlefield (CR 608.2b).</item>
///     <item>Chosen target is an artifact OR an enchantment.</item>
///     <item>If both pass: destroy via
///       <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///       <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///       cancels per CR 702.12, active regeneration shield consumed per
///       CR 701.15).</item>
///     <item>If any fails: the sacrifice still happens (cost was paid) and the
///       destroy is a clean no-op (CR 608.2b — illegal target → effect does
///       nothing).</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not filter
///   activation targets to "artifact or enchantment" — the resolution-time
///   guard handles illegal targets (CR 608.2b). Same posture as
///   <see cref="CausticCaterpillarFactory"/>.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is a no-op stub; the effect
///   closure performs the zone move directly. Same posture as
///   <see cref="CausticCaterpillarFactory"/>.
/// </summary>
[CardName("Cathar Commando")]
public static class CatharCommandoFactory
{
    public const string CardName = "Cathar Commando";

    /// <summary>The mana component of the activated ability cost.</summary>
    public const string ActivationManaCost = "{1}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("cathar-commando");

    /// <summary>
    /// Construct Cathar Commando owned and controlled by
    /// <paramref name="owner"/>. Attaches the Flash keyword marker and the
    /// single "{1}, sacrifice: destroy target artifact or enchantment"
    /// activated ability.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — Festival-Crasher pattern). Threads <c>effects.EventBus</c>
    /// into the self-sacrifice cost so paying it publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
    /// cost-payer — the seam aristocrat payoffs read.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into the self-sacrifice <see cref="AdditionalCost"/> + the
    /// resolve-path <c>SacrificeSelf</c> fallback so the sacrifice publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a). Null preserves the
    /// legacy publish-nothing posture.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed. TimingRules reads
        // this marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // {1}, Sacrifice this creature: Destroy target artifact or enchantment.
        // CR 602 — activated ability. Cost = ManaCostCost("{1}") +
        // AdditionalCost.Sacrifice on the commando itself.
        // CR 608.2b — resolution-time guard ensures the chosen target is still
        // a legal artifact / enchantment on the battlefield.
        // CR 701.7 — destroy via MoveToGraveyard(Destroy).
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice self + destroy target artifact/enchantment",
            () =>
            {
                SacrificeSelf(card, owner, eventBus);

                if (sacAbility == null
                    || sacAbility.ChosenTargets.Count == 0
                    || sacAbility.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                if (sacAbility.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-target check at resolution.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact)
                    && !target.HasType(CardType.Enchantment))
                {
                    return;
                }

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // active regeneration shield (CR 701.15) is consumed.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
            });

        sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Sacrifice(card, eventBus),
            },
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors
    /// <see cref="CausticCaterpillarFactory"/> — the generic
    /// <see cref="AdditionalCost.Pay"/> sacrifice path is a no-op stub, so the
    /// effect closure performs the zone move directly.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner, IEventBus? eventBus)
    {
        if (card.Zone != ZoneType.Battlefield) return;

        if (eventBus != null)
        {
            Fx.Sacrifice(card, card.Controller ?? owner, eventBus);
            return;
        }

        owner.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}

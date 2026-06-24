using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plumecreed Escort (Bloomburrow, {1}{U}).
///
/// Creature — Bird Scout 2/1. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Flash
///    Flying
///    When this creature enters, target creature you control gains hexproof
///    until end of turn."
///
/// ## Shape source
/// Card identity (name, {1}{U}, blue, 2/1, Creature — Bird Scout, Flash +
/// Flying keyword markers) is loaded from
/// <c>Majik.Core/CardData/Cards/plumecreed-escort.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> and built
/// through <see cref="CardDefinitionFactory"/>. The single ETB triggered
/// ability is attached in code below — the JSON ability schema does not yet
/// express a "target creature you control gains hexproof EOT" rider, so it is
/// hand-rolled here. Mirrors the suggested analogue
/// <see cref="RattlechainsFactory"/>, minus that card's Spirit-subtype
/// restriction and its Spirit-flash printed static (Plumecreed's ETB targets
/// ANY creature you control and it has no flash-granting static).
///
/// ## Implemented (v1)
/// - 2/1 Creature — Bird Scout (CR 205.3m) at {1}{U}, blue.
/// - <b>Flash</b> + <b>Flying</b> keyword markers (CR 702.8 / CR 702.9) —
///   from the JSON <c>keywords</c> list.
/// - <b>ETB hexproof rider</b> (CR 603.6a): a <see cref="TriggeredAbility"/>
///   declaring a 1..1 <see cref="TargetRequest"/> for "target creature you
///   control" (<see cref="BotIntent.Protection"/>). On resolution:
///   <list type="number">
///     <item>If the chosen target is still a <see cref="Creature"/> on the
///       battlefield controlled by Plumecreed's controller (CR 608.2b
///       resolution-time legality re-check), a Layer-6
///       <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Hexproof"
///       (CR 702.11b) is registered on the supplied
///       <see cref="ContinuousEffectsService"/> and expires at cleanup
///       (CR 514.2).</item>
///     <item>If no service is supplied (shape / unit tests) the effect is a
///       clean no-op — the trigger still fires but the keyword grant has
///       nowhere to live.</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Target candidate enumeration</b>: <c>LegalCandidates</c> is left empty
///   (same posture as <see cref="RattlechainsFactory"/> / Pestermite — the
///   production agent enumerates the live battlefield itself).
/// </summary>
[CardName("Plumecreed Escort")]
public static class PlumecreedEscortFactory
{
    public const string CardName = "Plumecreed Escort";
    public const string Slug = "plumecreed-escort";

    /// <summary>Granted keyword — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Plumecreed Escort with its ETB trigger attached structurally
    /// but NOT registered with a <see cref="TriggerManager"/> and no
    /// continuous-effects service. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, continuousEffects: null);

    /// <summary>
    /// Construct Plumecreed Escort with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB-hexproof rider is
    /// registered for bus-driven firing (CR 603.3). When
    /// <paramref name="continuousEffects"/> is supplied, the ETB grants a real
    /// Layer-6 hexproof keyword on resolution.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — "When this creature enters, target creature you
        // control gains hexproof until end of turn." (CR 603.6a + CR
        // 702.11.) 1..1 TargetRequest for the creature, resolution-time
        // legality re-check (CR 608.2b) before registering a Layer-6
        // GrantKeywordUntilEndOfTurnEffect("Hexproof") that expires at
        // cleanup (CR 514.2).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName} — grant target creature you control hexproof EOT",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time illegal-target check. The target
                // must still be a creature on the battlefield controlled by
                // Plumecreed's controller.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!ReferenceEquals(target.Controller, owner)) return;

                if (continuousEffects == null) return;

                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(target, GrantedHexproof));
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}

using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Festering Gulch (Outlaws of Thunder Junction
/// "painland Desert" cycle — the B/G member, sibling of Abraded Bluffs).
///
/// B/G damage-dealing tapland. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, it deals 1 damage to target opponent.
///    {T}: Add {B} or {G}."
///
/// Type line is <c>Land — Desert</c> (the printed Desert subtype).
///
/// ## Implemented (v1)
/// - <b>Identity + dual mana</b> — loaded from
///   <c>Majik.Core/CardData/Cards/festering-gulch.json</c> via
///   <see cref="CardDefinitionFactory"/>: a Land with the Desert subtype
///   and two single-colour <see cref="ManaAbility"/> instances producing
///   {B} and {G} (CR 605.1a — mana abilities don't use the stack). Same
///   dual-mana shape as its cycle sibling <see cref="AbradedBluffsFactory"/>.
/// - <b>Enters-tapped (CR 614.1c)</b> — unconditional
///   "This land enters tapped." Applied on the production load path by
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle
///   text (this factory builds the land without it, matching the cycle
///   posture — the binder owns the replacement so it isn't double-registered).
/// - <b>ETB triggered ability (CR 603.6a)</b> — "When this land enters,
///   it deals 1 damage to target opponent." Wired in code (the JSON
///   damage-effect variant has no opponent-only targeting in the data
///   schema yet) as a self-ETB <see cref="TriggeredAbility"/> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a 1..1
///   "target opponent" <see cref="TargetRequest"/>. On resolution the
///   chosen opponent loses 1 life via <see cref="Fx.DealDamageAny"/>
///   (Player → <see cref="Player.LoseLife"/>) — exactly the
///   <see cref="AbradedBluffsFactory"/> ETB-damage wiring, swapped to the
///   B/G colour pair. CR 608.2b — no chosen target / illegal target at
///   resolution → clean no-op.
///
/// ## Deferred (v1 gaps)
/// - <b>"Target opponent" agent prompt + enforcement</b> — v1 honours
///   pre-supplied targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; the "must be an
///   opponent" restriction is documented in the <see cref="TargetRequest"/>
///   description but not yet machine-enforced (no opponent-only candidate
///   gathering / validation). Same posture as Abraded Bluffs. No chosen
///   target → the damage effect no-ops.
/// </summary>
[CardName("Festering Gulch")]
public static class FesteringGulchFactory
{
    public const string CardName = "Festering Gulch";
    public const int DamageAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("festering-gulch");

    /// <summary>
    /// Construct Festering Gulch with no live wiring. The ETB trigger is
    /// attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>); the enters-tapped replacement is
    /// omitted (no <see cref="ReplacementBus"/> available — the binder
    /// layer owns it on the production path). Enters untapped on this
    /// shape-only path, matching the cycle posture.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Festering Gulch. When <paramref name="triggers"/> is
    /// supplied the ETB damage trigger is registered so bus events
    /// auto-queue it. Enters-tapped (CR 614.1c) is applied by
    /// <see cref="Majik.Core.CardData.EntersTappedBinder"/> on the
    /// production load path, not here.
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + dual {B}/{G} mana come from the JSON definition.
        var card = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this land enters, it deals 1 damage to target opponent."
        // Single 1..1 "target opponent" TargetRequest; on resolution the
        // chosen opponent loses 1 life via Fx.DealDamageAny (Player →
        // Player.LoseLife). Mirrors Abraded Bluffs' 1-damage ETB shape.
        // CR 608.2b — no target chosen → clean no-op.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var damageEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to target opponent",
            () =>
            {
                if (etbTrigger == null) return;
                if (etbTrigger.ChosenTargets.Count == 0) return;
                if (etbTrigger.ChosenTargets[0].Count == 0) return;

                var target = etbTrigger.ChosenTargets[0][0];
                Fx.DealDamageAny(target, DamageAmount);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}

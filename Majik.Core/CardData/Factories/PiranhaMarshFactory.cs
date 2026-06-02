using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Piranha Marsh (Conflux mono-black life-loss
/// tapland).
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, target player loses 1 life.
///    {T}: Add {B}."
///
/// Type line is a plain <c>Land</c> (no printed subtype).
///
/// ## Implemented (v1)
/// - <b>Identity + mana</b> — loaded from
///   <c>Majik.Core/CardData/Cards/piranha-marsh.json</c> via
///   <see cref="CardDefinitionFactory"/>: a nonbasic Land with a single
///   {B} <see cref="ManaAbility"/> (CR 605.1a — mana abilities don't use
///   the stack). Same JSON-driven identity posture as the Refuge cycle
///   (Akoum Refuge) and the painland Deserts (Abraded Bluffs).
/// - <b>Enters-tapped (CR 614.1c)</b> — unconditional "This land enters
///   tapped." Applied on the production load path by
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle
///   text (this factory builds the land without it, matching the Refuge /
///   Abraded Bluffs cycle posture — the binder owns the replacement so it
///   isn't double-registered).
/// - <b>ETB triggered ability (CR 603.6a)</b> — "When this land enters,
///   target player loses 1 life." Wired in code (targeting isn't
///   expressible in the JSON schema yet) as a self-ETB
///   <see cref="TriggeredAbility"/> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a 1..1
///   "target player" <see cref="TargetRequest"/>. On resolution the chosen
///   player loses 1 life via a direct <see cref="Player.LoseLife"/>
///   (CR 119.3 — life loss, NOT damage; unlike Abraded Bluffs' "deals 1
///   damage", so no damage event / replacement applies). CR 608.2b — no
///   chosen target / illegal target at resolution → clean no-op.
///
/// ## Notes
/// - <b>"Target player" (CR 115.1)</b> — any player, including the
///   controller, may be chosen (contrast Abraded Bluffs' "target
///   opponent"). The resolver simply loses the chosen player 1 life.
///
/// ## Deferred (v1 gaps)
/// - <b>"Target player" agent prompt + enforcement</b> — v1 honours
///   pre-supplied targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; candidate gathering /
///   validation is not yet machine-enforced. Same posture as Abraded
///   Bluffs. No chosen target → the life-loss effect no-ops.
/// </summary>
[CardName("Piranha Marsh")]
public static class PiranhaMarshFactory
{
    public const string CardName = "Piranha Marsh";
    public const int LifeLossAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("piranha-marsh");

    /// <summary>
    /// Construct Piranha Marsh with no live wiring. The ETB trigger is
    /// attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>); the enters-tapped replacement is
    /// omitted (no <see cref="ReplacementBus"/> available — the binder
    /// layer owns it on the production path). Enters untapped on this
    /// shape-only path, matching the Refuge / Abraded Bluffs cycle posture.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Piranha Marsh. When <paramref name="triggers"/> is supplied
    /// the ETB life-loss trigger is registered so bus events auto-queue it.
    /// Enters-tapped (CR 614.1c) is applied by
    /// <see cref="Majik.Core.CardData.EntersTappedBinder"/> on the
    /// production load path, not here.
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + single {B} mana come from the JSON definition.
        var card = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this land enters, target player loses 1 life."
        // Single 1..1 "target player" TargetRequest; on resolution the
        // chosen player loses 1 life via a direct Player.LoseLife
        // (CR 119.3 — life loss, not damage). "Target player" is any player
        // including the controller (CR 115.1). CR 608.2b — no target chosen
        // → clean no-op.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var lifeLossEffect = new Effect(
            $"{CardName}: target player loses {LifeLossAmount} life",
            () =>
            {
                if (etbTrigger == null) return;
                if (etbTrigger.ChosenTargets.Count == 0) return;
                if (etbTrigger.ChosenTargets[0].Count == 0) return;

                if (etbTrigger.ChosenTargets[0][0] is Player target)
                {
                    target.LoseLife(LifeLossAmount);
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { lifeLossEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}

using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Orcish Bowmasters (The Lord of the Rings: Tales
/// of Middle-earth, {1}{B}).
///
/// Creature — Orc Archer 1/1. Oracle text:
///   "Flash
///    When this creature enters and whenever an opponent draws a card
///    except the first one they draw in each of their draw steps, this
///    creature deals 1 damage to any target. Then amass Orcs 1."
///
/// ## v2 implementation — full ETB + opponent-draw trigger + Amass Orcs 1
///
/// The single printed line is modelled as TWO sibling
/// <see cref="TriggeredAbility"/> objects sharing one resolve effect:
/// the <see cref="TriggerManager"/>'s subscribe-by-EventType contract
/// requires one ability per event type. Both triggers feed the same
/// "1 damage + Amass Orcs 1" resolve body via a shared captured
/// "pending target" cell, so the printed-text semantics are preserved
/// without an "or" composite condition.
///
/// - 1/1 Orc Archer at {1}{B}. Subtypes Orc + Archer.
/// - <b>Flash</b> (CR 702.8) via <see cref="KeywordAbility"/> — same
///   wiring as Vendilion Clique / Containment Priest. Consumed by
///   <see cref="Majik.Core.Rules.TimingRules.CanCastAtInstantSpeed"/>.
/// - <b>ETB trigger</b> (CR 603.6a) wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — captures the
///   firing opponent as the deterministic fallback target.
/// - <b>Opponent-draw trigger</b> over <see cref="CardDrawnEvent"/>
///   filtered to (player != controller) AND
///   (player's draw-step-draw counter &gt; 1). The per-opponent
///   draw counter is keyed by Player reference and reset to 0 at the
///   start of THAT opponent's Draw step via a
///   <see cref="StepStartedEvent"/> bus subscription (CR 504.1 — only
///   the first draw each draw step is free).
/// - <b>Effect — 1 damage to any target + Amass Orcs 1</b>: composite
///   resolve body running:
///     a. Read agent-set target (any-target — creature OR player); fall
///        back to the captured "pending target" cell (firing opponent
///        for the draw trigger; controller for the ETB v1 dispatcher
///        path).
///     b. <see cref="Fx.DealDamageAny"/> (1 damage) — routes through
///        <see cref="Permanent.AddDamage"/> for creatures (CR 119.1c
///        marked damage stays until cleanup) or
///        <see cref="Player.LoseLife"/> for players (CR 119.3).
///     c. <see cref="AmassAction.Apply"/> with tribe =
///        <see cref="CardSubtype.Orc"/>, count = 1 (CR 701.49 — find
///        controller's first Army OR create a 0/0 black Orc Army,
///        then add 1 +1/+1 counter).
/// - <b>Single-arg dispatcher path</b> attaches Flash + both triggers
///   structurally (no bus → triggers never fire).
/// - <b>(owner, eventBus, triggers, zones) overload</b> registers both
///   triggers with the supplied <see cref="TriggerManager"/> AND
///   subscribes the per-opponent draw-step reset to
///   <see cref="StepStartedEvent"/>. Same posture as
///   <see cref="EsperSentinelFactory"/>'s per-turn reset.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent any-target prompt</b>: deterministic fallback picks the
///   firing opponent as the damage target. The
///   <see cref="TriggeredAbility.SetChosenTargets"/> path overrides
///   when an agent is wired (mirrors Snapcaster / Eternal Witness).
/// - <b>Multiple-draw-replacement edges</b>: the per-opponent counter
///   is incremented on every <see cref="CardDrawnEvent"/>, so the
///   FIRST CardDrawnEvent during an opponent's draw step is the "free"
///   draw (CR 504.1) and every subsequent draw fires Bowmasters. If a
///   draw-step replacement (e.g. Sylvan Library second-draw rider)
///   reshapes draws, the counter still increments correctly because
///   the gate is "counter &gt; 1" not "in draw step".
/// </summary>
[CardName("Orcish Bowmasters")]
public static class OrcishBowmastersFactory
{
    public const string CardName = "Orcish Bowmasters";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Orcish Bowmasters with no live bus / trigger-manager
    /// wiring — Flash + both triggers' shapes are attached, but the
    /// triggers never fire. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zones: null);

    /// <summary>
    /// Construct Orcish Bowmasters with full bus + trigger-manager +
    /// optional <see cref="ZoneService"/> wiring. Both triggers are
    /// registered with the manager for bus-driven firing; the
    /// per-opponent draw-step counter is reset on each opponent's
    /// Draw step via a bus subscription.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Orc, CardSubtype.Archer });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. KeywordAbility marker; TimingRules reads.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // Per-opponent draw-counter map. Keyed by Player reference —
        // tracks "draws so far this step" for each opponent. The first
        // draw each draw step is FREE (CR 504.1); subsequent draws fire
        // Bowmasters' damage + amass.
        // ----------------------------------------------------------------
        var drawsThisStep = new Dictionary<Player, int>();

        // Captured fallback damage target for the resolve body. Set by
        // the firing predicate (opponent for draw trigger, controller for
        // ETB), cleared by the resolve body. The agent-set ChosenTargets
        // overrides this fallback when present.
        var pendingTarget = new object?[] { null };

        // ----------------------------------------------------------------
        // Shared resolve effect — used by BOTH triggers below. Reads
        // agent-set ChosenTargets if present, else the captured
        // pendingTarget fallback. 1 damage + Amass Orcs 1.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        TriggeredAbility? drawTrigger = null;

        IEffect BuildResolveEffect() => new Effect(
            $"{CardName}: 1 damage to any target, then Amass Orcs 1",
            () =>
            {
                // Agent-set target wins. Probe both triggers so the
                // resolve body is generic (whichever trigger surfaced
                // the prompt populated its ChosenTargets).
                object? target = null;
                foreach (var t in new[] { etbTrigger, drawTrigger })
                {
                    if (t != null
                        && t.ChosenTargets.Count > 0
                        && t.ChosenTargets[0].Count > 0)
                    {
                        target = t.ChosenTargets[0][0];
                        break;
                    }
                }

                target ??= pendingTarget[0];
                pendingTarget[0] = null;
                if (target == null) return;

                // CR 119 — 1 damage to chosen target (creature or
                // player). Fx.DealDamageAny dispatches on type.
                Fx.DealDamageAny(target, 1);

                // CR 701.49 — Amass Orcs 1. Controller is Bowmasters'
                // CURRENT controller (read at resolve so a control
                // change between trigger + resolve is honoured).
                var controller = card.Controller ?? owner;
                AmassAction.Apply(controller, count: 1, tribe: CardSubtype.Orc, zones);
            });

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a. "When this creature enters, ..."
        // Captures the controller as the fallback target (no opponent
        // is "the firing player" of an ETB; the controller is the
        // default any-target).
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Card, card)) return false;
            if (e.ToZone != ZoneType.Battlefield) return false;
            pendingTarget[0] = card.Controller ?? owner;
            return true;
        });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { BuildResolveEffect() },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Opponent-draw trigger — CR 603.1.
        //   "Whenever an opponent draws a card except the first one they
        //    draw in each of their draw steps, ..."
        // Per-opponent counter gates the "first draw is free" rule. The
        // counter is reset to 0 at the start of THAT opponent's Draw
        // step (subscription below). Out-of-step draws were never reset
        // → counter is whatever it was (likely >= 1) → they fire too.
        // ----------------------------------------------------------------
        var drawCondition = new EventTriggerCondition<CardDrawnEvent>((e, _) =>
        {
            var drawer = e.Player;
            if (drawer == null) return false;
            if (ReferenceEquals(drawer, card.Controller ?? owner)) return false;

            if (!drawsThisStep.TryGetValue(drawer, out var n)) n = 0;
            n++;
            drawsThisStep[drawer] = n;

            // CR 504.1 — first draw each draw step is free.
            if (n <= 1) return false;

            pendingTarget[0] = drawer;
            return true;
        });

        drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: drawCondition,
            effects: new IEffect[] { BuildResolveEffect() },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            });

        card.AddAbility(drawTrigger);
        triggers?.RegisterTriggeredAbility(drawTrigger);

        // ----------------------------------------------------------------
        // Reset the per-opponent draw counter at the start of each
        // player's Draw step (CR 504.1 — only one "free" draw per draw
        // step, scoped to the active player whose step it is). Each
        // opponent's counter is reset to 0 when their own draw step
        // begins, so subsequent draws in that step register as the
        // "first" draw being free.
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            eventBus.Subscribe<StepStartedEvent>(e =>
            {
                if (e.StepType != PhaseStateType.Draw) return;
                if (e.Player == null) return;
                drawsThisStep[e.Player] = 0;
            });
        }

        return card;
    }
}

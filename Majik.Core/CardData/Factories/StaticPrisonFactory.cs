using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Static Prison (Modern Horizons 3).
///
/// Enchantment — {2}{W}. Oracle text:
///   "When Static Prison enters, you get {E}{E} (two energy counters),
///    then put a stasis counter on Static Prison for each energy you have.
///    Then if Static Prison has no stasis counters on it, exile it.
///    Static Prison has 'Permanents enter tapped' as long as it has a
///    stasis counter on it.
///    At the beginning of each upkeep, remove a stasis counter from
///    Static Prison."
///
/// ## Implemented (v1)
/// - Enchantment shape with mana cost {2}{W}, owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a). Resolution body:
///   <list type="number">
///     <item>controller gains {E}{E} via <see cref="Player.GainEnergy"/>
///       (CR 106.13);</item>
///     <item>snapshots controller's TOTAL energy post-gain (the printed
///       "for each energy you have" — not "for each you got") and places
///       that many <see cref="CounterType.Stasis"/> counters on Static
///       Prison via <see cref="CounterCollection.Add(CounterType,int)"/>;</item>
///     <item>if the resulting stasis-counter count is 0, exiles Static
///       Prison (CR 701.21) via a raw zone move (Battlefield→Exile) on
///       the controller's zones. The "if it has no stasis counters"
///       check happens at resolution time per the printed "Then if …"
///       sequencing — CR 608.2c.</item>
///   </list>
///   Single-arg dispatcher path attaches the trigger structurally; the
///   (owner, replacements, eventBus, triggers) overload also registers
///   the trigger with the supplied <see cref="TriggerManager"/> for
///   bus-driven firing AND wires the global tap-all-permanents
///   replacement (see below).
/// - <b>"Permanents enter tapped" while it has a stasis counter</b>
///   (CR 614.1c). The (owner, replacements, …) overload registers a
///   global <see cref="LambdaReplacement{ZoneMoveIntent}"/> on the
///   supplied <see cref="ReplacementBus"/> whose <c>Applies</c>
///   predicate returns true for ANY card entering the battlefield from
///   another zone WHEN (a) Static Prison is on the battlefield AND
///   (b) Static Prison has at least one stasis counter on it. The
///   <c>Replace</c> body rewrites the intent with
///   <see cref="ZoneMoveIntent.EntersTapped"/> = true so
///   <see cref="Services.ZoneService"/> taps the entering permanent on
///   landing. The replacement excludes Static Prison itself from the
///   tap rewrite (CR 614.1c — a permanent's own ETB-tap clause doesn't
///   tap that same permanent as it lands; in any case Static Prison's
///   stasis-counter gate is false at the moment it itself ETBs, so the
///   guard is belt-and-suspenders).
///
///   The replacement is GLOBAL — it watches every battlefield-entering
///   permanent regardless of controller (the printed clause is
///   board-symmetric, mirroring "Permanents enter the battlefield
///   tapped" from Smokestack / Frozen Aether shapes). The active-while
///   predicate keys on Static Prison's live counter bag, so the
///   replacement automatically deactivates when stasis hits 0 (upkeep
///   drain or ETB-with-0-energy exile) without unregistering — the
///   <c>Applies</c> short-circuit handles it.
/// - <b>Upkeep triggered ability — each upkeep</b> (CR 500.4 / CR
///   603.1): "At the beginning of EACH upkeep, remove a stasis counter
///   from Static Prison." Printed text scopes to BOTH players' upkeeps,
///   not just the controller's — mirrors The Lab Society / Smokestack /
///   the Static Prison printed text exactly. Wired as a raw
///   <see cref="EventTriggerCondition{StepStartedEvent}"/> matching
///   <see cref="Majik.Core.StateMachine.PhaseStateType.Upkeep"/> with
///   NO controller filter (contrast with
///   <see cref="Triggers.OnStepBegin"/>, which restricts to the
///   controller's own upkeep). Resolution body removes one
///   <see cref="CounterType.Stasis"/> counter from Static Prison via
///   <see cref="CounterCollection.Remove(CounterType,int)"/> — clamps
///   at 0 (CR 122.6 — you can't remove a counter that isn't there;
///   <c>CounterCollection.Remove</c> is already 0-safe).
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-bus ordering when Static Prison itself enters at the
///   same time as another permanent</b>: the engine doesn't expose the
///   CR 616 player-choose-order prompt yet, so simultaneous-ETB ordering
///   between the prison's "enters tapped" replacement and the entering
///   permanent's own replacements is whatever the bus produces in
///   registration order. Same gap as every other multi-replacement
///   factory.
/// - <b>Upkeep trigger live wiring on the single-arg dispatcher path</b>:
///   the upkeep trigger is attached structurally for shape inspection
///   but is NOT registered with a <see cref="TriggerManager"/>. Tests
///   fire it manually or invoke the effect directly. The (owner,
///   replacements, eventBus, triggers) overload registers it for
///   bus-driven firing (mirrors The One Ring's two-arg pattern).
/// - <b>Exile-on-ETB-with-0-energy live zone-service routing</b>: the
///   single-arg dispatcher path uses a raw
///   <c>controller.Zones.Battlefield.RemoveCard</c> →
///   <c>controller.Zones.Exile.AddCard</c> → <c>SetZone(Exile)</c>
///   sequence; no <see cref="CardMovedEvent"/> is fired and no
///   replacements consult the move (acceptable — the prison's own ETB
///   has just resolved, no other replacement applies to the same card
///   leaving the battlefield in the same resolution step). The
///   (owner, replacements, eventBus, triggers) overload could use the
///   ZoneService route, but currently mirrors the raw-move shape for
///   parity.
/// </summary>
public static class StaticPrisonFactory
{
    public const string CardName = "Static Prison";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>
    /// Construct Static Prison with no live bus / trigger-manager wiring.
    /// Triggers are attached structurally; the global tap-replacement is
    /// NOT registered. Tests fire triggers by invoking effects directly.
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, replacements: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Static Prison with optional replacement bus + event bus
    /// + trigger manager. When <paramref name="replacements"/> is
    /// supplied, the global "permanents enter tapped while stasis &gt; 0"
    /// replacement (CR 614.1c) is registered on the bus. When
    /// <paramref name="triggers"/> is supplied, the ETB + upkeep triggers
    /// are registered for bus-driven firing.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var prison = new Enchantment(CardName, PrintedManaCost);
        prison.SetOwner(owner);
        prison.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When Static Prison enters, you get {E}{E}, then put a stasis
        //    counter on Static Prison for each energy you have. Then if
        //    Static Prison has no stasis counters on it, exile it."
        // Order matters (CR 608.2c):
        //   1. controller.GainEnergy(2)
        //   2. snapshot total post-gain energy → that many stasis counters
        //   3. if counters == 0 → exile self (CR 701.21)
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Static Prison: gain {E}{E}, place stasis counters, self-exile if 0",
            () =>
            {
                var controller = prison.Controller ?? owner;

                // (1) Gain {E}{E}.
                controller.GainEnergy(2);

                // (2) "For each energy you have" — post-gain TOTAL energy
                // (CR 106.13). Place that many stasis counters.
                var energy = controller.EnergyCounters;
                if (energy > 0)
                {
                    prison.Counters.Add(CounterType.Stasis, energy);
                }

                // (3) "Then if Static Prison has no stasis counters on it,
                // exile it." (CR 608.2c — sequenced after the counter
                // placement.) Raw zone move: Battlefield → Exile.
                if (prison.Counters.Count(CounterType.Stasis) == 0)
                {
                    if (prison.Zone == ZoneType.Battlefield)
                    {
                        controller.Zones.Battlefield.RemoveCard(prison);
                    }
                    controller.Zones.Exile.AddCard(prison);
                    prison.SetZone(ZoneType.Exile);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: prison,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(prison),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        prison.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 500.4 / CR 603.1.
        //   "At the beginning of EACH upkeep, remove a stasis counter
        //    from Static Prison."
        // NOTE: "each upkeep" (printed) — scope to both players' upkeeps,
        // NOT just controller's. Triggers.OnStepBegin restricts to one
        // player; we use a raw EventTriggerCondition with no Player
        // filter here.
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            "Static Prison: remove a stasis counter",
            () =>
            {
                if (prison.Counters.Count(CounterType.Stasis) > 0)
                {
                    prison.Counters.Remove(CounterType.Stasis);
                }
            });

        var upkeepTrigger = new TriggeredAbility(
            source: prison,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        prison.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // Global "permanents enter tapped" replacement — CR 614.1c.
        // Active while:
        //   (a) Static Prison is on the battlefield, AND
        //   (b) Static Prison has ≥1 stasis counter on it.
        // Applies to: any non-Static-Prison card entering the battlefield
        // from another zone. Rewrites ZoneMoveIntent.EntersTapped = true.
        // ZoneService consults this on every battlefield landing.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            var tapAll = new LambdaReplacement<ZoneMoveIntent>(
                applies: (intent, _) =>
                {
                    if (intent.ToZone != ZoneType.Battlefield) return false;
                    if (intent.FromZone == ZoneType.Battlefield) return false;
                    if (ReferenceEquals(intent.Card, prison)) return false;
                    if (prison.Zone != ZoneType.Battlefield) return false;
                    return prison.Counters.Count(CounterType.Stasis) > 0;
                },
                replace: (intent, _) => intent with { EntersTapped = true });

            replacements.Register(tapAll);
        }

        return prison;
    }
}

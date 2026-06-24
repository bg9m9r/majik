using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cackling Prowler (Tarkir: Dragonstorm, {3}{G}).
///
/// Creature — Hyena Rogue 4/3. Oracle text (verified against the embedded
/// Scryfall seed):
///   "Ward {2} (Whenever this creature becomes the target of a spell or ability
///    an opponent controls, counter it unless that player pays {2}.)
///    Morbid — At the beginning of your end step, if a creature died this turn,
///    put a +1/+1 counter on this creature."
///
/// ## Shape source
/// Card identity (name, {3}{G}, 4/3, Creature — Hyena Rogue) is loaded from
/// <c>Majik.Core/CardData/Cards/cackling-prowler.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The JSON carries no abilities — the Ward
/// keyword marker and the Morbid end-step counter trigger are layered on here
/// (same posture as <see cref="KnightOfTheEbonLegionFactory"/>, whose end-step
/// intervening-if counter trigger this cribs directly).
///
/// ## Implemented (v1)
/// - <b>4/3 Creature — Hyena Rogue</b> at {3}{G}, green, owner / controller
///   stamped.
/// - <b>Ward {2} (CR 702.21)</b> as a <see cref="KeywordAbility"/>("Ward")
///   marker only. Same posture as <see cref="SpinewoodsArmadilloFactory"/> /
///   every other Ward factory — the keyword is surfaced for introspection
///   (UI / bots), but the spell-resolution "counter unless they pay {2}"
///   consultation is a deferred cross-factory gap (no Ward trigger primitive on
///   spell resolution yet). The printed Ward cost ({2}) is therefore not carried
///   as a value (the marker is un-parameterized, matching every other Ward
///   factory).
/// - <b>Morbid end-step counter trigger (CR 603.1 + CR 603.4 intervening-if +
///   CR 121.1)</b>: "At the beginning of your end step, if a creature died this
///   turn, put a +1/+1 counter on this creature."
///     - "your end step" carries the controller filter (CR 500) via
///       <see cref="Triggers.OnStepBegin"/>(controller,
///       <see cref="StepStateType.End"/>) — fires only on the controller's own
///       end step.
///     - The Morbid intervening-if (CR 603.4) is the GLOBAL "a creature died
///       this turn" question (CR 700.4 — any creature, any controller), read
///       from <see cref="TurnState.CreaturesDiedThisTurn"/> via the supplied
///       <paramref name="turnStateResolver"/> — same Morbid sample as
///       <see cref="CacklingSlasherFactory"/> / <see cref="TragicSlipFactory"/>.
///       When no resolver is wired (shape / dispatcher tests) the gate is false
///       and the trigger no-ops.
///     - On a satisfied resolution it puts one +1/+1 counter on the Prowler via
///       <see cref="CountersService.Add"/> (CR 121.1 / 614 — routed through the
///       optional <see cref="ReplacementBus"/> so Hardened Scales / Doubling
///       Season can rewrite the count, and the <see cref="CounterAddedEvent"/>
///       fires for counters-matter payoffs).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The end-step trigger is attached
///   structurally but NOT bus-registered (no <see cref="TriggerManager"/>); its
///   Morbid intervening-if reads no <see cref="TurnState"/> (null resolver →
///   false). This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ReplacementBus?, System.Func{TurnState?})"/>
///   — fully wired: the trigger is bus-registered, the Morbid gate samples the
///   live turn state, and the +1/+1 placement routes through the replacement bus
///   + publishes <see cref="CounterAddedEvent"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Ward {2} trigger wiring</b>: keyword marker present; the
///   counter-unless-they-pay surface lands once the Ward trigger primitive is
///   plumbed onto spell resolution (sibling gap to every other Ward factory).
/// </summary>
[CardName("Cackling Prowler")]
public static class CacklingProwlerFactory
{
    public const string CardName = "Cackling Prowler";
    public const string Slug = "cackling-prowler";

    /// <summary>CR 702.21 — Cackling Prowler's printed Ward cost ({2}). Carried
    /// as documentation only; the marker keyword is un-parameterized (sibling to
    /// every other Ward factory).</summary>
    public const string WardCost = "{2}";

    /// <summary>+1/+1 counters placed by the Morbid end-step trigger
    /// (CR 121.1).</summary>
    public const int CounterAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Cackling Prowler with card identity + Ward marker only — the
    /// Morbid end-step counter trigger is attached structurally but not
    /// bus-registered, and its intervening-if reads no <see cref="TurnState"/>
    /// (null resolver → gate false → no-op). Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null,
               turnStateResolver: null);

    /// <summary>
    /// Construct Cackling Prowler with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Routed through <see cref="CountersService.Add"/>
    /// so the +1/+1 placement publishes <see cref="CounterAddedEvent"/>. May be
    /// null.</param>
    /// <param name="triggers">Registers the Morbid end-step counter trigger for
    /// bus-driven firing. Null → the trigger is attached to the card but not
    /// bus-driven.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> routed
    /// through <see cref="CountersService.Add"/> for the +1/+1 placement
    /// (Hardened Scales / Doubling Season — CR 614).</param>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at resolution time. Null return (no driver wired —
    /// typical for shape / dispatcher tests) is treated as Morbid inactive (the
    /// intervening-if fails and the trigger no-ops). Same posture as
    /// <see cref="CacklingSlasherFactory"/>.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        System.Func<TurnState?>? turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Hyena
        // Rogue subtypes, {3}{G}, 4/3). The JSON carries no abilities — the Ward
        // marker + Morbid end-step trigger are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.21 — Ward {2}. Marker keyword for discovery (UI / bots). The
        // functional "counter unless they pay {2}" rider is a deferred
        // cross-factory gap (no Ward trigger primitive on spell resolution yet),
        // same posture as every other Ward factory.
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // ----------------------------------------------------------------
        // Morbid — "At the beginning of your end step, if a creature died this
        // turn, put a +1/+1 counter on this creature." CR 603.1 + CR 603.4
        // (intervening-if) + CR 121.1.
        //
        // "your end step" → controller filter (Triggers.OnStepBegin, CR 500).
        // The Morbid intervening-if is checked at resolution against the GLOBAL
        // "a creature died this turn" tally (CR 700.4 — any creature, any
        // controller) via TurnState.CreaturesDiedThisTurn. Null resolver →
        // gate false → no-op (shape / dispatcher path).
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on this creature if a creature died this turn (Morbid)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (!IsMorbidActive(turnStateResolver)) return;

                CountersService.Add(
                    card,
                    CounterType.PlusOnePlusOne,
                    CounterAmount,
                    replacements,
                    eventBus);
            });

        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.End),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.4 / CR 700.4 — Morbid: true iff at least one creature died this
    /// turn (any creature, any controller), read from
    /// <see cref="TurnState.CreaturesDiedThisTurn"/>. Null-safe: when no
    /// <see cref="TurnState"/> is wired the gate is false (same posture as
    /// <see cref="CacklingSlasherFactory"/> / <see cref="TragicSlipFactory"/>).
    /// </summary>
    public static bool IsMorbidActive(System.Func<TurnState?>? turnStateResolver)
    {
        var turnState = turnStateResolver?.Invoke();
        return turnState != null && turnState.CreaturesDiedThisTurn > 0;
    }
}

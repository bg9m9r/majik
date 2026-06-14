using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shrine of Burning Rage (New Phyrexia, {2}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-06-14):
///   "At the beginning of your upkeep and whenever you cast a red spell, put
///    a charge counter on this artifact.
///    {3}, {T}, Sacrifice this artifact: It deals damage equal to the number
///    of charge counters on it to any target."
///
/// The base shape (name, Artifact, {2}) is materialised from the embedded
/// JSON definition (<c>shrine-of-burning-rage.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two charge-counter triggers
/// and the burn activation are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express the trigger / activated
/// shapes (same posture as <see cref="WeeDragonautsFactory"/>).
///
/// ## Implemented (v1)
/// - Plain Artifact identity ({2}, no supertype, no printed subtype).
/// - <b>Charge-counter accrual</b> (CR 603.1 / CR 122.1) — modelled as TWO
///   <see cref="TriggeredAbility"/> objects that share the same body (add one
///   <see cref="CounterType.Charge"/> counter). This is functionally
///   identical to the single printed trigger with an "upkeep OR red-cast"
///   condition (both clauses share one source + put exactly one counter), and
///   matches the engine's per-event-type
///   <see cref="EventTriggerCondition{TEvent}"/> shape:
///   <list type="bullet">
///     <item><b>Upkeep</b>: <see cref="Triggers.OnStepBegin"/> filtered to the
///       controller's <see cref="Majik.Core.StateMachine.StepStateType.Upkeep"/>
///       (CR 500.4). Mirrors <see cref="DarksteelReactorFactory"/>'s upkeep
///       charge trigger.</item>
///     <item><b>Whenever you cast a red spell</b>: an
///       <see cref="EventTriggerCondition{T}"/> over
///       <see cref="SpellCastEvent"/> where the spell's controller is this
///       artifact's controller AND the spell's card is red
///       (<see cref="CardColors.GetColors"/> contains
///       <see cref="ManaColor.Red"/> — CR 105.2a / CR 202.2). Mirrors the
///       prowess-style SpellCastEvent filtering in
///       <see cref="WeeDragonautsFactory"/> / Kessig Flamebreather, narrowed
///       to red spells.</item>
///   </list>
/// - <b>{3}, {T}, Sacrifice this artifact: deal damage = charge counters to
///   any target</b> — an <see cref="ActivatedAbility"/> with
///   <see cref="ManaCostCost"/>("{3}") + <see cref="AdditionalCost.Tap"/> +
///   <see cref="AdditionalCost.Sacrifice"/> and a 1..1 "any target"
///   <see cref="TargetRequest"/>. Resolution snapshots the charge count
///   BEFORE the sacrifice (CR 121.2 — counters cease to exist once the Shrine
///   leaves the battlefield), moves the Shrine to its owner's graveyard
///   (CR 701.16), then deals that much damage to the chosen target via
///   <see cref="Fx.DealDamageAny"/> so Player / Creature / Planeswalker
///   targets all funnel through the right damage shape (CR 119.3 / CR 306.7).
///   Mirrors <see cref="GoblinCharbelcherFactory"/>'s "{cost}, {T}: deal
///   damage to any target" activation + Ratchet Bomb's snapshot-before-sac.
///
/// ## Deferred (v1 gaps — same posture as the analogue factories)
/// - <b>ReplacementBus / CounterAddedEvent</b>: charge counters are placed
///   directly on the permanent's <see cref="CounterCollection"/> rather than
///   routed through CountersService (no Modern card keys on Charge-counter
///   placement). Same as Darksteel Reactor / Ratchet Bomb.
/// - <b>Sacrifice cost as a real cost</b>: the effect closure performs the
///   zone move (publishing <see cref="PermanentSacrificedEvent"/> when a bus
///   is wired) — same stub posture as Ratchet Bomb / Blast Zone.
/// - <b>Target prompts</b>: come through
///   <see cref="ActivatedAbility.ChosenTargets"/> at resolve time; illegal /
///   absent target at resolution = the damage step is skipped (CR 608.2b),
///   but the cost (sac) still happened. Mirrors Goblin Charbelcher.
/// </summary>
[CardName("Shrine of Burning Rage")]
public static class ShrineOfBurningRageFactory
{
    public const string CardName = "Shrine of Burning Rage";
    public const string Slug = "shrine-of-burning-rage";

    /// <summary>
    /// Construct Shrine of Burning Rage with no live wiring. The two charge
    /// triggers are attached to the card for shape observability but are not
    /// registered with any <see cref="TriggerManager"/> (the bus won't fire
    /// them); the burn activation works under direct effect invocation.
    /// Suitable for dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — Festival-Crasher pattern). Threads <c>effects.EventBus</c>
    /// into the self-sacrifice cost so paying it publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a). The
    /// <see cref="TriggerManager"/> for the charge triggers is wired by the
    /// routed activation path; the structural overload below leaves it null.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus, triggers: null);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into the self-sacrifice <see cref="AdditionalCost"/> so the
    /// sacrifice publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a). <paramref name="triggers"/> (when non-null) registers the
    /// two charge-counter triggers so the bus surfaces them automatically.
    /// </summary>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact, {2}).
        // The JSON carries no abilities — the triggers + burn are layered on
        // below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var shrine = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Charge-counter accrual (CR 603.1 / CR 122.1).
        //   "At the beginning of your upkeep and whenever you cast a red
        //    spell, put a charge counter on this artifact."
        // Modelled as two triggers sharing the same one-counter body — the
        // engine's trigger conditions are per-event-type, and splitting the
        // printed "A or B" clause into two same-bodied triggers is functionally
        // identical (each puts exactly one charge counter).
        // ----------------------------------------------------------------
        void AddChargeCounter()
        {
            if (shrine.Zone != ZoneType.Battlefield) return;
            shrine.Counters.Add(CounterType.Charge, 1);
        }

        // CR 500.4 — "At the beginning of your upkeep".
        var upkeepTrigger = new TriggeredAbility(
            source: shrine,
            controller: owner,
            condition: Triggers.OnStepBegin(
                owner, Majik.Core.StateMachine.StepStateType.Upkeep),
            effects: new IEffect[]
            {
                new Effect($"{CardName}: put a charge counter (your upkeep)", AddChargeCounter),
            },
            activeZones: new[] { ZoneType.Battlefield });

        // CR 603.1 — "whenever you cast a red spell". Predicate: the spell's
        // controller is this artifact's controller (CR 601 — "you" = the
        // permanent's controller) AND the spell's card is red
        // (CR 105.2a / CR 202.2 — color derived from mana cost pips +
        // color indicator).
        var redCastCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, shrine.Controller ?? owner)
            && CardColors.GetColors(e.Spell.Card).Contains(ManaColor.Red));

        var redCastTrigger = new TriggeredAbility(
            source: shrine,
            controller: owner,
            condition: redCastCondition,
            effects: new IEffect[]
            {
                new Effect($"{CardName}: put a charge counter (cast a red spell)", AddChargeCounter),
            },
            activeZones: new[] { ZoneType.Battlefield });

        shrine.AddAbility(upkeepTrigger);
        shrine.AddAbility(redCastTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(redCastTrigger);

        // ----------------------------------------------------------------
        // {3}, {T}, Sacrifice this artifact: It deals damage equal to the
        // number of charge counters on it to any target.
        //
        // CR 602 — ordinary activated ability, instant speed (no sorcery
        // rider). Snapshot the charge count BEFORE the sacrifice (CR 121.2 —
        // counters cease to exist on the zone change), move the Shrine to its
        // owner's graveyard (CR 701.16), then deal that much damage to the
        // chosen "any target" (CR 119.3 / CR 306.7).
        // ----------------------------------------------------------------
        ActivatedAbility? burnAbility = null;
        var burnEffect = new Effect(
            $"{CardName}: deal damage = charge counters to any target",
            () =>
            {
                // Snapshot BEFORE the sacrifice — once the Shrine is in the
                // graveyard its Counters bag is gone (CR 121.2).
                var damage = shrine.Counters.Count(CounterType.Charge);

                // Resolve the chosen target before moving the Shrine.
                object? target = null;
                if (burnAbility != null
                    && burnAbility.ChosenTargets.Count > 0
                    && burnAbility.ChosenTargets[0].Count > 0)
                {
                    target = burnAbility.ChosenTargets[0][0];
                }

                // Sacrifice payment — move the Shrine to its owner's graveyard
                // (CR 701.16). Route through Fx.Sacrifice when a bus is wired so
                // the resolve-only path publishes PermanentSacrificedEvent
                // (CR 701.16a); otherwise do the raw zone move.
                if (shrine.Zone == ZoneType.Battlefield)
                {
                    if (eventBus != null)
                    {
                        Fx.Sacrifice(shrine, shrine.Controller ?? owner, eventBus);
                    }
                    else
                    {
                        owner.Zones.Battlefield.RemoveCard(shrine);
                        owner.Zones.Graveyard.AddCard(shrine);
                        shrine.SetZone(ZoneType.Graveyard);
                    }
                }

                // CR 608.2b — illegal / absent target at resolution skips the
                // damage step (the cost was already paid).
                if (target != null && damage > 0)
                {
                    Fx.DealDamageAny(target, damage);
                }
            });

        burnAbility = new ActivatedAbility(
            source: shrine,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Tap(shrine),
                AdditionalCost.Sacrifice(shrine, eventBus),
            },
            effects: new IEffect[] { burnEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        shrine.AddAbility(burnAbility);

        return shrine;
    }
}

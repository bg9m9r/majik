using System.Reflection;
using FluentAssertions;
using Majik.Core.Events;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// PLAN 07 — coverage guard over the EventPayloadBuilder mapping.
///
/// Every concrete <see cref="GameEvent"/> subclass must be EITHER:
///   * <b>migrated</b> — it has a typed <c>*Payload</c> record + a builder
///     arm constructing it (see <see cref="MigratedEvents"/>), OR
///   * <b>known-empty</b> — explicitly on <see cref="KnownEmptyPayload"/>,
///     meaning the builder deliberately emits <c>{}</c> for it (the ~31
///     long-tail types not yet migrated; the portal refetches /state on
///     them, which is non-breaking).
///
/// This reflection test fails the moment a NEW GameEvent subclass is added
/// without a conscious decision (record arm or allow-list entry),
/// preventing a silent <c>{}</c> regression and tracking the incremental
/// migration of the long tail.
/// </summary>
public class EventPayloadCoverageTests
{
    // The 16 currently-emitted typed payload arms (PLAN 07 first cut). Each
    // name maps to a *Payload record constructed by EventPayloadBuilder.
    // CombatDamageDealtEvent routes through the DamageDealtEvent base arm.
    private static readonly HashSet<string> MigratedEvents = new()
    {
        nameof(CardMovedEvent),
        nameof(CardDrawnEvent),
        "CardRevealedEvent",
        nameof(LifeChangedEvent),
        "PhaseStartedEvent",
        "PhaseEndedEvent",
        "PhaseChangedEvent",
        "TurnStateChangedEvent",
        "StepStartedEvent",
        "StepEndedEvent",
        nameof(TurnStartedEvent),
        "TurnEndedEvent",
        "ExtraPhaseAddedEvent",
        "PlayerLostEvent",
        "SpellCastEvent",
        "StackObjectAddedEvent",
        "StackObjectResolvedEvent",
        "DamageDealtEvent",
        "CombatDamageDealtEvent", // routes via the DamageDealtEvent arm
        "CounterAddedEvent",
    };

    // The long-tail GameEvent subclasses that deliberately emit an empty
    // payload today (builder `_ => Empty()` / explicit `=> Empty()`). They
    // are tracked here so the coverage test stays green while making the
    // remaining migration EXPLICIT — moving a name from this set to
    // MigratedEvents (with a record + arm) is the per-type migration step.
    private static readonly HashSet<string> KnownEmptyPayload = new()
    {
        "AbilityActivatedEvent",
        "AllPlayersPassedEvent",
        "AttackersDeclaredEvent",
        "BlockersDeclaredEvent",
        "CardCycledEvent",
        "ClassLevelUpEvent",
        "CombatEndedEvent",
        "CombatStartedEvent",
        "CostsPaidEvent",
        "CreatureAttacksEvent",
        "DayNightChangedEvent",
        "ExtraTurnAddedEvent",
        "GainedCitysBlessingEvent",
        "GameStartedEvent",
        "LibraryShuffledEvent",
        "ManaAbilityActivatedEvent",
        "OpeningHandCheckEvent",
        "PriorityPassedEvent",
        "PriorityReceivedEvent",
        "StackClearedEvent",
        "StateBasedActionExecutedEvent",
        "SurveilEvent",
        "SuspendCounterDrainedEvent",
        "TargetsChosenEvent",
        "TriggeredAbilityCounteredEvent",
        "TriggeredAbilityTriggeredEvent",
        "UnimplementedCardEncounteredEvent",
    };

    private static IEnumerable<Type> AllConcreteGameEvents()
    {
        // GameEvent subclasses live in two assemblies' worth of namespaces
        // but a single assembly (Majik.Core). Scan that assembly.
        var asm = typeof(GameEvent).Assembly;
        return asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(GameEvent).IsAssignableFrom(t));
    }

    [Fact]
    public void EveryGameEvent_IsEitherMigratedOrKnownEmpty()
    {
        var classified = new HashSet<string>(MigratedEvents);
        classified.UnionWith(KnownEmptyPayload);

        var unclassified = AllConcreteGameEvents()
            .Select(t => t.Name)
            .Where(n => !classified.Contains(n))
            .OrderBy(n => n)
            .ToList();

        unclassified.Should().BeEmpty(
            "every concrete GameEvent must have a typed payload record + builder " +
            "arm (add to MigratedEvents) OR be an explicit known-empty payload " +
            "(add to KnownEmptyPayload). A new event here means a silent `{}` " +
            "regression — make the choice explicit. Unclassified: " +
            string.Join(", ", unclassified));
    }

    [Fact]
    public void MigratedAndKnownEmpty_AreDisjoint()
    {
        MigratedEvents.Overlaps(KnownEmptyPayload).Should().BeFalse(
            "an event is either migrated or known-empty, never both.");
    }

    [Fact]
    public void AllListedEventNames_AreRealGameEventTypes()
    {
        // Guard against typos / stale names lingering in either set after a
        // rename. Every listed name must correspond to a live concrete type.
        var live = AllConcreteGameEvents().Select(t => t.Name).ToHashSet();
        var listed = new HashSet<string>(MigratedEvents);
        listed.UnionWith(KnownEmptyPayload);

        var stale = listed.Where(n => !live.Contains(n)).OrderBy(n => n).ToList();
        stale.Should().BeEmpty(
            "a listed event name no longer maps to a concrete GameEvent " +
            "(rename / removal?). Stale: " + string.Join(", ", stale));
    }
}

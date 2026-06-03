using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Planeswalker = Majik.Core.Cards.Planeswalker;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Engine-level coverage for the declarative <c>exile_with_return</c> spell
/// verb — the "exile target(s), return at a stated future moment" flicker
/// family (CR 701.21 exile + CR 603.7 delayed triggered ability + CR 614
/// "under its owner's control"). This is the SPELL-path sibling of the
/// permanent-anchored <c>exile_until_leaves</c> verb: instead of returning
/// when the source leaves the battlefield, it schedules a one-shot return at
/// the next end step (or next upkeep).
///
/// The canonical "for many" card is Eerie Interlude ({2}{W} instant): "Exile
/// any number of target creatures you control. Return those cards to the
/// battlefield under their owner's control at the beginning of the next end
/// step." — a single multi-target slot whose whole batch is exiled and
/// returned together by one delayed trigger.
///
/// Each test builds a runtime SpellDefinition straight from inline JSON via
/// the #2128 spell adapter, registers a <see cref="TriggerManager"/> through
/// <see cref="TriggerManagerRegistry"/> so the resolve closure can schedule
/// the delayed return, then drives the cast + the end-step trigger the same
/// way the hand-rolled OtherworldlyJourney tests do.
/// </summary>
public class JsonExileWithReturnTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;

    public JsonExileWithReturnTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        TriggerManagerRegistry.Set(_triggers);
    }

    public void Dispose() => TriggerManagerRegistry.Clear();

    // Eerie Interlude shape: "exile any number of target creatures you control;
    // return them at the beginning of the next end step."
    private static SpellDefinition BuildEerieInterlude() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Eerie Interlude",
            new EffectDefinition[]
            {
                new ExileWithReturnEffectDef
                {
                    TargetFilter = "creature_you_control",
                    MinTargets = 0,
                    MaxTargets = 99,
                    ReturnAt = "next_end_step",
                },
            });

    private Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var bear = new Creature(name, cost, 2, 2);
        bear.SetOwner(owner);
        bear.SetController(owner);
        owner.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }

    private Planeswalker NewControlledPlaneswalker(Player owner, string name, string cost, int loyalty)
    {
        var pw = new Planeswalker(name, cost, loyalty);
        pw.SetOwner(owner);
        pw.SetController(owner);
        owner.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);
        return pw;
    }

    // Semester's End shape: "exile any number of target creatures and/or
    // planeswalkers you control; return them at the next end step, each with a
    // +1/+1 counter if it's a creature and a loyalty counter if it's a
    // planeswalker."
    private static SpellDefinition BuildSemestersEnd() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            "Semester's End",
            new EffectDefinition[]
            {
                new ExileWithReturnEffectDef
                {
                    TargetFilter = "creature_or_planeswalker_you_control",
                    MinTargets = 0,
                    MaxTargets = 99,
                    ReturnAt = "next_end_step",
                    CounterOnReturn = "plus_one_plus_one_or_loyalty",
                },
            });

    private void ExecuteCast(SpellDefinition def, params object[] targets)
    {
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob }));
        foreach (var e in effects) e.Execute();
    }

    private void FireEndStepReturn()
    {
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        _triggers.PutPendingTriggersOnStack(_alice);
        while (_stack.Count > 0) _stack.Pop()!.Resolve();
    }

    // -----------------------------------------------------------------------
    // Definition shape — one multi-target "creatures you control" slot.
    // -----------------------------------------------------------------------

    [Fact]
    public void EerieInterlude_Definition_HasOneMultiTargetCreatureSlot()
    {
        var def = BuildEerieInterlude();

        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(0, "\"any number of\" — declining is legal");
        tr.MaxTargets.Should().BeGreaterThan(1, "\"any number of target creatures\"");
        tr.Description.Should().Contain("creature");
    }

    // -----------------------------------------------------------------------
    // The "for many" path — exile a batch, return the whole batch at end step.
    // -----------------------------------------------------------------------

    [Fact]
    public void EerieInterlude_ExilesManyCreatures_ThenReturnsThemAllAtNextEndStep()
    {
        var a = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");
        var b = NewControlledCreature(_alice, "Sanctuary Cat", "{W}");
        var c = NewControlledCreature(_alice, "Savannah Lions", "{W}");

        var def = BuildEerieInterlude();
        ExecuteCast(def, a, b, c);

        a.Zone.Should().Be(ZoneType.Exile, "CR 701.21 — exile fires immediately");
        b.Zone.Should().Be(ZoneType.Exile);
        c.Zone.Should().Be(ZoneType.Exile);

        FireEndStepReturn();

        a.Zone.Should().Be(ZoneType.Battlefield,
            "CR 603.7 + CR 614 — the whole batch returns at the next end step");
        b.Zone.Should().Be(ZoneType.Battlefield);
        c.Zone.Should().Be(ZoneType.Battlefield);
        a.Controller.Should().BeSameAs(_alice, "CR 614 — under its owner's control");
        b.Controller.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // No +1/+1 counter for Eerie Interlude (distinct from Otherworldly
        // Journey / Long Road Home).
        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void EerieInterlude_DeclinedTargets_ResolvesToNoOp()
    {
        var def = BuildEerieInterlude();
        // "any number" — choosing zero targets is legal; nothing is exiled,
        // nothing is scheduled to return.
        ExecuteCast(def /* no targets */);

        _triggers.PendingCount.Should().Be(0);
        FireEndStepReturn();
        _triggers.PendingCount.Should().Be(0,
            "an empty batch schedules no delayed return");
    }

    [Fact]
    public void EerieInterlude_ReturnDoesNotFireUntilEndStep()
    {
        var bear = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");

        var def = BuildEerieInterlude();
        ExecuteCast(def, bear);

        bear.Zone.Should().Be(ZoneType.Exile);

        // A non-End step starting does NOT fire the delayed return.
        _bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        _triggers.PendingCount.Should().Be(0,
            "CR 603.7 — the return is gated to the End step only");
        bear.Zone.Should().Be(ZoneType.Exile);

        FireEndStepReturn();
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void EerieInterlude_DodgesABoardWipe_BetweenExileAndReturn()
    {
        // The signature use: exile your creatures in response to a Wrath, they
        // miss it, and come back at the end step. We model "they're in exile
        // while the wipe resolves" — the exiled cards are untouched.
        var bear = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");

        var def = BuildEerieInterlude();
        ExecuteCast(def, bear);

        bear.Zone.Should().Be(ZoneType.Exile,
            "while exiled, the creature is not on the battlefield to be wrathed");

        FireEndStepReturn();
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Single-target legality re-check (CR 608.2b) — an exiled-and-returned
    // creature that has since left exile is not returned.
    // -----------------------------------------------------------------------

    [Fact]
    public void EerieInterlude_TargetThatLeftExile_IsNotReturned()
    {
        var bear = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");

        var def = BuildEerieInterlude();
        ExecuteCast(def, bear);
        bear.Zone.Should().Be(ZoneType.Exile);

        // Something else pulls the card out of exile before the end step.
        _alice.Zones.Exile.RemoveCard(bear);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        FireEndStepReturn();
        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 603.7 — the delayed return only acts on cards still in exile");
    }

    // -----------------------------------------------------------------------
    // Shape-only fallback — no registered TriggerManager → exile happens, the
    // delayed return is skipped (same posture as the hand-rolled factories).
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // The counter-on-return rider (Semester's End) — exile a mixed batch of
    // creatures + planeswalkers, return them all at the next end step, each
    // creature with a +1/+1 counter (CR 122.1c) and each planeswalker with a
    // loyalty counter (CR 122.1b). The "for many" generalization of the
    // Otherworldly Journey single-target +1/+1-on-return rider.
    // -----------------------------------------------------------------------

    [Fact]
    public void SemestersEnd_ReturnsCreaturesWithPlusOneCounter_AndPlaneswalkersWithLoyalty()
    {
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");
        var cat = NewControlledCreature(_alice, "Savannah Lions", "{W}");
        var pw = NewControlledPlaneswalker(_alice, "Ajani, Caller of the Pride", "{2}{W}", 4);

        var def = BuildSemestersEnd();
        ExecuteCast(def, bear, cat, pw);

        bear.Zone.Should().Be(ZoneType.Exile);
        cat.Zone.Should().Be(ZoneType.Exile);
        pw.Zone.Should().Be(ZoneType.Exile);

        FireEndStepReturn();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        cat.Zone.Should().Be(ZoneType.Battlefield);
        pw.Zone.Should().Be(ZoneType.Battlefield);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 122.1c — a creature returns with an additional +1/+1 counter");
        cat.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // CR 122.1b — a planeswalker returns with an additional loyalty counter
        // on TOP of its starting loyalty (it re-enters fresh at 4, then +1 = 5).
        pw.Counters.Count(CounterType.Loyalty).Should().Be(1,
            "the rider adds ONE loyalty counter; it is NOT a +1/+1 counter");
        pw.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "a planeswalker gets loyalty, never a +1/+1 counter");
    }

    [Fact]
    public void SemestersEnd_Definition_HasOneMultiTargetCreatureOrPlaneswalkerSlot()
    {
        var def = BuildSemestersEnd();

        def.TargetRequests.Should().HaveCount(1);
        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(0, "\"any number of\" — declining is legal");
        tr.MaxTargets.Should().BeGreaterThan(1);
    }

    [Fact]
    public void EerieInterlude_NoTriggerManager_ExilesButSkipsDelayedReturn()
    {
        TriggerManagerRegistry.Clear();

        var bear = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");

        var def = BuildEerieInterlude();
        ExecuteCast(def, bear);

        bear.Zone.Should().Be(ZoneType.Exile,
            "CR 701.21 — exile still fires; shape-only mode just skips the return");
    }
}

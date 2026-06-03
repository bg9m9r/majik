using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MazemindTomeFactory"/> — the state-triggered
/// counter-threshold (CR 603.8) pay-down.
///
/// Mazemind Tome (Core Set 2021, {2}, Artifact):
///   "{T}, Put a page counter on this artifact: Scry 1."
///   "{2}, {T}, Put a page counter on this artifact: Draw a card."
///   "When there are four or more page counters on this artifact, exile it.
///    If you do, you gain 4 life."
///
/// Covers:
///   - Card identity (name, artifact type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name → an
///     <see cref="Artifact"/>.
///   - Each activated ability puts a page counter; the draw ability draws.
///   - The declarative <see cref="StateWhenCountersGeTriggerDef"/> fires on
///     the rising edge of "≥4 page counters" and is constant-false below.
///   - End-to-end: accruing the 4th page counter, an SBA pass enqueues the
///     state trigger via <see cref="TriggerManager.EvaluateStateChangeTriggers"/>,
///     and resolving it exiles the Tome and gains 4 life.
/// </summary>
public class MazemindTomeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MazemindTome_HasExpectedShape()
    {
        var card = MazemindTomeFactory.Create(_alice);

        card.Name.Should().Be("Mazemind Tome");
        card.ManaCost.Should().Be("{2}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MazemindTome()
    {
        var produced = NamedCardFactory.Create("Mazemind Tome", _alice);

        produced.Should().BeOfType<Artifact>();
        produced.Name.Should().Be("Mazemind Tome");
    }

    // -----------------------------------------------------------------------
    // Activated abilities — page-counter accrual
    // -----------------------------------------------------------------------

    [Fact]
    public void ScryAbility_PutsOnePageCounter()
    {
        var tome = MazemindTomeFactory.Create(_alice);
        PutOnBattlefield(tome);

        // The scry ability is the tap-only activated ability (no mana cost).
        var scry = tome.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in scry.Effects) e.Execute();

        tome.Counters.Count(CounterType.Page).Should().Be(1);
    }

    [Fact]
    public void DrawAbility_PutsOnePageCounter_AndDrawsACard()
    {
        var top = SeedLibraryCard("Top");
        var tome = MazemindTomeFactory.Create(_alice);
        PutOnBattlefield(tome);

        var draw = tome.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in draw.Effects) e.Execute();

        tome.Counters.Count(CounterType.Page).Should().Be(1);
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
    }

    // -----------------------------------------------------------------------
    // State trigger condition (CR 603.8) — declarative parity + rising edge
    // -----------------------------------------------------------------------

    [Fact]
    public void StateTrigger_IsConstantFalse_BelowThreshold()
    {
        var tome = MazemindTomeFactory.Create(_alice);
        var trigger = tome.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition.Should().BeOfType<StateChangeTriggerCondition>();

        var cond = (StateChangeTriggerCondition)trigger.Condition;

        // 0..3 page counters: never satisfied.
        for (var i = 0; i < 3; i++)
        {
            cond.IsSatisfied().Should().BeFalse();
            tome.Counters.Add(CounterType.Page);
        }
        cond.IsSatisfied().Should().BeFalse(); // exactly 3 counters now
    }

    [Fact]
    public void StateTrigger_FiresOnRisingEdge_AtFourCounters()
    {
        var tome = MazemindTomeFactory.Create(_alice);
        var cond = (StateChangeTriggerCondition)
            tome.Abilities.OfType<TriggeredAbility>().Single().Condition;

        tome.Counters.Add(CounterType.Page, 4);
        cond.IsSatisfied().Should().BeTrue();   // rising edge at ≥4
        cond.IsSatisfied().Should().BeFalse();  // stays armed, no re-fire
    }

    [Fact]
    public void DeclarativeTriggerDef_BuildsSameRisingEdgeCondition()
    {
        // The factory's trigger is built from the declarative
        // state_when_counters_ge def (Counter = "Page", Threshold = 4).
        var def = new StateWhenCountersGeTriggerDef
        {
            Counter = CounterType.Page.Name,
            Threshold = 4,
        };
        var tome = MazemindTomeFactory.Create(_alice);

        var cond = def.ToTrigger()(tome);
        cond.Should().BeOfType<StateChangeTriggerCondition>();
        var sc = (StateChangeTriggerCondition)cond;

        sc.IsSatisfied().Should().BeFalse();
        tome.Counters.Add(CounterType.Page, 4);
        sc.IsSatisfied().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // End-to-end — SBA pass enqueues the trigger; resolution exiles + 4 life
    // -----------------------------------------------------------------------

    [Fact]
    public void ReachingFourCounters_EnqueuesTrigger_ThenExilesAndGainsFourLife()
    {
        var stack = new Majik.Core.Stack.Stack();
        var triggers = new TriggerManager(stack);
        var tome = MazemindTomeFactory.Create(_alice, triggers);
        PutOnBattlefield(tome);

        // Below threshold: an SBA-style state-trigger pass enqueues nothing.
        tome.Counters.Add(CounterType.Page, 3);
        triggers.EvaluateStateChangeTriggers();
        triggers.PendingCount.Should().Be(0);

        // Cross the threshold (4th page counter), then the state-trigger
        // evaluation pass (run by StateBasedActions after each SBA pass,
        // CR 603.8 / CR 704) enqueues the threshold trigger exactly once.
        tome.Counters.Add(CounterType.Page);
        triggers.EvaluateStateChangeTriggers();
        triggers.PendingCount.Should().Be(1);

        // Put it on the stack and resolve — CR 603.8 "exile it. If you do,
        // gain 4 life."
        triggers.PutPendingTriggersOnStack(_alice);
        var ability = (TriggeredAbility)stack.Pop()!;
        ability.Resolve();

        tome.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(tome);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(tome);
        _alice.LifeTotal.Should().Be(24);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private void PutOnBattlefield(Artifact tome)
    {
        _alice.Zones.Battlefield.AddCard(tome);
        tome.SetZone(ZoneType.Battlefield);
    }
}

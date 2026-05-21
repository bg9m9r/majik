using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// Tests for CR 701.59 — Earthbend N keyword action.
/// </summary>
public class EarthbendActionTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    private Land MakeForest()
    {
        var forest = new Land("Forest")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(forest);
        return forest;
    }

    // -----------------------------------------------------------------------
    // Structural tests — no trigger harness needed
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 701.59a — target land gains Creature type and keeps its Land type.
    /// CR 701.59b — N +1/+1 counters placed.
    /// CR 702.10 — Haste keyword marker attached.
    /// </summary>
    [Fact]
    public void Apply_ConvertsLandToCreatureWithCounters()
    {
        var forest = MakeForest();

        var result = EarthbendAction.Apply(_alice, 1);

        result.Should().BeSameAs(forest);
        forest.HasType(CardType.Creature).Should().BeTrue("Earthbend grants the Creature type");
        forest.HasType(CardType.Land).Should().BeTrue("the land retains its Land type (CR 701.59a)");
        forest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        forest.Abilities.OfType<KeywordAbility>()
              .Should().Contain(k => k.Keyword == "Haste", "Earthbend grants Haste (CR 702.10)");
    }

    [Fact]
    public void Apply_ThreeCounters_LandHasThreePlusPlusCounters()
    {
        var forest = MakeForest();

        EarthbendAction.Apply(_alice, 3);

        forest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    /// <summary>
    /// A TriggeredAbility representing the return-tapped trigger must have been
    /// attached to the land after Apply.
    /// </summary>
    [Fact]
    public void Apply_AttachesReturnTriggerToLand()
    {
        var forest = MakeForest();

        EarthbendAction.Apply(_alice, 2);

        forest.Abilities.OfType<TriggeredAbility>()
              .Should().HaveCount(1, "exactly one return-tapped triggered ability must be attached");
    }

    // -----------------------------------------------------------------------
    // Guard / null-return cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Apply_NoLandsOnBattlefield_ReturnsNull()
    {
        // Controller has no lands.
        var result = EarthbendAction.Apply(_alice, 1);

        result.Should().BeNull();
    }

    [Fact]
    public void Apply_ZeroN_ReturnsNullWithoutMutating()
    {
        var forest = MakeForest();

        var result = EarthbendAction.Apply(_alice, 0);

        result.Should().BeNull("n <= 0 is a no-op per CR 701.59 — no valid Earthbend value");
        forest.HasType(CardType.Creature).Should().BeFalse("land must not be mutated when n = 0");
    }

    [Fact]
    public void Apply_NullController_Throws()
    {
        Action act = () => EarthbendAction.Apply(null!, 1);

        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Integration: return-tapped trigger fires on death via ZoneService
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 701.59c — when the earthbended land moves to the graveyard (dies),
    /// the delayed trigger fires and returns it to the battlefield tapped.
    /// </summary>
    [Fact]
    public void EarthbendedLand_DiesAndReturnsTapped()
    {
        var stack  = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones  = new ZoneService(_bus);

        var forest = MakeForest();
        EarthbendAction.Apply(_alice, 2);
        triggers.BindCard(forest);

        // Simulate death: land moves from battlefield to graveyard.
        zones.MoveCardTo(forest, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "return-tapped trigger must queue when the land dies");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            "the land returns to the battlefield (CR 701.59c)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(forest);
        forest.IsTapped.Should().BeTrue("the land returns tapped (CR 701.59c)");
    }

    /// <summary>
    /// CR 701.59c — when the earthbended land is exiled, the trigger fires
    /// and returns it to the battlefield tapped.
    /// </summary>
    [Fact]
    public void EarthbendedLand_ExiledAndReturnsTapped()
    {
        var stack  = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones  = new ZoneService(_bus);

        var forest = MakeForest();
        EarthbendAction.Apply(_alice, 1);
        triggers.BindCard(forest);

        // Simulate exile.
        zones.MoveCardTo(forest, ZoneType.Exile);

        triggers.PendingCount.Should().Be(1, "return-tapped trigger must queue when the land is exiled");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        forest.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Exile.GetCards().Should().NotContain(forest);
        forest.IsTapped.Should().BeTrue();
    }
}

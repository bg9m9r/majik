using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Young Wolf (Innistrad, {G}):
///   - 1/1 Wolf shape, {G} cost, Undying keyword marker present.
///   - Dies with 0 +1/+1 counters → returns to battlefield with a +1/+1
///     counter (CR 702.93b).
///   - Dies with a +1/+1 counter → stays dead (CR 702.93 / 603.4 — intervening-if).
///   - Bounce / exile bypass Undying (only Battlefield → Graveyard triggers).
///   - Dispatcher entry on NamedCardFactory returns a Young Wolf.
/// </summary>
[Trait("Color", "G")]
public class YoungWolfFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    private Creature MakeWolfOnBattlefield()
    {
        var wolf = YoungWolfFactory.Create(_alice);
        wolf.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wolf);
        return wolf;
    }

    // ------------------------------------------------------------------
    // Shape
    // ------------------------------------------------------------------

    [Fact]
    public void YoungWolf_Shape_Is1OverOneWolfWithUndyingKeyword()
    {
        var wolf = YoungWolfFactory.Create(_alice);

        wolf.Name.Should().Be("Young Wolf");
        wolf.ManaCost.ToString().Should().Be("{G}");
        wolf.Power.Should().Be(1);
        wolf.Toughness.Should().Be(1);
        wolf.HasSubtype(CardSubtype.Wolf).Should().BeTrue();
        wolf.Owner.Should().Be(_alice);
        wolf.Controller.Should().Be(_alice);

        // Undying keyword marker is present (CR 702.93).
        wolf.Abilities
            .OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Undying");
    }

    // ------------------------------------------------------------------
    // Undying — dies without +1/+1 counter → returns with one
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 702.93b — when Young Wolf dies with no +1/+1 counters, it returns
    /// to the battlefield under its owner's control with one +1/+1 counter.
    /// </summary>
    [Fact]
    public void YoungWolf_DiesWithNoCounters_ReturnsToBattlefieldWithPlusOneCounter()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wolf = MakeWolfOnBattlefield();
        triggers.BindCard(wolf);

        // Simulate death via ZoneService (fires CardMovedEvent).
        zones.MoveCardTo(wolf, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1,
            "Undying trigger must queue on Battlefield → Graveyard death");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Wolf should be back on the battlefield.
        wolf.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(wolf);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(wolf);

        // Wolf should have exactly one +1/+1 counter (becoming an effective 2/2).
        wolf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        wolf.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0,
            "counter bag should be clean — only the one +1/+1 counter added by Undying");
    }

    // ------------------------------------------------------------------
    // Undying interveningIf — dies WITH +1/+1 counter → stays dead
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 702.93 + CR 603.4 — "if it had no +1/+1 counters on it": a creature
    /// that already carried a +1/+1 counter when it died does NOT return.
    /// The interveningIf gates the trigger from going on the stack.
    /// </summary>
    [Fact]
    public void YoungWolf_DiesWithPlusOneCounter_StaysInGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wolf = MakeWolfOnBattlefield();
        triggers.BindCard(wolf);

        // Give the wolf a +1/+1 counter before it dies.
        wolf.Counters.Add(CounterType.PlusOnePlusOne, 1);
        wolf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        zones.MoveCardTo(wolf, ZoneType.Graveyard);

        // InterveningIf fails — trigger must NOT go on the stack.
        triggers.PendingCount.Should().Be(0,
            "Undying must not trigger when a +1/+1 counter was present at death");

        wolf.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(wolf);
    }

    // ------------------------------------------------------------------
    // Undying only triggers on death (Battlefield → Graveyard)
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 702.93b only triggers on "dies" — the Battlefield → Graveyard
    /// transition (CR 700.4). Bouncing the wolf to its owner's hand must
    /// NOT trigger Undying.
    /// </summary>
    [Fact]
    public void YoungWolf_BouncedToHand_DoesNotTriggerUndying()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wolf = MakeWolfOnBattlefield();
        triggers.BindCard(wolf);

        // Bounce: battlefield → hand (NOT graveyard).
        zones.MoveCardTo(wolf, ZoneType.Hand);

        triggers.PendingCount.Should().Be(0,
            "Undying must not trigger on non-death zone changes (bounce to hand)");

        wolf.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(wolf);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(wolf);
    }

    /// <summary>
    /// CR 702.93b only triggers on "dies" — exile via removal effects (e.g.
    /// Path to Exile) bypasses the graveyard and must NOT trigger Undying.
    /// </summary>
    [Fact]
    public void YoungWolf_ExiledFromBattlefield_DoesNotTriggerUndying()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wolf = MakeWolfOnBattlefield();
        triggers.BindCard(wolf);

        // Exile directly from the battlefield (skips graveyard).
        zones.MoveCardTo(wolf, ZoneType.Exile);

        triggers.PendingCount.Should().Be(0,
            "Undying must not trigger on Battlefield → Exile (not a death per CR 700.4)");

        wolf.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(wolf);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(wolf);
    }

    // ------------------------------------------------------------------
    // Dispatcher
    // ------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_YoungWolf_ReturnsWolfWithUndying()
    {
        var card = NamedCardFactory.Create("Young Wolf", _alice);

        card.Should().BeOfType<Creature>();
        var wolf = (Creature)card;

        wolf.Name.Should().Be("Young Wolf");
        wolf.Power.Should().Be(1);
        wolf.Toughness.Should().Be(1);
        wolf.HasSubtype(CardSubtype.Wolf).Should().BeTrue();

        wolf.Abilities
            .OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Undying");
    }
}

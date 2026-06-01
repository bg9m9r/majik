using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// Tests for CR 701.59 — Earthbend N keyword action + the animate-land
/// continuous effect it drives.
/// </summary>
public class EarthbendActionTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    private Land MakeForest(ContinuousEffectsService? svc = null)
    {
        var forest = new Land("Forest")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        if (svc != null) forest.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(forest);
        return forest;
    }

    // -----------------------------------------------------------------------
    // Animate-land continuous effect (CR 701.59a/b — surfaces through Compute)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 701.59a — target land becomes a 0/0 Elemental creature with haste
    /// that's still a land. CR 701.59b — N=1 +1/+1 counter → a 1/1. All of
    /// it surfaces through the layer system's creature-row upgrade.
    /// </summary>
    [Fact]
    public void Apply_AnimatesLandToCreature_PTSurfacesThroughCompute()
    {
        var svc = new ContinuousEffectsService();
        var forest = MakeForest(svc);

        var result = EarthbendAction.Apply(_alice, 1, svc);

        result.Should().BeSameAs(forest);

        var chars = svc.Compute(forest);
        chars.Should().BeOfType<CreatureCharacteristics>("the Creature grant upgrades the row");
        chars.Types.Should().Contain(CardType.Creature, "Earthbend grants the Creature type (CR 701.59a)");
        chars.Types.Should().Contain(CardType.Land, "the land retains its Land type (still a land)");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental, "Earthbend makes it an Elemental (CR 701.59a)");
        chars.Keywords.Should().Contain("Haste", "Earthbend grants Haste (CR 702.10)");

        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(1, "0/0 base + one +1/+1 counter = 1/1");
        cc.Toughness.Should().Be(1);

        forest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Apply_ThreeCounters_AnimatedLandIsThreeThree()
    {
        var svc = new ContinuousEffectsService();
        var forest = MakeForest(svc);

        EarthbendAction.Apply(_alice, 3, svc);

        forest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
        var cc = (CreatureCharacteristics)svc.Compute(forest);
        cc.Power.Should().Be(3, "0/0 base + three +1/+1 counters = 3/3");
        cc.Toughness.Should().Be(3);
    }

    /// <summary>
    /// The animation self-terminates when the land leaves the battlefield
    /// (CR 613 IsActive gate). Compute then reflects the plain land again.
    /// </summary>
    [Fact]
    public void Apply_AnimationEndsWhenLandLeavesBattlefield()
    {
        var svc = new ContinuousEffectsService();
        var forest = MakeForest(svc);
        EarthbendAction.Apply(_alice, 1, svc);
        ((CreatureCharacteristics)svc.Compute(forest)).Power.Should().Be(1);

        forest.SetZone(ZoneType.Graveyard);

        svc.Compute(forest).Types.Should().NotContain(CardType.Creature,
            "the animate effect is inactive off the battlefield");
    }

    /// <summary>
    /// EarthbendAction reads the land's own ActiveEffects when no explicit
    /// service is supplied (the Badgermole ETB closure passes the card's CES,
    /// but a land already wired to a CES animates correctly either way).
    /// </summary>
    [Fact]
    public void Apply_UsesLandActiveEffects_WhenNoExplicitServicePassed()
    {
        var svc = new ContinuousEffectsService();
        var forest = MakeForest(svc);

        EarthbendAction.Apply(_alice, 1); // no explicit service → uses land.ActiveEffects

        ((CreatureCharacteristics)svc.Compute(forest)).Power.Should().Be(1);
    }

    /// <summary>
    /// A TriggeredAbility representing the return-tapped trigger must be
    /// attached to the land after Apply.
    /// </summary>
    [Fact]
    public void Apply_AttachesReturnTriggerToLand()
    {
        var svc = new ContinuousEffectsService();
        var forest = MakeForest(svc);

        EarthbendAction.Apply(_alice, 2, svc);

        forest.Abilities.OfType<TriggeredAbility>()
              .Should().HaveCount(1, "exactly one return-tapped triggered ability must be attached");
    }

    // -----------------------------------------------------------------------
    // Guard / null-return cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Apply_NoLandsOnBattlefield_ReturnsNull()
    {
        var result = EarthbendAction.Apply(_alice, 1);

        result.Should().BeNull();
    }

    [Fact]
    public void Apply_ZeroN_ReturnsNullWithoutMutating()
    {
        var svc = new ContinuousEffectsService();
        var forest = MakeForest(svc);

        var result = EarthbendAction.Apply(_alice, 0, svc);

        result.Should().BeNull("n <= 0 is a no-op per CR 701.59 — no valid Earthbend value");
        svc.Compute(forest).Types.Should().NotContain(CardType.Creature,
            "land must not be mutated when n = 0");
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

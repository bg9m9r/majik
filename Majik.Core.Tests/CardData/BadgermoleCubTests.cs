using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BadgermoleCubFactory"/>.
///
/// Badgermole Cub — Creature — Bear {G} 1/1.
/// "When this creature enters, earthbend 1." (+ a deferred tap-for-mana clause).
///
/// Covers card identity, the ETB Earthbend-1 trigger shape (1..1 "target land
/// you control"), and resolution: the chosen land gets a +1/+1 counter and is
/// animated into a 1/1 Elemental creature with haste that's still a land.
/// </summary>
public class BadgermoleCubTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BadgermoleCub_Identity()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        b.Name.Should().Be("Badgermole Cub");
        b.HasType(CardType.Creature).Should().BeTrue();
        b.HasSubtype(CardSubtype.Bear).Should().BeTrue("Badgermole Cub is a Bear");
        b.BasePower.Should().Be(1);
        b.BaseToughness.Should().Be(1);
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BadgermoleCub_DispatchesViaNamedCardFactory()
    {
        var b = NamedCardFactory.Create("Badgermole Cub", _alice);

        b.Should().BeOfType<Creature>();
        b.Name.Should().Be("Badgermole Cub");
        b.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the source-gen dispatcher routes through Create(owner), attaching the ETB trigger");
    }

    [Fact]
    public void BadgermoleCub_HasEarthbendEtbTrigger()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        var etb = b.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("land", "Earthbend targets a land you control");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void BadgermoleCub_EtbTargetGatherer_OnlyControllersLands()
    {
        var bob = new Player("Bob", 20);
        var myForest = MakeLandFor(_alice, "Forest");
        var oppForest = MakeLandFor(bob, "Forest");

        var b = BadgermoleCubFactory.Create(_alice);
        var etb = b.Abilities.OfType<TriggeredAbility>().Single();
        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            _alice, new[] { _alice, bob }, _alice, 1,
            Majik.Core.StateMachine.PhaseStateType.PreCombatMain, stack);

        var candidates = etb.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(myForest, "you can target a land you control");
        candidates.Should().NotContain(oppForest, "Earthbend can't target an opponent's land");
    }

    [Fact]
    public void BadgermoleCub_EtbResolution_AnimatesChosenLandToOneOne()
    {
        var svc = new ContinuousEffectsService();
        var forest = MakeLandFor(_alice, "Forest", svc);

        var b = BadgermoleCubFactory.Create(_alice);
        b.ActiveEffects = svc; // prod build wires the creature's CES
        var etb = b.Abilities.OfType<TriggeredAbility>().Single();

        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { forest } });
        foreach (var effect in etb.Effects) effect.Execute();

        forest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Earthbend 1 puts one +1/+1 counter on the land (CR 701.59b)");

        var chars = svc.Compute(forest);
        chars.Should().BeOfType<CreatureCharacteristics>();
        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Land, "still a land (CR 701.59a)");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);
        chars.Keywords.Should().Contain("Haste");

        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(1, "0/0 base + one +1/+1 counter = 1/1");
        cc.Toughness.Should().Be(1);

        forest.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the return-tapped delayed trigger is attached to the land (CR 701.59c)");
    }

    [Fact]
    public void BadgermoleCub_EtbResolution_NoTarget_IsNoOp()
    {
        var svc = new ContinuousEffectsService();
        var b = BadgermoleCubFactory.Create(_alice);
        b.ActiveEffects = svc;
        var etb = b.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no chosen target → no-op");
    }

    private Land MakeLandFor(Player p, string name, ContinuousEffectsService? svc = null)
    {
        var land = new Land(name) { Owner = p, Controller = p, Zone = ZoneType.Battlefield };
        if (svc != null) land.ActiveEffects = svc;
        p.Zones.Battlefield.AddCard(land);
        return land;
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Empyrean Eagle (Modern Horizons, {1}{W}{U}).
///
/// Oracle: "Flying. Other creatures you control with flying get +1/+1."
///
/// Covers:
///   - Identity (name, 2/3, Bird + Spirit subtypes, mana cost, Flying).
///   - NamedCardFactory dispatch.
///   - Keyword-gated anthem (CR 613.7c, Layer 7c) gated on EFFECTIVE flying:
///       * controller's other flyer gets +1/+1;
///       * controller's ground creature does NOT;
///       * granting that ground creature flying (Layer 6) makes it qualify;
///       * the Eagle does not pump itself via its own effect ("Other");
///       * a second Eagle pumps the first (both 3/4), each excluding itself;
///       * opponent's flyer is NOT pumped (controller-scoped);
///       * LTB lifts the bonus.
/// </summary>
public class EmpyreanEagleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Flyer(string name, int p, int t, Player ctrl, ContinuousEffectsService svc)
    {
        var c = new Creature(name, "1U", p, t) { Owner = ctrl, Controller = ctrl, ActiveEffects = svc };
        c.AddAbility(new KeywordAbility("Flying", c, ctrl));
        c.SetZone(ZoneType.Battlefield);
        ctrl.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature Grounder(string name, int p, int t, Player ctrl, ContinuousEffectsService svc)
    {
        var c = new Creature(name, "1G", p, t) { Owner = ctrl, Controller = ctrl, ActiveEffects = svc };
        c.SetZone(ZoneType.Battlefield);
        ctrl.Zones.Battlefield.AddCard(c);
        return c;
    }

    private Creature Eagle(ContinuousEffectsService svc)
    {
        var eagle = EmpyreanEagleFactory.Create(_alice, svc);
        eagle.ActiveEffects = svc;
        eagle.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(eagle);
        return eagle;
    }

    [Fact]
    public void Identity()
    {
        var c = EmpyreanEagleFactory.Create(_alice);

        c.Name.Should().Be("Empyrean Eagle");
        c.ManaCost.Should().Be("{1}{W}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying");
    }

    [Fact]
    public void DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Empyrean Eagle", _alice);
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Empyrean Eagle");
    }

    [Fact]
    public void PumpsControllerFlyer_NotGroundCreature()
    {
        var svc = new ContinuousEffectsService();
        var eagle = Eagle(svc);
        var flyer = Flyer("Flyer", 1, 1, _alice, svc);
        var grounder = Grounder("Grounder", 1, 1, _alice, svc);

        flyer.Power.Should().Be(2);
        flyer.Toughness.Should().Be(2);
        grounder.Power.Should().Be(1);
        grounder.Toughness.Should().Be(1);
        // Eagle is a flyer but "Other ..." excludes it from its own static.
        eagle.Power.Should().Be(2);
        eagle.Toughness.Should().Be(3);
    }

    [Fact]
    public void GrantingFlyingMakesGroundCreatureQualify()
    {
        var svc = new ContinuousEffectsService();
        Eagle(svc);
        var grounder = Grounder("Grounder", 1, 1, _alice, svc);
        grounder.Power.Should().Be(1);

        svc.Register(new GrantKeywordUntilEndOfTurnEffect(grounder, "Flying"));

        grounder.Power.Should().Be(2);
        grounder.Toughness.Should().Be(2);
    }

    [Fact]
    public void OpponentFlyer_NotPumped()
    {
        var svc = new ContinuousEffectsService();
        Eagle(svc);
        var bobFlyer = Flyer("BobFlyer", 1, 1, _bob, svc);

        bobFlyer.Power.Should().Be(1);
        bobFlyer.Toughness.Should().Be(1);
    }

    [Fact]
    public void TwoEagles_EachPumpsTheOther_BothBecome3_4()
    {
        var svc = new ContinuousEffectsService();
        var eagleA = Eagle(svc);
        var eagleB = Eagle(svc);

        // Each Eagle has flying; each pumps the OTHER (not itself).
        eagleA.Power.Should().Be(3);
        eagleA.Toughness.Should().Be(4);
        eagleB.Power.Should().Be(3);
        eagleB.Toughness.Should().Be(4);
    }

    [Fact]
    public void LtbLiftsTheBonus()
    {
        var svc = new ContinuousEffectsService();
        var eagle = Eagle(svc);
        var flyer = Flyer("Flyer", 1, 1, _alice, svc);
        flyer.Power.Should().Be(2);

        eagle.SetZone(ZoneType.Graveyard);
        flyer.Power.Should().Be(1);
        flyer.Toughness.Should().Be(1);
    }
}

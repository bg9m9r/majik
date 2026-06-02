using FluentAssertions;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Keyword-gated anthem static (CR 613.4 / 613.7c Layer 7c): "Other creatures
/// you control with [keyword] get +N/+N." The affected set is the source's
/// controller's OTHER creatures whose EFFECTIVE keyword set (post-Layer-6, so
/// a granted keyword counts — CR 613.8 dependency) contains the gating
/// keyword. Exercised through <see cref="LordStaticEffect"/>'s
/// <c>matchingKeyword</c> variant.
/// </summary>
public class KeywordGatedAnthemEffectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature Creature(string name, int p, int t, Player ctrl, ContinuousEffectsService svc)
    {
        var c = new Creature(name, "1G", p, t) { Owner = ctrl, Controller = ctrl, ActiveEffects = svc };
        c.SetZone(ZoneType.Battlefield);
        ctrl.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static LordStaticEffect FlyingAnthem(Creature source) =>
        new(source: source, matchingKeyword: "Flying", power: 1, toughness: 1,
            includeSelf: false, opponentsOnly: false);

    [Fact]
    public void PumpsControllerFlyer_NotGroundCreature()
    {
        var svc = new ContinuousEffectsService();
        var lord = Creature("Lord", 2, 2, _alice, svc);
        lord.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", lord, _alice));

        var flyer = Creature("Flyer", 1, 1, _alice, svc);
        flyer.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", flyer, _alice));

        var grounder = Creature("Grounder", 1, 1, _alice, svc);

        svc.Register(FlyingAnthem(lord));

        flyer.Power.Should().Be(2);
        flyer.Toughness.Should().Be(2);
        grounder.Power.Should().Be(1);
        grounder.Toughness.Should().Be(1);
    }

    [Fact]
    public void DoesNotPumpOpponentFlyer()
    {
        var svc = new ContinuousEffectsService();
        var lord = Creature("Lord", 2, 2, _alice, svc);
        lord.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", lord, _alice));

        var bobFlyer = Creature("BobFlyer", 1, 1, _bob, svc);
        bobFlyer.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", bobFlyer, _bob));

        svc.Register(FlyingAnthem(lord));

        bobFlyer.Power.Should().Be(1);
        bobFlyer.Toughness.Should().Be(1);
    }

    [Fact]
    public void GrantingFlyingToGroundCreature_StartsPumpingIt_LiveReevaluation()
    {
        var svc = new ContinuousEffectsService();
        var lord = Creature("Lord", 2, 2, _alice, svc);
        lord.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", lord, _alice));

        var grounder = Creature("Grounder", 1, 1, _alice, svc);
        svc.Register(FlyingAnthem(lord));

        // Initially grounded → not pumped.
        grounder.Power.Should().Be(1);

        // CR 613.8 — grant Flying (Layer 6). The anthem (Layer 7c) reads the
        // effective keyword set AFTER the grant, so the grounder now qualifies.
        svc.Register(new GrantKeywordUntilEndOfTurnEffect(grounder, "Flying"));

        grounder.Power.Should().Be(2);
        grounder.Toughness.Should().Be(2);
    }

    [Fact]
    public void OtherClause_LordDoesNotPumpItselfViaOwnEffect()
    {
        var svc = new ContinuousEffectsService();
        var lord = Creature("Lord", 2, 2, _alice, svc);
        lord.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", lord, _alice));

        svc.Register(FlyingAnthem(lord));

        // Even though the lord itself has flying, "Other ..." excludes it.
        lord.Power.Should().Be(2);
        lord.Toughness.Should().Be(2);
    }

    [Fact]
    public void TwoFlyingLords_EachPumpsTheOther_NeitherPumpsItself()
    {
        var svc = new ContinuousEffectsService();
        var lordA = Creature("LordA", 2, 2, _alice, svc);
        lordA.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", lordA, _alice));
        var lordB = Creature("LordB", 2, 2, _alice, svc);
        lordB.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", lordB, _alice));

        svc.Register(FlyingAnthem(lordA));
        svc.Register(FlyingAnthem(lordB));

        // Each gets +1/+1 from the OTHER lord (but not from its own effect).
        lordA.Power.Should().Be(3);
        lordA.Toughness.Should().Be(3);
        lordB.Power.Should().Be(3);
        lordB.Toughness.Should().Be(3);
    }

    [Fact]
    public void LiftsWhenSourceLeavesBattlefield()
    {
        var svc = new ContinuousEffectsService();
        var lord = Creature("Lord", 2, 2, _alice, svc);
        lord.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", lord, _alice));
        var flyer = Creature("Flyer", 1, 1, _alice, svc);
        flyer.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", flyer, _alice));

        svc.Register(FlyingAnthem(lord));
        flyer.Power.Should().Be(2);

        lord.SetZone(ZoneType.Graveyard);
        flyer.Power.Should().Be(1);
        flyer.Toughness.Should().Be(1);
    }
}

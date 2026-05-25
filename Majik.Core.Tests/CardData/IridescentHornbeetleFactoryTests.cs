using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="IridescentHornbeetleFactory"/>.
///
/// Card: Iridescent Hornbeetle — Creature — Insect Beast 2/4 {3}{G}
/// (Foundations).
///   "Whenever one or more +1/+1 counters are placed on a creature you
///    control, create a 1/1 green Insect creature token."
///
/// Covers:
///   - Identity / dispatch (Creature — Insect Beast, {3}{G}, 2/4).
///   - Triggered ability is attached for shape regardless of TriggerManager.
///   - Trigger matches CounterAddedEvent for +1/+1 on a creature you control.
///   - Trigger rejects -1/-1 counters, non-creature targets, opponent's creatures.
///   - On fire: a 1/1 green Insect token is created on the controller's
///     battlefield.
/// </summary>
public class IridescentHornbeetleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void IridescentHornbeetle_Identity()
    {
        var c = IridescentHornbeetleFactory.Create(_alice);

        c.Name.Should().Be("Iridescent Hornbeetle");
        c.ManaCost.Should().Be("{3}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_IridescentHornbeetle()
    {
        var card = NamedCardFactory.Create("Iridescent Hornbeetle", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Iridescent Hornbeetle");
        card.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
    }

    [Fact]
    public void Hornbeetle_HasTrigger_AttachedAsAbility()
    {
        var c = IridescentHornbeetleFactory.Create(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Trigger condition — +1/+1 on a creature you control fires.
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_MatchesPlusOneOnControlledCreature()
    {
        var hornbeetle = IridescentHornbeetleFactory.Create(_alice);
        PlaceOnBattlefield(hornbeetle, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var evt = new CounterAddedEvent(bear, CounterType.PlusOnePlusOne, 1);
        var trig = hornbeetle.Abilities.OfType<TriggeredAbility>().First();

        trig.Condition.Matches(evt, trig).Should().BeTrue();
    }

    [Fact]
    public void Trigger_DoesNotMatch_OpponentsCreature()
    {
        var hornbeetle = IridescentHornbeetleFactory.Create(_alice);
        PlaceOnBattlefield(hornbeetle, _alice);

        var bobsBear = new Creature("Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobsBear);
        bobsBear.SetZone(ZoneType.Battlefield);

        var evt = new CounterAddedEvent(bobsBear, CounterType.PlusOnePlusOne, 1);
        var trig = hornbeetle.Abilities.OfType<TriggeredAbility>().First();

        trig.Condition.Matches(evt, trig).Should().BeFalse(
            "the printed clause is 'creature you control'");
    }

    [Fact]
    public void Trigger_DoesNotMatch_MinusOneCounter()
    {
        var hornbeetle = IridescentHornbeetleFactory.Create(_alice);
        PlaceOnBattlefield(hornbeetle, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var evt = new CounterAddedEvent(bear, CounterType.MinusOneMinusOne, 1);
        var trig = hornbeetle.Abilities.OfType<TriggeredAbility>().First();

        trig.Condition.Matches(evt, trig).Should().BeFalse(
            "only +1/+1 counters fire the trigger");
    }

    [Fact]
    public void Trigger_DoesNotMatch_NonCreatureTarget()
    {
        var hornbeetle = IridescentHornbeetleFactory.Create(_alice);
        PlaceOnBattlefield(hornbeetle, _alice);

        var artifact = new Artifact("Charge Artifact", "{2}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var evt = new CounterAddedEvent(artifact, CounterType.PlusOnePlusOne, 1);
        var trig = hornbeetle.Abilities.OfType<TriggeredAbility>().First();

        trig.Condition.Matches(evt, trig).Should().BeFalse(
            "the printed clause says 'creature you control', not 'permanent'");
    }

    // -----------------------------------------------------------------------
    // Effect — creates a 1/1 green Insect token on controller's BF.
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_OnFire_CreatesGreenInsectToken()
    {
        var hornbeetle = IridescentHornbeetleFactory.Create(_alice);
        PlaceOnBattlefield(hornbeetle, _alice);

        var startingTokenCount = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(c => c.Name == "Insect");

        var trig = hornbeetle.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in trig.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.Name == "Insect").ToList();
        tokens.Should().HaveCount(startingTokenCount + 1);

        var token = tokens.Last();
        token.IsToken.Should().BeTrue();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Trigger_NoOp_WhenSourceNotOnBattlefield()
    {
        var hornbeetle = IridescentHornbeetleFactory.Create(_alice);
        // Don't place on the battlefield — leave in hand.
        hornbeetle.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(hornbeetle);

        var trig = hornbeetle.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in trig.Effects) e.Execute();

        // No tokens minted because source isn't on the battlefield.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.Name == "Insect").Should().BeEmpty();
    }

    private static void PlaceOnBattlefield(Creature c, Player owner)
    {
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }
}

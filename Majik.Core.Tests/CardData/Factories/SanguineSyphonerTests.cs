using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SanguineSyphonerFactory"/> (Innistrad: Crimson
/// Vow, {1}{B}).
///
/// Covers the card's UNIQUE behaviour — the attack-trigger drain (CR 508.1f /
/// 119.3): "Whenever this creature attacks, each opponent loses 1 life and you
/// gain 1 life." Plus a single identity assert (mana cost / P-T / subtypes).
/// NamedCardFactory dispatch + well-formedness are covered automatically by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "B")]
public class SanguineSyphonerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SanguineSyphoner_Identity()
    {
        var c = SanguineSyphonerFactory.Create(_alice);

        c.Name.Should().Be("Sanguine Syphoner");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AttackTrigger_FiresWhenThisCreatureAttacks_NotOthers()
    {
        var c = SanguineSyphonerFactory.Create(_alice);
        var other = new Creature("Other", "{B}", 1, 1, subtypes: new[] { CardSubtype.Vampire });
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // This creature attacks — matches.
        trigger.Condition.Matches(new CreatureAttacksEvent(c, _bob), trigger).Should().BeTrue();
        // A different creature attacks — does NOT match (per-attacker, CR 508.1f).
        trigger.Condition.Matches(new CreatureAttacksEvent(other, _bob), trigger).Should().BeFalse();
    }

    [Fact]
    public void AttackTrigger_EachOpponentLosesOne_AndControllerGainsOne()
    {
        var c = SanguineSyphonerFactory.Create(_alice, eventBus: null, triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // Resolve through a live game (resolver-null bug-class fix).
        Majik.Core.Tests.Helpers.ContextResolve.Resolve(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life when Sanguine Syphoner attacks");
        _alice.LifeTotal.Should().Be(21, "the controller gains 1 life when Sanguine Syphoner attacks");
    }

    [Fact]
    public void AttackTrigger_MultipleAttacks_DrainAndGainEachTime()
    {
        // CR 603.2c — the triggered ability resolves once per attack event.
        var c = SanguineSyphonerFactory.Create(_alice, eventBus: null, triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        for (var i = 0; i < 3; i++)
        {
            Majik.Core.Tests.Helpers.ContextResolve.Resolve(trigger, _alice, _alice, _bob);
        }

        _bob.LifeTotal.Should().Be(17, "three resolutions ⇒ -3 life for Bob");
        _alice.LifeTotal.Should().Be(23, "three resolutions ⇒ +3 life for Alice");
    }

    [Fact]
    public void AttackTrigger_OnlyActiveOnBattlefield()
    {
        var c = SanguineSyphonerFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }
}

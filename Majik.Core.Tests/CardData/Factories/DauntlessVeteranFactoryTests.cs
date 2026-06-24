using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DauntlessVeteranFactory"/> (Dominaria United, {1}{W}{W}).
/// Creature — Human Soldier 2/2:
///   "Whenever this creature attacks, creatures you control get +1/+1 until end
///    of turn."
///
/// Covers the card's UNIQUE behaviour — the non-targeted attack-trigger anthem
/// that pumps EVERY creature you control (including itself) +1/+1 until end of
/// turn — plus a single identity assert for the printed 2/2 Human Soldier body.
/// (Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests.)
/// </summary>
[Trait("Color", "W")]
public class DauntlessVeteranFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DauntlessVeteran_Identity()
    {
        var card = DauntlessVeteranFactory.Create(_alice);

        card.Name.Should().Be("Dauntless Veteran");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}{W}");
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
    }

    [Fact]
    public void DauntlessVeteran_HasOneAttackTrigger()
    {
        var card = DauntlessVeteranFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack trigger is attached to the creature shape");
    }

    [Fact]
    public void DauntlessVeteran_AttackTrigger_IsNonTargeted()
    {
        var card = DauntlessVeteranFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().BeEmpty(
            "the anthem is non-targeted — it hits ALL creatures you control");
    }

    [Fact]
    public void DauntlessVeteran_AttackTrigger_PumpsAllCreatures_IncludingItself()
    {
        var effects = new ContinuousEffectsService();
        var card = DauntlessVeteranFactory.Create(_alice, effects, triggers: null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // No "other" qualifier — the Veteran pumps itself too.
        effects.Compute(card).Power.Should().Be(3, "base 2 + 1 from its own +1/+1");
        effects.Compute(card).Toughness.Should().Be(3);
        effects.Compute(bear).Power.Should().Be(3, "base 2 + 1 from +1/+1");
        effects.Compute(bear).Toughness.Should().Be(3);

        // The pump expires at end of turn (CR 514.2 cleanup).
        effects.ExpireEndOfTurn();
        effects.Compute(card).Power.Should().Be(2, "the +1/+1 expired at end of turn");
        effects.Compute(bear).Power.Should().Be(2);
    }

    [Fact]
    public void DauntlessVeteran_AttackTrigger_NoEffectsService_DoesNotThrow()
    {
        var card = DauntlessVeteranFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow("with no effects service the anthem is a clean no-op");
    }
}

using System.Collections.Generic;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FearOfLostTeethFactory"/> — Duskmourn: House of Horror
/// Enchantment Creature — Nightmare {B} 1/1. Oracle text (verified against
/// Scryfall):
///   "When this creature dies, it deals 1 damage to any target and you gain
///    1 life."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: 1/1 black Nightmare Enchantment Creature, {B}.
/// - Dies trigger (CR 603.6c / 700.4): on resolution deals 1 damage to the
///   chosen any-target (CR 115.3) AND the controller gains 1 life (CR 119.3).
///   The card-data contract test already covers NamedCardFactory dispatch +
///   well-formedness.
/// </summary>
[Trait("Color", "B")]
public class FearOfLostTeethFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void FearOfLostTeeth_Is11BlackNightmareEnchantmentCreature()
    {
        var card = FearOfLostTeethFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fear of Lost Teeth");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue("Enchantment Creature (CR 301.1)");
        card.HasSubtype(CardSubtype.Nightmare).Should().BeTrue();
        card.Power.Should().Be(1);
        card.Toughness.Should().Be(1);
        card.ManaCost.Should().Be("{B}");
        card.ManaCostValue.TotalValue.Should().Be(1, "{B} is mana value 1");
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FearOfLostTeeth_HasSingleDiesTrigger()
    {
        var card = FearOfLostTeethFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the only ability is the dies trigger");
    }

    [Fact]
    public void Dies_DealsOneDamageToChosenPlayer_AndControllerGainsOneLife()
    {
        var card = FearOfLostTeethFactory.Create(_alice);
        var diesTrigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // Choose the opponent as the any-target for the 1 damage.
        diesTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);

        diesTrigger.Resolve();

        _bob.LifeTotal.Should().Be(19, "it deals 1 damage to the chosen target (CR 115.3)");
        _alice.LifeTotal.Should().Be(21, "you gain 1 life (CR 119.3)");
    }

    [Fact]
    public void Dies_DealsOneDamageToChosenCreature()
    {
        var card = FearOfLostTeethFactory.Create(_alice);
        var diesTrigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        diesTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        diesTrigger.Resolve();

        bear.Damage.Should().Be(1, "1 damage was marked on the chosen creature (CR 306.7 / 120.3)");
        _alice.LifeTotal.Should().Be(21, "you gain 1 life regardless of the damage target (CR 119.3)");
    }
}

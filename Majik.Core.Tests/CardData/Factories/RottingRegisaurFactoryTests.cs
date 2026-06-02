using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RottingRegisaurFactory"/> — Rotting Regisaur
/// (Core Set 2020, {2}{B}). Creature — Zombie Dinosaur 7/6.
///
/// Oracle text (Scryfall verified):
///   "At the beginning of your upkeep, discard a card."
///
/// Covers:
/// - Identity (name, type, P/T 7/6, Zombie Dinosaur subtypes, cost).
/// - NamedCardFactory dispatch (single-arg overload).
/// - Upkeep triggered ability scoped to the controller's own upkeep
///   (CR 603.1 / CR 500.4): on resolution the controller discards a card
///   (CR 701.8). Empty hand → no-op.
/// </summary>
[Trait("Color", "B")]
public class RottingRegisaurFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RottingRegisaur_Identity()
    {
        var c = RottingRegisaurFactory.Create(_alice);

        c.Name.Should().Be("Rotting Regisaur");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(7);
        c.Toughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue(
            "Rotting Regisaur is a Zombie Dinosaur");
        c.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue(
            "Rotting Regisaur is a Zombie Dinosaur");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{2}{B}");
    }

    // -----------------------------------------------------------------------
    // Upkeep discard trigger — CR 603.1 / CR 500.4 / CR 701.8
    // -----------------------------------------------------------------------

    [Fact]
    public void RottingRegisaur_HasUpkeepDiscardTrigger()
    {
        var c = RottingRegisaurFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);

        trigger.Should().NotBeNull(
            "Rotting Regisaur has an at-the-beginning-of-your-upkeep trigger");
    }

    [Fact]
    public void RottingRegisaur_UpkeepTrigger_DiscardsACard()
    {
        var alice = new Player("Alice", 20);
        var regisaur = RottingRegisaurFactory.Create(alice);

        var card = new Creature("Grizzly Bears", "1G", 2, 2);
        card.SetOwner(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var trigger = regisaur.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);

        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().NotContain(card,
            "CR 701.8 — the controller discards a card on upkeep");
        alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }

    [Fact]
    public void RottingRegisaur_UpkeepTrigger_EmptyHand_NoOp()
    {
        var alice = new Player("Alice", 20);
        var regisaur = RottingRegisaurFactory.Create(alice);

        var trigger = regisaur.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);

        // Empty hand — the discard is a no-op, no exceptions.
        var act = () =>
        {
            foreach (var effect in trigger.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Survival of the Fittest ({1}{G}, Enchantment — Exodus).
///
/// "{G}, Discard a creature card: Search your library for a creature
/// card, reveal it, put it into your hand, then shuffle."
/// (CR 117.1 / CR 602 / CR 701.16a / CR 701.19a / CR 701.20a)
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Ability shape: one ActivatedAbility, {G} mana cost + discard a
///    creature card cost.
///  - Activation: discard cost moves a creature card to graveyard, tutor
///    picks a creature from library and puts it into hand.
///  - Library with no creatures → tutor portion is a no-op (still
///    shuffles per CR 701.20a).
///  - Empty hand of creatures → discard cost is unpayable.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class SurvivalOfTheFittestTests
{
    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = SurvivalOfTheFittestFactory.Create(owner);

        card.Name.Should().Be("Survival of the Fittest");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SurvivalOfTheFittest()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Survival of the Fittest", owner);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Survival of the Fittest");
        card.ManaCost.Should().Be("{1}{G}");
    }

    [Fact]
    public void Ability_Has_GreenMana_And_DiscardCreatureCard_Costs()
    {
        var card = SurvivalOfTheFittestFactory.Create(new Player("A", 20));

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost.ToString().Should().Contain("G");
        ability.Costs.OfType<DiscardACreatureCardCost>().Should().ContainSingle(
            "the activation cost is exactly \"{G}, Discard a creature card\"");
    }

    [Fact]
    public void Activate_DiscardsCreature_AndTutorsCreatureToHand()
    {
        var owner = new Player("A", 20);
        var card = SurvivalOfTheFittestFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Creature in hand to discard.
        var sacrifice = new Creature("Sacrifice Bear", "1G", 2, 2);
        sacrifice.SetOwner(owner); sacrifice.SetController(owner);
        owner.Zones.Hand.AddCard(sacrifice);
        sacrifice.SetZone(ZoneType.Hand);

        // Library: tutor target (creature) + irrelevant land.
        var target = new Creature("Tarmogoyf", "1G", 2, 2);
        target.SetOwner(owner); target.SetController(owner);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(owner); forest.SetController(owner);
        owner.Zones.Library.AddCard(target);
        owner.Zones.Library.AddCard(forest);

        // Mana for the {G} portion of the activation cost.
        owner.AddManaToPool(ManaCost.Parse("G"));

        AgentRegistry.Set(owner, new DeterministicBotAgent());
        GameRandomRegistry.Set(owner, new GameRandom(seed: 1));
        try
        {
            var ability = card.Abilities.OfType<ActivatedAbility>().Single();

            // Pay the costs in declaration order, then execute the effect
            // (mirrors PsychicFrogTests' activation pattern).
            foreach (var cost in ability.Costs) cost.Pay(owner);
            foreach (var fx in ability.Effects) fx.Execute();

            // Discard cost moved Sacrifice Bear to the graveyard.
            owner.Zones.Graveyard.GetCards().Should().Contain(c => c.Name == "Sacrifice Bear");
            // Tutored creature ended up in hand.
            owner.Zones.Hand.GetCards().Should().ContainSingle()
                .Which.Name.Should().Be("Tarmogoyf");
            // Library no longer contains the tutored creature.
            owner.Zones.Library.GetCards().Should().NotContain(c => c.Name == "Tarmogoyf");
        }
        finally
        {
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void Activate_NoCreatureInLibrary_StillShuffles()
    {
        // CR 701.20a — shuffle whether the search found a card or not.
        var owner = new Player("A", 20);
        var card = SurvivalOfTheFittestFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var sacrifice = new Creature("Sacrifice Bear", "1G", 2, 2);
        sacrifice.SetOwner(owner); sacrifice.SetController(owner);
        owner.Zones.Hand.AddCard(sacrifice);
        sacrifice.SetZone(ZoneType.Hand);

        // Library: ONLY non-creatures.
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(owner); forest.SetController(owner);
        owner.Zones.Library.AddCard(forest);

        owner.AddManaToPool(ManaCost.Parse("G"));

        AgentRegistry.Set(owner, new DeterministicBotAgent());
        GameRandomRegistry.Set(owner, new GameRandom(seed: 1));
        var bus = new EventBus();
        LibraryShuffledEvent? captured = null;
        bus.Subscribe<LibraryShuffledEvent>(e => captured = e);
        EventBusRegistry.Set(owner, bus);
        try
        {
            var ability = card.Abilities.OfType<ActivatedAbility>().Single();
            foreach (var cost in ability.Costs) cost.Pay(owner);
            foreach (var fx in ability.Effects) fx.Execute();

            // Discard cost still ran.
            owner.Zones.Graveyard.GetCards().Should().Contain(c => c.Name == "Sacrifice Bear");
            // Hand only has whatever the discard cost emptied — no tutored
            // creature joined it.
            owner.Zones.Hand.GetCards().Should().BeEmpty();
            // Shuffle event was published.
            captured.Should().NotBeNull();
            captured!.Reason.Should().Be("survival-of-the-fittest");
        }
        finally
        {
            EventBusRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void DiscardCost_NoCreatureInHand_IsUnpayable()
    {
        var owner = new Player("A", 20);
        var card = SurvivalOfTheFittestFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Hand has only a non-creature.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(owner); bolt.SetController(owner);
        owner.Zones.Hand.AddCard(bolt);
        bolt.SetZone(ZoneType.Hand);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = ability.Costs.OfType<DiscardACreatureCardCost>().Single();

        discardCost.CanPay(owner).Should().BeFalse(
            "the cost is restricted to creature cards in hand");
    }
}

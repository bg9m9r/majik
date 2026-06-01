using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TestConniverFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, Human Rogue subtypes, owner/controller, P/T).
/// - Single ETB triggered ability with no mana abilities.
/// - ETB effect: connive — draws + discards; adds +1/+1 counter when discarded card is nonland.
/// - ETB effect: connive with a land on top of library → discards land → no counter.
/// </summary>
public class TestConniverTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TestConniver_IsCreature()
    {
        var card = (Creature)NamedCardFactory.Create("Test Conniver", _alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void TestConniver_HasExpectedShape()
    {
        var creature = (Creature)NamedCardFactory.Create("Test Conniver", _alice);

        creature.Name.Should().Be("Test Conniver");
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
        creature.Power.Should().Be(1);
        creature.Toughness.Should().Be(1);
        creature.Subtypes.Should().Contain(CardSubtype.Human);
        creature.Subtypes.Should().Contain(CardSubtype.Rogue);
    }

    [Fact]
    public void TestConniver_HasSingleEtbTrigger_NoManaAbility()
    {
        var creature = (Creature)NamedCardFactory.Create("Test Conniver", _alice);

        creature.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        creature.Abilities.OfType<ManaAbility>().Should().BeEmpty();
    }

    [Fact]
    public void TestConniver_EtbConnive_NonLandDiscarded_AddsCounter()
    {
        var alice = new Player("Alice", 20);
        var bolt = new Card("Lightning Bolt", "R");
        bolt.SetOwner(alice);
        alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var creature = (Creature)NamedCardFactory.Create("Test Conniver", alice);
        var etb = creature.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        // Drew bolt → discarded bolt (last in hand) → counter added because nonland.
        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
    }

    [Fact]
    public void TestConniver_EtbConnive_LandDiscarded_NoCounter()
    {
        var alice = new Player("Alice", 20);
        var forest = new Land("Forest");
        forest.SetOwner(alice);
        alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var creature = (Creature)NamedCardFactory.Create("Test Conniver", alice);
        var etb = creature.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        // Drew + discarded the forest → no counter added (land).
        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        alice.Zones.Graveyard.GetCards().Should().Contain(forest);
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ScavengingOozeFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, subtype Ooze, mana cost, P/T).
/// - Dispatch via <see cref="NamedCardFactory.Create"/>.
/// - Activated ability shape: single mana cost {G}, no tap cost.
/// - Resolution: target creature in graveyard → exiled, +1/+1 counter on
///   the Ooze, controller gains 1 life.
/// - No-op when there are no creature cards in any graveyard (no counter
///   added, no life gained).
/// - Stacking: two activations stack to +2/+2 with 2 life gained.
/// - Cross-player graveyard reach via allPlayersResolver overload.
/// </summary>
public class ScavengingOozeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ScavengingOoze_IdentityIsCorrect()
    {
        var c = ScavengingOozeFactory.Create(_alice);

        c.Name.Should().Be("Scavenging Ooze");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ooze).Should().BeTrue("Scavenging Ooze is an Ooze");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ScavengingOoze_NamedCardFactory_DispatchesToFactory()
    {
        var card = NamedCardFactory.Create("Scavenging Ooze", _alice);

        card.Should().BeOfType<Creature>("Scavenging Ooze is a Creature");
        card.Name.Should().Be("Scavenging Ooze");
        card.HasSubtype(CardSubtype.Ooze).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ScavengingOoze_HasActivatedAbilityWithSingleGreenManaCost()
    {
        var ooze = ScavengingOozeFactory.Create(_alice);
        var ability = ooze.Abilities.OfType<ActivatedAbility>().Should()
            .ContainSingle("only the {G}: graveyard-exile ability is wired").Subject;

        var manaCost = ability.Costs.OfType<ManaCostCost>().Should()
            .ContainSingle("the activation cost is {G}").Subject;
        manaCost.Cost.ToString().Should().Be("G");

        ability.Costs.OfType<AdditionalCost>()
            .Should().BeEmpty("the ability has no tap or other additional cost");
    }

    // -----------------------------------------------------------------------
    // Resolution — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void ScavengingOoze_Activate_ExilesCreatureCard_AddsCounter_AndGainsLife()
    {
        var alice = new Player("Alice", 20);

        var deadBear = new Creature("Dead Bear", "1G", 2, 2);
        deadBear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(deadBear);
        deadBear.SetZone(ZoneType.Graveyard);

        var ooze = ScavengingOozeFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ooze);
        ooze.SetZone(ZoneType.Battlefield);

        var ability = ooze.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Exile.GetCards().Should().Contain(deadBear,
            "the exile effect moves the creature card to exile");
        alice.Zones.Graveyard.GetCards().Should().NotContain(deadBear);
        deadBear.Zone.Should().Be(ZoneType.Exile);

        ooze.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Scavenging Ooze gets a +1/+1 counter when the exile happens");

        alice.LifeTotal.Should().Be(21, "controller gains 1 life on a successful activation");
    }

    // -----------------------------------------------------------------------
    // Resolution — empty graveyards / no creature card
    // -----------------------------------------------------------------------

    [Fact]
    public void ScavengingOoze_Activate_NoCreatureCardAnywhere_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // Graveyard has only a non-creature card.
        var instant = new Instant("Shock", "R");
        instant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(instant);
        instant.SetZone(ZoneType.Graveyard);

        var ooze = ScavengingOozeFactory.Create(alice);
        var ability = ooze.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("no creature card → no-op");

        alice.Zones.Exile.GetCards().Should().BeEmpty("nothing was exiled");
        alice.Zones.Graveyard.GetCards().Should().Contain(instant,
            "the non-creature card is untouched");
        ooze.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no exile → 'If you do' rider is skipped → no counter");
        alice.LifeTotal.Should().Be(20, "no exile → no life gain");
    }

    [Fact]
    public void ScavengingOoze_Activate_EmptyGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        // Graveyard intentionally empty.

        var ooze = ScavengingOozeFactory.Create(alice);
        var ability = ooze.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow();
        ooze.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Resolution — stacks across multiple activations
    // -----------------------------------------------------------------------

    [Fact]
    public void ScavengingOoze_ActivateTwice_StacksCountersAndLife()
    {
        var alice = new Player("Alice", 20);

        var bear1 = new Creature("Dead Bear 1", "1G", 2, 2);
        bear1.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear1);
        bear1.SetZone(ZoneType.Graveyard);

        var bear2 = new Creature("Dead Bear 2", "1G", 2, 2);
        bear2.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear2);
        bear2.SetZone(ZoneType.Graveyard);

        var ooze = ScavengingOozeFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ooze);
        ooze.SetZone(ZoneType.Battlefield);

        var ability = ooze.Abilities.OfType<ActivatedAbility>().Single();

        // First activation.
        foreach (var effect in ability.Effects) effect.Execute();
        // Second activation.
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Exile.GetCards().Should().Contain(new ICard[] { bear1, bear2 },
            "both bears were exiled across the two activations");
        alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "every creature card was scavenged");

        ooze.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "two activations stack two +1/+1 counters");

        alice.LifeTotal.Should().Be(22, "1 life per activation, twice");
    }

    // -----------------------------------------------------------------------
    // Cross-player graveyard reach
    // -----------------------------------------------------------------------

    [Fact]
    public void ScavengingOoze_Activate_WithAllPlayersResolver_ExilesFromOpponentGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Alice has no creatures in her graveyard. Bob does.
        var bobBear = new Creature("Bob's Bear", "1G", 2, 2);
        bobBear.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Graveyard);

        var ooze = ScavengingOozeFactory.Create(
            alice,
            allPlayersResolver: () => new[] { alice, bob });
        alice.Zones.Battlefield.AddCard(ooze);
        ooze.SetZone(ZoneType.Battlefield);

        var ability = ooze.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        bob.Zones.Graveyard.GetCards().Should().NotContain(bobBear,
            "Bob's bear was removed from his graveyard");
        bob.Zones.Exile.GetCards().Should().Contain(bobBear,
            "the exiled card goes to its owner's exile zone");
        bobBear.Zone.Should().Be(ZoneType.Exile);

        ooze.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "exiling from any graveyard satisfies 'If you do'");
        alice.LifeTotal.Should().Be(21, "Alice (the controller) gains the life, not Bob");
        bob.LifeTotal.Should().Be(20);
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ExcavatedWallFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-14): {1} Artifact Creature — Wall 0/4,
///   "Defender
///    {1}, {T}: Mill a card."
///
/// Covers card identity (artifact + creature card types, Defender), the single
/// {1}, {T}: mill activated ability shape, and the mill resolution.
/// </summary>
public class ExcavatedWallFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ExcavatedWall_HasExpectedShape()
    {
        var creature = (Creature)NamedCardFactory.Create("Excavated Wall", _alice);

        creature.Name.Should().Be("Excavated Wall");
        creature.HasType(CardType.Creature).Should().BeTrue();
        creature.HasType(CardType.Artifact).Should()
            .BeTrue("Excavated Wall is an Artifact Creature (CR 205.2a)");
        creature.Subtypes.Should().Contain(CardSubtype.Wall);
        creature.Power.Should().Be(0);
        creature.Toughness.Should().Be(4);
    }

    [Fact]
    public void ExcavatedWall_HasDefender()
    {
        var creature = (Creature)NamedCardFactory.Create("Excavated Wall", _alice);

        Majik.Core.Combat.CombatAbilities.HasDefender(creature).Should()
            .BeTrue("Excavated Wall has Defender (CR 702.3)");
    }

    [Fact]
    public void ExcavatedWall_HasSingleMillActivatedAbility()
    {
        var creature = (Creature)NamedCardFactory.Create("Excavated Wall", _alice);

        var activated = creature.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only ability is {1}, {T}: Mill a card");
        activated[0].Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"), "the {1} cost");
        activated[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap, "the {T} cost");
        activated[0].TargetRequests.Should().BeEmpty(
            "mill targets nothing — the controller's own library");
    }

    [Fact]
    public void ExcavatedWall_Mill_MovesTopOfControllerLibraryToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var creature = (Creature)NamedCardFactory.Create("Excavated Wall", alice);
        var ability = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        // CR 701.13 — mill a card: the top of the controller's library goes to
        // their graveyard; the second card remains.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    [Fact]
    public void ExcavatedWall_Mill_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);
        var creature = (Creature)NamedCardFactory.Create("Excavated Wall", alice);
        var ability = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        Action act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("milling from an empty library is a clean no-op (CR 104.3c)");
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}

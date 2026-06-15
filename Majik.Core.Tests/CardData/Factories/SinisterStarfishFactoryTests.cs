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
/// Unit tests for <see cref="SinisterStarfishFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-14): {1}{B} Creature — Starfish 0/3,
///   "{T}: Surveil 1."
///
/// Covers card identity, the single tap-to-surveil activated ability shape, and
/// the resolution (no-agent default sends the peeked card to the graveyard).
/// </summary>
public class SinisterStarfishFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SinisterStarfish_HasExpectedShape()
    {
        var creature = (Creature)NamedCardFactory.Create("Sinister Starfish", _alice);

        creature.Name.Should().Be("Sinister Starfish");
        creature.HasType(CardType.Creature).Should().BeTrue();
        creature.Subtypes.Should().Contain(CardSubtype.Starfish);
        creature.Power.Should().Be(0);
        creature.Toughness.Should().Be(3);
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SinisterStarfish_HasSingleTapSurveilActivatedAbility()
    {
        var creature = (Creature)NamedCardFactory.Create("Sinister Starfish", _alice);

        var activated = creature.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only ability is {T}: Surveil 1");
        activated[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activation cost is {T}");
        activated[0].TargetRequests.Should().BeEmpty(
            "surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void SinisterStarfish_Surveil_DefaultsPeekedCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var creature = (Creature)NamedCardFactory.Create("Sinister Starfish", alice);
        var ability = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        // No agent registered → the no-agent default surveils the peeked card to
        // the graveyard (CR 701.42); the second card remains on the library.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    [Fact]
    public void SinisterStarfish_Surveil_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);
        var creature = (Creature)NamedCardFactory.Create("Sinister Starfish", alice);
        var ability = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        Action act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}

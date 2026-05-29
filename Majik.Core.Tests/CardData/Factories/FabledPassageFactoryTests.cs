using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <c>FabledPassageFactory</c> (Throne of Eldraine, etc.).
///
/// Oracle: <c>{T}, Sacrifice this land: Search your library for a basic land
/// card, put it onto the battlefield tapped, then shuffle. Then if you control
/// four or more lands, untap that land.</c>
///
/// Same tutor shape as <see cref="EvolvingWildsFactory"/> (search a basic land,
/// put it onto the battlefield tapped, then shuffle) plus the printed rider:
/// after the fetch, if the controller controls four or more lands, untap the
/// just-fetched land. The fetched land counts toward that four (CR — the
/// "four or more lands" check happens after it has entered the battlefield),
/// while the sacrificed Fabled Passage no longer does.
/// </summary>
public class FabledPassageFactoryTests
{
    private static Land MakeBasic(string name, CardSubtype subtype) =>
        new(name, new[] { CardSupertype.Basic }, new[] { subtype });

    private static Land MakeNonbasicLand(string name) =>
        new(name, supertypes: null, subtypes: null);

    [Fact]
    public void Dispatch_ReturnsLandWithPrintedName()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Fabled Passage", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Fabled Passage");
    }

    [Fact]
    public void HasSingleTapActivatedAbility()
    {
        var alice = new Player("Alice", 20);

        var land = NamedCardFactory.Create("Fabled Passage", alice);

        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Should().ContainSingle().Subject;
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(ac => ac.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public void Activation_FewerThanFourLands_FetchesBasicTapped_StaysTapped()
    {
        var alice = new Player("Alice", 20);

        // Stage a basic + a nonbasic dual-typed land in library; activation
        // must pick the basic and leave the dual alone (CR 205.4a).
        var basicForest = MakeBasic("Forest", CardSubtype.Forest);
        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        alice.Zones.Library.AddCard(basicForest);
        alice.Zones.Library.AddCard(stomping);
        basicForest.SetZone(ZoneType.Library);
        stomping.SetZone(ZoneType.Library);

        var passage = NamedCardFactory.Create("Fabled Passage", alice) as Land;
        passage.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(passage!);
        passage!.SetZone(ZoneType.Battlefield);

        var ability = passage!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        // Basic forest fetched to battlefield; dual stays in library.
        alice.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        alice.Zones.Library.GetCards().Should().Contain(stomping);
        alice.Zones.Library.GetCards().Should().NotContain(basicForest);

        // Only one land on battlefield after sacrifice (the fetched basic) —
        // fewer than four lands, so it remains TAPPED.
        basicForest.IsTapped.Should().BeTrue();

        // Passage self-sacrificed; no life payment.
        alice.Zones.Graveyard.GetCards().Should().Contain(passage);
        alice.Zones.Battlefield.GetCards().Should().NotContain(passage);
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Activation_FourOrMoreLands_FetchedLandIsUntapped()
    {
        var alice = new Player("Alice", 20);

        // Three lands already on the battlefield. The fetched basic becomes
        // the fourth land on the battlefield (Fabled Passage is sacrificed and
        // no longer counts), so the rider untaps it.
        for (var i = 0; i < 3; i++)
        {
            var existing = MakeNonbasicLand($"Wastes{i}");
            alice.Zones.Battlefield.AddCard(existing);
            existing.SetZone(ZoneType.Battlefield);
        }

        var basicMountain = MakeBasic("Mountain", CardSubtype.Mountain);
        alice.Zones.Library.AddCard(basicMountain);
        basicMountain.SetZone(ZoneType.Library);

        var passage = NamedCardFactory.Create("Fabled Passage", alice) as Land;
        passage.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(passage!);
        passage!.SetZone(ZoneType.Battlefield);

        var ability = passage!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        // Fetched basic on the battlefield and UNTAPPED (controller controls
        // four-or-more lands: the 3 pre-existing + the fetched basic).
        alice.Zones.Battlefield.GetCards().Should().Contain(basicMountain);
        basicMountain.IsTapped.Should().BeFalse();

        // Passage sacrificed.
        alice.Zones.Graveyard.GetCards().Should().Contain(passage);
        alice.Zones.Battlefield.GetCards().Should().NotContain(passage);
    }

    [Fact]
    public void Activation_NoBasicInLibrary_StillSacrificesAndShuffles()
    {
        var alice = new Player("Alice", 20);

        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        alice.Zones.Library.AddCard(stomping);
        stomping.SetZone(ZoneType.Library);

        var passage = NamedCardFactory.Create("Fabled Passage", alice) as Land;
        passage.Should().NotBeNull();
        alice.Zones.Battlefield.AddCard(passage!);
        passage!.SetZone(ZoneType.Battlefield);

        var ability = passage!.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        alice.Zones.Graveyard.GetCards().Should().Contain(passage);
        alice.LifeTotal.Should().Be(20);
        // Nonbasic untouched.
        alice.Zones.Library.GetCards().Should().Contain(stomping);
        alice.Zones.Battlefield.GetCards().Should().NotContain(stomping);
    }
}

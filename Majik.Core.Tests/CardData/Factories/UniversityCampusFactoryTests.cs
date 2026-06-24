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
/// Unit tests for <see cref="UniversityCampusFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-24): Land (no basic subtypes),
///   "This land enters tapped.
///    {T}: Add {W} or {U}.
///    {4}, {T}: Surveil 1."
///
/// This is the Foundations "campus" surveil-land shape — same as the Outlaws of
/// Thunder Junction surveil lands (e.g. <see cref="TitansGraveFactory"/>) and
/// distinct from the Duskmourn / Karlov Manor surveil-ON-ENTER lands: here the
/// surveil is an ACTIVATED ability gated behind {4} + {T}, NOT an ETB trigger.
///
/// Covered unique behaviour: the dual {W}/{U} mana abilities and the single
/// {4}, {T}: Surveil 1 activated ability (cost shape + resolution).
/// The CardFactoryContractTests suite already asserts dispatch + well-formedness.
/// </summary>
[Trait("Color", "M")]
public class UniversityCampusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void UniversityCampus_Identity()
    {
        var land = (Land)NamedCardFactory.Create("University Campus", _alice);

        land.Name.Should().Be("University Campus");
        land.HasType(CardType.Land).Should().BeTrue();
        // Type line is plain "Land" — no basic land subtypes (this campus cycle
        // is not a dual basic like the Karlov surveil lands).
        land.Subtypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void UniversityCampus_ProducesWhiteAndBlueMana()
    {
        var land = (Land)NamedCardFactory.Create("University Campus", _alice);

        // Two mana abilities, one per produced colour (CR 605.1a): {T}: Add {W} or {U}.
        var manaAbilities = land.Abilities.OfType<IManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2, "{T}: Add {W} or {U} is two single-colour mana abilities");
    }

    [Fact]
    public void UniversityCampus_HasManaGatedTapSurveilActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("University Campus", _alice);

        // The non-mana activated ability is {4}, {T}: Surveil 1.
        var activated = land.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only non-mana ability is {4}, {T}: Surveil 1");

        var ability = activated[0];

        // {T} is part of the activation cost.
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the activation cost includes {T}");

        // {4} mana cost is part of the activation cost (CR 601.2f).
        ability.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle("the activation cost includes the {4} mana payment");

        ability.TargetRequests.Should().BeEmpty(
            "surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void UniversityCampus_Surveil_DefaultsPeekedCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("University Campus", alice);
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        // No agent registered → the no-agent default surveils the peeked card to
        // the graveyard (CR 701.42); the second card remains on the library.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }
}

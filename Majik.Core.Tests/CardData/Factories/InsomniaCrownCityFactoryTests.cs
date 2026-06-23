using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="InsomniaCrownCityFactory"/> — the W/B "Town"
/// dual tapland. Oracle text:
///   "This land enters tapped.
///    {T}: Add {W} or {B}."
///
/// Covers this card's unique shell:
/// - Identity (Land + the printed <c>Town</c> subtype, CR 205.3m).
/// - Two mana abilities producing {W} and {B} respectively (CR 605.1 — mana
///   abilities don't use the stack).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Baron / Treno factories). Dispatch +
/// well-formedness are covered for every implemented card by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")]
public class InsomniaCrownCityFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void InsomniaCrownCity_HasTwoManaAbilities_ProducingWhiteAndBlack()
    {
        var land = (Land)NamedCardFactory.Create("Insomnia, Crown City", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Insomnia taps for {W} or {B}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void InsomniaCrownCity_Identity_IsLandWithTownSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Insomnia, Crown City", _alice);

        land.Subtypes.Should().Contain(CardSubtype.Town, "the printed land subtype is Town (CR 205.3m)");
    }

    [Fact]
    public void InsomniaCrownCity_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Insomnia, Crown City", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Insomnia has no cycling clause");
    }
}

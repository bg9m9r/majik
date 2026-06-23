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
/// Unit tests for <see cref="GongagaReactorTownFactory"/> — the R/G "Town"
/// dual tapland. Oracle text:
///   "This land enters tapped.
///    {T}: Add {R} or {G}."
///
/// Covers this card's unique shell:
/// - Identity (Land + the printed <c>Town</c> subtype, CR 205.3m).
/// - Two mana abilities producing {R} and {G} respectively (CR 605.1 — mana
///   abilities don't use the stack).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Baron / Guildgate factories). Dispatch +
/// well-formedness are covered for every implemented card by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")]
public class GongagaReactorTownFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GongagaReactorTown_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Gongaga, Reactor Town", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Gongaga taps for {R} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void GongagaReactorTown_Identity_IsLandWithTownSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Gongaga, Reactor Town", _alice);

        land.Subtypes.Should().Contain(CardSubtype.Town, "the printed land subtype is Town (CR 205.3m)");
    }

    [Fact]
    public void GongagaReactorTown_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Gongaga, Reactor Town", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Gongaga has no cycling clause");
    }
}

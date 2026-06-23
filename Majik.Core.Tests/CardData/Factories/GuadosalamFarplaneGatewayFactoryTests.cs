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
/// Unit tests for <see cref="GuadosalamFarplaneGatewayFactory"/> — the G/U "Town"
/// dual tapland. Oracle text:
///   "This land enters tapped.
///    {T}: Add {G} or {U}."
///
/// Covers this card's unique shell:
/// - Identity (Land + the printed <c>Town</c> subtype, CR 205.3m).
/// - Two mana abilities producing {G} and {U} respectively (CR 605.1 — mana
///   abilities don't use the stack).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Baron / Treno factories). Dispatch +
/// well-formedness are covered for every implemented card by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")]
public class GuadosalamFarplaneGatewayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GuadosalamFarplaneGateway_HasTwoManaAbilities_ProducingGreenAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Guadosalam, Farplane Gateway", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Guadosalam taps for {G} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void GuadosalamFarplaneGateway_Identity_IsLandWithTownSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Guadosalam, Farplane Gateway", _alice);

        land.Subtypes.Should().Contain(CardSubtype.Town, "the printed land subtype is Town (CR 205.3m)");
    }

    [Fact]
    public void GuadosalamFarplaneGateway_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Guadosalam, Farplane Gateway", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Guadosalam has no cycling clause");
    }
}

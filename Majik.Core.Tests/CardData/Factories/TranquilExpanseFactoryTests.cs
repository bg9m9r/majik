using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TranquilExpanseFactory"/> — the G/W
/// enters-tapped dual land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {G} or {W}."
///
/// Covers the card's unique behaviour:
/// - Two mana abilities producing {G} and {W} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - No other activated abilities (no cycling, unlike the Triome cycle).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Guildgate / gain-land factories). Dispatch and
/// well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so they are not duplicated here.
/// </summary>
[Trait("Color", "C")]
public class TranquilExpanseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TranquilExpanse_HasTwoManaAbilities_ProducingGreenAndWhite()
    {
        var land = (Land)NamedCardFactory.Create("Tranquil Expanse", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Tranquil Expanse taps for {G} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void TranquilExpanse_HasNoOtherActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Tranquil Expanse", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Tranquil Expanse has only its two mana abilities — no cycling");
    }
}

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
/// Unit tests for <see cref="TimberGorgeFactory"/> — the R/G common tapland.
/// Oracle text:
///   "This land enters tapped.
///    {T}: Add {R} or {G}."
///
/// Covers the card's unique behaviour:
/// - Identity (a plain Land with NO land subtype — unlike the Gate cycle).
/// - Two mana abilities producing {R} and {G} respectively (CR 605.1 — mana
///   abilities don't use the stack).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load path
/// by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Guildgate factories). Dispatch + general
/// well-formedness are covered by CardFactoryContractTests.
/// </summary>
[Trait("Color", "C")]
public class TimberGorgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TimberGorge_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Timber Gorge", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Timber Gorge taps for {R} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void TimberGorge_Identity_IsPlainLandWithNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Timber Gorge", _alice);

        land.Name.Should().Be("Timber Gorge");
        // Unlike the Guildgate cycle, Timber Gorge is NOT a Gate — no land
        // subtype is printed (CR 305.6 — a land has only the subtypes it lists).
        land.Subtypes.Should().BeEmpty("Timber Gorge has no land subtype");
    }
}

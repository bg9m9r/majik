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
/// Unit tests for <see cref="VolatileFjordFactory"/> — the U/R Kaldheim snow
/// dual land. Type line: <c>Snow Land — Island Mountain</c>. Oracle text:
///   "({T}: Add {U} or {R}.)
///    This land enters tapped."
///
/// Covers:
/// - Identity (Land + Snow supertype, CR 205.4d, + the printed Island and
///   Mountain subtypes, CR 205.3i).
/// - Two mana abilities producing {U} and {R} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the rest of the snow-dual cycle).
/// </summary>
[Trait("Color", "C")]
public class VolatileFjordFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    [Fact]
    public void VolatileFjord_HasTwoManaAbilities_ProducingBlueAndRed()
    {
        var land = (Land)NamedCardFactory.Create("Volatile Fjord", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Volatile Fjord taps for {U} or {R}");
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void VolatileFjord_HasNoNonManaActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Volatile Fjord", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("the snow dual lands have no activated abilities");
    }
}

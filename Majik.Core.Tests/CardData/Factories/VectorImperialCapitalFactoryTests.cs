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
/// Unit tests for <see cref="VectorImperialCapitalFactory"/> — the {B}/{R}
/// enters-tapped Town tapland (Edge of Eternities). Oracle text:
///   "This land enters tapped.
///    {T}: Add {B} or {R}."
///
/// Covers:
/// - Identity (Land + the printed Town subtype, CR 205.3i) — the one trait that
///   distinguishes this from <see cref="RakdosGuildgateFactory"/> (a Gate).
/// - Two mana abilities producing {B} and {R} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - No cycling / extra activated abilities.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load path
/// by <see cref="EntersTappedBinder"/> from the oracle text, not by this factory
/// (same posture as the Guildgate factories).
/// </summary>
[Trait("Color", "C")]
public class VectorImperialCapitalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void VectorImperialCapital_Identity_IsLandWithTownSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Vector, Imperial Capital", _alice);

        land.Subtypes.Should().Contain(CardSubtype.Town,
            "Vector is printed Land — Town (CR 205.3i)");
    }

    [Fact]
    public void VectorImperialCapital_HasTwoManaAbilities_ProducingBlackAndRed()
    {
        var land = (Land)NamedCardFactory.Create("Vector, Imperial Capital", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Vector taps for {B} or {R}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void VectorImperialCapital_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Vector, Imperial Capital", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Vector has no cycling, unlike the Triomes");
    }
}

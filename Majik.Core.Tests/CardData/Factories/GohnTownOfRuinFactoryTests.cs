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
/// Unit tests for <see cref="GohnTownOfRuinFactory"/> — the B/G Town land
/// (Edge of Eternities). Oracle text:
///   "This land enters tapped.
///    {T}: Add {B} or {G}."
///
/// Covers:
/// - Identity (Land + the printed Town subtype, CR 205.3m).
/// - Two mana abilities producing {B} and {G} respectively (CR 605.1 — mana
///   abilities don't use the stack).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load path
/// by <see cref="EntersTappedBinder"/> from the oracle text, not by this factory
/// (same posture as the Guildgate factories).
/// </summary>
[Trait("Color", "M")]
public class GohnTownOfRuinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GohnTownOfRuin_HasTwoManaAbilities_ProducingBlackAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Gohn, Town of Ruin", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Gohn taps for {B} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void GohnTownOfRuin_Identity_IsLandWithTownSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Gohn, Town of Ruin", _alice);

        land.HasSubtype(CardSubtype.Town).Should().BeTrue("printed type line is 'Land — Town' (CR 205.3m)");
    }

    [Fact]
    public void GohnTownOfRuin_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Gohn, Town of Ruin", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Gohn has no activated non-mana ability");
    }
}

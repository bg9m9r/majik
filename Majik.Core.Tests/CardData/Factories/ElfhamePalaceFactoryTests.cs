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
/// Unit tests for <see cref="ElfhamePalaceFactory"/> — the G/W tapland from
/// Invasion. Oracle text:
///   "This land enters tapped.
///    {T}: Add {G} or {W}."
///
/// Covers:
/// - Identity (Land, no land subtype — unlike the Selesnya Guildgate it is
///   not a Gate).
/// - Two mana abilities producing {G} and {W} respectively (CR 605.1 — mana
///   abilities don't use the stack).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Guildgate factories).
/// </summary>
[Trait("Color", "C")]
public class ElfhamePalaceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ElfhamePalace_HasTwoManaAbilities_ProducingGreenAndWhite()
    {
        var land = (Land)NamedCardFactory.Create("Elfhame Palace", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Elfhame Palace taps for {G} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void ElfhamePalace_Identity_LandWithNoSubtype()
    {
        var land = (Land)NamedCardFactory.Create("Elfhame Palace", _alice);

        land.Subtypes.Should().BeEmpty(
            "Elfhame Palace predates the Gate cycle and carries no land subtype");
    }

    [Fact]
    public void ElfhamePalace_HasNoNonManaActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Elfhame Palace", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Elfhame Palace has no cycling or other activated ability beyond its mana abilities");
    }
}

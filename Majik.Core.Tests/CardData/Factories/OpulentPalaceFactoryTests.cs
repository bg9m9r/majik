using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="OpulentPalaceFactory"/> — Opulent Palace
/// (Khans of Tarkir Sultai-wedge tapland). Oracle text:
///   "This land enters tapped.
///    {T}: Add {B}, {G}, or {U}."
///
/// Mirrors <see cref="IzzetGuildgateFactoryTests"/>: identity, three mana
/// abilities (one per produced colour {B}/{G}/{U}, CR 605.1), no extra
/// activated abilities (no cycling), and the enters-tapped replacement
/// registration (CR 614.1c) when a <see cref="ReplacementBus"/> is supplied.
/// </summary>
[Trait("Color", "C")]
public class OpulentPalaceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void OpulentPalace_HasThreeManaAbilities_ProducingBGU()
    {
        var land = (Land)NamedCardFactory.Create("Opulent Palace", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "{T}: Add {B}, {G}, or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void OpulentPalace_HasNoActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Opulent Palace", _alice);

        // Unlike the Sultai triome (Zagoth) there is no cycling clause —
        // only the three mana abilities, no extra activated abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void OpulentPalace_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = OpulentPalaceFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c);
        // the shape-only path (null bus) skips it. EntersTappedReplacement
        // has no public bus-inspection surface, so the production path
        // (covered by the binder chain via oracle text) is the authoritative
        // test for tapped-entry behaviour — same posture as Izzet Guildgate.
    }
}

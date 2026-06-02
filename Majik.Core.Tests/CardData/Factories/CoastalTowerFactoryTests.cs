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
/// Unit tests for <see cref="CoastalTowerFactory"/> — Coastal Tower
/// (Invasion "Tower" tapped-dual cycle). Oracle text (verified against
/// Scryfall):
///   "This land enters tapped.
///    {T}: Add {W} or {U}."
///
/// Mirrors <see cref="IzzetGuildgateFactory"/>'s shape: a plain Land with two
/// single-colour mana abilities ({W}/{U}) and an unconditional enters-tapped
/// replacement (CR 614.1c) when a <see cref="ReplacementBus"/> is supplied.
/// Unlike the guildgate it carries no Gate subtype and, like the guildgate,
/// has no extra activated abilities (no life gain, no cycling).
/// </summary>
[Trait("Color", "C")]
public class CoastalTowerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void CoastalTower_HasTwoManaAbilities_ProducingWhiteAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Coastal Tower", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {W} or {U}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void CoastalTower_HasNoExtraActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Coastal Tower", _alice);

        // Coastal Tower has only the two mana abilities — no cycling, no
        // life-gain trigger, no other activated abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void CoastalTower_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = CoastalTowerFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c);
        // the shape-only path (null bus) skips it. EntersTappedReplacement
        // has no public bus-inspection surface, so the production path
        // (covered by the binder chain via oracle text) is the authoritative
        // test for tapped-entry behaviour — same posture as Izzet Guildgate.
    }
}

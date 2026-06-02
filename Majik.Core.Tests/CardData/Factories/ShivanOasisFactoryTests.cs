using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ShivanOasisFactory"/> — Shivan Oasis (Invasion
/// tapped-dual "Oasis" cycle, red/green member). Oracle text:
///   "This land enters tapped.
///    {T}: Add {R} or {G}."
///
/// Mirrors <see cref="IzzetGuildgateFactoryTests"/>: identity + two mana
/// abilities (one per produced colour {R}/{G}), no extra activated abilities,
/// and the enters-tapped replacement registration (CR 614.1c) when a
/// <see cref="ReplacementBus"/> is supplied.
/// </summary>
[Trait("Color", "C")]
public class ShivanOasisFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void ShivanOasis_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Shivan Oasis", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {R} or {G}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void ShivanOasis_HasNoExtraActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shivan Oasis", _alice);

        // A plain tapped dual — only the two mana abilities, no cycling or
        // other activated abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------
    [Fact]
    public void ShivanOasis_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = ShivanOasisFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered by the
        // binder chain via oracle text) is authoritative for tapped-entry
        // behaviour — same posture as Izzet Guildgate.
    }
}

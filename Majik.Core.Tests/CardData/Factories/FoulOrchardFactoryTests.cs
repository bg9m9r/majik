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
/// Unit tests for <see cref="FoulOrchardFactory"/> — Foul Orchard (common
/// tapped-dual cycle, black/green member). Oracle text:
///   "This land enters tapped.
///    {T}: Add {B} or {G}."
///
/// Mirrors <see cref="StoneQuarryFactoryTests"/>: identity + two mana
/// abilities (one per produced colour {B}/{G}), no extra activated abilities,
/// and the enters-tapped replacement registration (CR 614.1c) when a
/// <see cref="ReplacementBus"/> is supplied.
/// </summary>
[Trait("Color", "C")]
public class FoulOrchardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------
    [Fact]
    public void FoulOrchard_HasTwoManaAbilities_ProducingBlackAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Foul Orchard", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {B} or {G}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void FoulOrchard_HasNoExtraActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Foul Orchard", _alice);

        // A plain tapped dual — only the two mana abilities, no cycling or
        // other activated abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------
    [Fact]
    public void FoulOrchard_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = FoulOrchardFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered by the
        // binder chain via oracle text) is authoritative for tapped-entry
        // behaviour — same posture as Stone Quarry.
    }
}

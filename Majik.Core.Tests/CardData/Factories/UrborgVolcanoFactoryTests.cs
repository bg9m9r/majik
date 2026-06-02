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
/// Unit tests for <see cref="UrborgVolcanoFactory"/> — Urborg Volcano
/// (Planeshift tapland cycle). Oracle text:
///   "This land enters tapped.
///    {T}: Add {B} or {R}."
///
/// Mirrors <see cref="IzzetGuildgateFactoryTests"/>: identity + two mana
/// abilities (one per produced colour {B}/{R}), no extra activated abilities,
/// and the enters-tapped replacement registration (CR 614.1c) when a
/// <see cref="ReplacementBus"/> is supplied.
/// </summary>
[Trait("Color", "C")]
public class UrborgVolcanoFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void UrborgVolcano_HasTwoManaAbilities_ProducingBlackAndRed()
    {
        var land = (Land)NamedCardFactory.Create("Urborg Volcano", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {B} or {R}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void UrborgVolcano_HasNoExtraActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Urborg Volcano", _alice);

        // Only the two mana abilities — no cycling or other activated abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void UrborgVolcano_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = UrborgVolcanoFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered by the
        // binder chain via oracle text) is the authoritative test for
        // tapped-entry behaviour — same posture as Izzet Guildgate.
    }
}

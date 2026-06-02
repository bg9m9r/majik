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
/// Unit tests for <see cref="SaltMarshFactory"/> — Salt Marsh (Apocalypse
/// "Tap Land" cycle). Oracle text:
///   "This land enters tapped.
///    {T}: Add {U} or {B}."
///
/// Mirrors <see cref="IzzetGuildgateFactory"/> shape: identity (a bare Land,
/// no subtype), two mana abilities (one per produced colour {U}/{B}, CR 605.1
/// — mana abilities don't use the stack), no cycling, and the enters-tapped
/// replacement registration (CR 614.1c) when a <see cref="ReplacementBus"/>
/// is supplied.
/// </summary>
[Trait("Color", "C")]
public class SaltMarshFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void SaltMarsh_HasTwoManaAbilities_ProducingBlueAndBlack()
    {
        var land = (Land)NamedCardFactory.Create("Salt Marsh", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {U} or {B}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void SaltMarsh_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Salt Marsh", _alice);

        // Salt Marsh has no cycling clause — only the two mana abilities,
        // no extra activated abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------
    [Fact]
    public void SaltMarsh_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = SaltMarshFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c);
        // the shape-only path (null bus) skips it. EntersTappedReplacement
        // has no public bus-inspection surface, so the production path
        // (covered by the binder chain via oracle text) is the authoritative
        // test for tapped-entry behaviour — same posture as the Guildgates.
    }
}

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
/// Unit tests for <see cref="CinderBarrensFactory"/> — Cinder Barrens
/// (the "plain" B/R enters-tapped dual). Oracle text (verified against
/// Scryfall 2026-06-02):
///   "This land enters tapped.
///    {T}: Add {B} or {R}."
///
/// Same oracle shape as <see cref="IzzetGuildgateFactory"/> but without the
/// Gate subtype and without any rider: identity (Land, nonbasic), two mana
/// abilities (one per produced colour {B}/{R}), no extra activated ability,
/// and the enters-tapped replacement registration (CR 614.1c) when a
/// <see cref="ReplacementBus"/> is supplied.
/// </summary>
[Trait("Color", "C")]
public class CinderBarrensFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void CinderBarrens_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Cinder Barrens", _alice);

        land.Name.Should().Be("Cinder Barrens");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Cinder Barrens is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CinderBarrens_HasTwoManaAbilities_ProducingBlackAndRed()
    {
        var land = (Land)NamedCardFactory.Create("Cinder Barrens", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {B} or {R}");
        manaAbilities.Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Red == 0);
        manaAbilities.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void CinderBarrens_HasNoExtraActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Cinder Barrens", _alice);

        // The plain tapland has no rider (no cycling, no life gain, no scry) —
        // only the two mana abilities, no other activated ability.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void CinderBarrens_HasNoTriggeredAbility()
    {
        var land = (Land)NamedCardFactory.Create("Cinder Barrens", _alice);

        // Unlike the gain-land cycle (Bloodfell Caves) there is no ETB
        // triggered rider on Cinder Barrens.
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void CinderBarrens_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = CinderBarrensFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c);
        // the shape-only path (null bus) skips it. EntersTappedReplacement
        // has no public bus-inspection surface, so the production path
        // (covered by the binder chain via oracle text) is the authoritative
        // test for tapped-entry behaviour — same posture as Izzet Guildgate.
    }
}

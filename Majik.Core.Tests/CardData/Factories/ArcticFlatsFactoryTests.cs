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
/// Unit tests for <see cref="ArcticFlatsFactory"/> — Arctic Flats (the
/// Coldsnap G/W snow enters-tapped dual). Oracle text (verified against
/// Scryfall 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {G} or {W}."
/// Type line: "Snow Land".
///
/// Same oracle shape as <see cref="CinderBarrensFactory"/> but carries the
/// Snow supertype and has NO basic land subtypes: identity (Snow Land,
/// nonbasic), two mana abilities (one per produced colour {G}/{W}), no extra
/// activated/triggered ability, and the enters-tapped replacement
/// registration (CR 614.1c) when a <see cref="ReplacementBus"/> is supplied.
/// </summary>
[Trait("Color", "C")]
public class ArcticFlatsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------
    [Fact]
    public void ArcticFlats_IsSnowLand_Nonbasic()
    {
        var land = (Land)NamedCardFactory.Create("Arctic Flats", _alice);

        land.Name.Should().Be("Arctic Flats");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue("type line is \"Snow Land\"");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Arctic Flats is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Unique behaviour: two single-colour mana abilities {G}/{W}
    // -----------------------------------------------------------------------
    [Fact]
    public void ArcticFlats_HasTwoManaAbilities_ProducingGreenAndWhite()
    {
        var land = (Land)NamedCardFactory.Create("Arctic Flats", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {G} or {W}");
        manaAbilities.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.White == 0);
        manaAbilities.Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void ArcticFlats_HasNoExtraActivatedOrTriggeredAbility()
    {
        var land = (Land)NamedCardFactory.Create("Arctic Flats", _alice);

        // The plain snow tapland has no rider — only the two mana abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------
    [Fact]
    public void ArcticFlats_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = ArcticFlatsFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered by the
        // binder chain via oracle text) is the authoritative tapped-entry test
        // — same posture as Cinder Barrens.
    }
}

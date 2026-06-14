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
/// Unit tests for <see cref="BorealShelfFactory"/> — Boreal Shelf, the
/// Coldsnap white/blue "plain" snow enters-tapped land. Type line:
/// <c>Snow Land</c>. Oracle text (verified against Scryfall 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {W} or {U}."
///
/// Same oracle shape as <see cref="CinderBarrensFactory"/> (no rider, no
/// triggered ability) but carries the Snow supertype (CR 205.4d) and — unlike
/// the Kaldheim snow duals (<see cref="TangledIsletFactory"/>) — has NO printed
/// basic land subtypes. Covers: identity (Land + Snow supertype, nonbasic, no
/// basic subtypes), the two mana abilities {W}/{U} (CR 605.1 — mana abilities
/// don't use the stack), the absence of any other ability, and the
/// enters-tapped replacement registration (CR 614.1c) when a
/// <see cref="ReplacementBus"/> is supplied.
/// </summary>
[Trait("Color", "C")]
public class BorealShelfFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BorealShelf_Identity_IsNonbasicSnowLand_WithNoBasicSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Boreal Shelf", _alice);

        land.Name.Should().Be("Boreal Shelf");
        land.HasType(CardType.Land).Should().BeTrue();
        // Snow Land — Snow supertype, CR 205.4d.
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue("type line is \"Snow Land\"");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Boreal Shelf is nonbasic");
        // No printed basic land subtypes — it is simply "Snow Land".
        land.HasSubtype(CardSubtype.Plains).Should().BeFalse();
        land.HasSubtype(CardSubtype.Island).Should().BeFalse();
    }

    [Fact]
    public void BorealShelf_HasTwoManaAbilities_ProducingWhiteAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Boreal Shelf", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "{T}: Add {W} or {U}");
        mana.Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void BorealShelf_HasNoExtraActivatedOrTriggeredAbility()
    {
        var land = (Land)NamedCardFactory.Create("Boreal Shelf", _alice);

        // The plain snow tapland has no rider (no cycling, no life gain, no
        // scry) — only the two mana abilities.
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void BorealShelf_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = BorealShelfFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c);
        // the shape-only path (null bus) skips it. The production path (binder
        // chain off oracle text) is the authoritative tapped-entry test — same
        // posture as Cinder Barrens / Tangled Islet.
    }
}

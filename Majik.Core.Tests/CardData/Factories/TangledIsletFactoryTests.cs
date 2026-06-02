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
/// Unit tests for <see cref="TangledIsletFactory"/> — Tangled Islet, the
/// green/blue member of the common ETB-tapped dual-land cycle. Type line:
/// <c>Land — Forest Island</c>. Oracle text (verified against Scryfall):
///   "({T}: Add {G} or {U}.)
///    This land enters tapped."
///
/// Covers:
/// - Identity (Land + the printed Forest and Island basic land subtypes,
///   CR 205.3i; nonbasic — no Basic supertype, CR 205.4a).
/// - Two mana abilities producing {G} and {U} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - No activated / triggered abilities beyond the mana abilities (no
///   gain-life trigger, unlike the Refuge cycle).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Args validation.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as <see cref="SimicGuildgateFactory"/>).
/// </summary>
[Trait("Color", "C")]
public class TangledIsletFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TangledIslet_HasForestAndIslandSubtypes_AndIsNonbasic()
    {
        var land = (Land)NamedCardFactory.Create("Tangled Islet", _alice);

        land.HasSubtype(CardSubtype.Forest).Should().BeTrue(
            "the type line is 'Land — Forest Island'");
        land.HasSubtype(CardSubtype.Island).Should().BeTrue(
            "the type line is 'Land — Forest Island'");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Tangled Islet is a nonbasic dual land");
    }

    [Fact]
    public void TangledIslet_HasTwoManaAbilities_ProducingGreenAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Tangled Islet", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Tangled Islet taps for {G} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void TangledIslet_HasNoActivatedOrTriggeredAbilities()
    {
        var land = TangledIsletFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Tangled Islet has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Tangled Islet has no triggered abilities (no gain-life trigger)");
    }

    [Fact]
    public void TangledIslet_Create_ThrowsOnNullOwner()
    {
        var act = () => TangledIsletFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

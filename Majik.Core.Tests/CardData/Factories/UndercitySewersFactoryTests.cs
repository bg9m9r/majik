using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="UndercitySewersFactory"/> (Murders at Karlov Manor
/// "surveil land" dual cycle — the U/B member).
///
/// U/B surveil tapland. Oracle text (verified against Scryfall):
///   "({T}: Add {U} or {B}.)
///    This land enters tapped.
///    When this land enters, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)"
///
/// Type line is <c>Land — Island Swamp</c>. The whole shape (identity, dual
/// mana, ETB surveil) loads from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Identity (Land, Island + Swamp subtypes, nonbasic, owner/controller).
/// - Two single-colour mana abilities — {U} and {B} (CR 605.1a).
/// - ETB triggered ability (CR 603.6a) that is battlefield-active.
/// - Surveil-1 default decision (CR 701.43) — top card to graveyard.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class UndercitySewersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void UndercitySewers_Identity_LandWithIslandSwampSubtypes()
    {
        var land = UndercitySewersFactory.Create(_alice);

        land.Name.Should().Be("Undercity Sewers");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSubtype(CardSubtype.Island).Should().BeTrue(
            "Undercity Sewers's printed type line is 'Land — Island Swamp'");
        land.HasSubtype(CardSubtype.Swamp).Should().BeTrue(
            "Undercity Sewers's printed type line is 'Land — Island Swamp'");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Undercity Sewers is a nonbasic Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void UndercitySewers_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Undercity Sewers", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Undercity Sewers");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {U} or {B} — two single-colour mana abilities (CR 605.1a)
    // -----------------------------------------------------------------------

    [Fact]
    public void UndercitySewers_HasManaAbility_ForBlue()
    {
        var land = UndercitySewersFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void UndercitySewers_HasManaAbility_ForBlack()
    {
        var land = UndercitySewersFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Blue == 0);
    }

    // -----------------------------------------------------------------------
    // ETB surveil 1 (CR 603.6a + CR 701.43)
    // -----------------------------------------------------------------------

    [Fact]
    public void UndercitySewers_EtbTrigger_IsBattlefieldActive()
    {
        var land = UndercitySewersFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    /// <summary>
    /// CR 701.43 — surveil 1 with no registered agent defaults to putting the
    /// peeked top card into the controller's graveyard (same posture as the
    /// rest of the surveil-land cycle).
    /// </summary>
    [Fact]
    public void UndercitySewers_SurveilEffect_PutsTopCardInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = UndercitySewersFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Graveyard);
    }
}

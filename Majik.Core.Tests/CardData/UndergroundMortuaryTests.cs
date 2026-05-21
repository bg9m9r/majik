using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="UndergroundMortuaryFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type)
/// - Two mana abilities ({U} and {B})
/// - ETB triggered ability presence
/// - Surveil 1 effect: top card sent to graveyard (default-all-graveyard decision)
/// - Empty library surveil (no crash)
/// </summary>
public class UndergroundMortuaryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void UndergroundMortuary_IsLand()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void UndergroundMortuary_NameIsCorrect()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);

        land.Name.Should().Be("Underground Mortuary");
    }

    [Fact]
    public void UndergroundMortuary_OwnerAndControllerAreSet()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void UndergroundMortuary_IsNotLegendary()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Underground Mortuary is not a legendary land");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}: Add {U} or {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void UndergroundMortuary_HasExactlyTwoManaAbilities()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {U} and one for {B}; player selects which to activate");
    }

    [Fact]
    public void UndergroundMortuary_HasBlueManaAbility()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0,
                "must have exactly one {U} mana ability");
    }

    [Fact]
    public void UndergroundMortuary_HasBlackManaAbility()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Blue == 0,
                "must have exactly one {B} mana ability");
    }

    [Fact]
    public void UndergroundMortuary_BlueManaAbility_ProducesOnlyBlue()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.ManaGenerated.Generic.Should().Be(0);
        blue.ManaGenerated.White.Should().Be(0);
        blue.ManaGenerated.Black.Should().Be(0);
        blue.ManaGenerated.Red.Should().Be(0);
        blue.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void UndergroundMortuary_BlackManaAbility_ProducesOnlyBlack()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.ManaGenerated.Generic.Should().Be(0);
        black.ManaGenerated.White.Should().Be(0);
        black.ManaGenerated.Blue.Should().Be(0);
        black.ManaGenerated.Red.Should().Be(0);
        black.ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — surveil 1
    // -----------------------------------------------------------------------

    [Fact]
    public void UndergroundMortuary_HasExactlyOneTriggeredAbility()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "only the ETB surveil trigger");
    }

    [Fact]
    public void UndergroundMortuary_EtbTrigger_IsBattlefieldActive()
    {
        var land = UndergroundMortuaryFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are active on the battlefield (default active zone)");
    }

    // -----------------------------------------------------------------------
    // Surveil effect execution — default-all-graveyard decision
    // -----------------------------------------------------------------------

    [Fact]
    public void UndergroundMortuary_SurveilEffect_PutsTopCardInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Card("Top Card", "");
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var land = UndergroundMortuaryFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(topCard,
            "default surveil decision sends the top card to the graveyard");
        alice.Zones.Library.GetCards().Should().NotContain(topCard,
            "top card is removed from the library by surveil");
        topCard.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void UndergroundMortuary_SurveilEffect_OnlyMovesTopCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        var second = new Card("Second", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(second);
        second.SetZone(ZoneType.Library);

        var land = UndergroundMortuaryFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top, "only the top card (surveil 1) is moved");
        alice.Zones.Library.GetCards().Should().Contain(second,
            "second card is unaffected by surveil 1");
    }

    [Fact]
    public void UndergroundMortuary_SurveilEffect_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);
        // Library intentionally left empty

        var land = UndergroundMortuaryFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in trigger.Effects) effect.Execute(); };

        act.Should().NotThrow("surveil on empty library is a no-op");
    }

    [Fact]
    public void UndergroundMortuary_SurveilEffect_ResolvesWithoutThrowing()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = UndergroundMortuaryFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => trigger.Resolve();

        act.Should().NotThrow("trigger resolve executes the surveil effect without error");
    }
}

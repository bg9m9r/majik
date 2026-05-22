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
/// Unit tests for <see cref="ThunderingFallsFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Two mana abilities ({U} + {R})
/// - ETB triggered ability presence + battlefield-active default
/// - Surveil 1 effect moves top card to graveyard (default decision)
/// </summary>
public class ThunderingFallsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ThunderingFalls_IsLand()
    {
        var land = ThunderingFallsFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ThunderingFalls_NameIsCorrect()
    {
        var land = ThunderingFallsFactory.Create(_alice);

        land.Name.Should().Be("Thundering Falls");
    }

    [Fact]
    public void ThunderingFalls_OwnerAndControllerAreSet()
    {
        var land = ThunderingFallsFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThunderingFalls_IsNotLegendary()
    {
        var land = ThunderingFallsFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ThunderingFalls_HasTwoManaAbilities()
    {
        var land = ThunderingFallsFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void ThunderingFalls_HasBlueManaAbility()
    {
        var land = ThunderingFallsFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void ThunderingFalls_HasRedManaAbility()
    {
        var land = ThunderingFallsFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void ThunderingFalls_HasExactlyOneTriggeredAbility()
    {
        var land = ThunderingFallsFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "only the ETB surveil 1 trigger");
    }

    [Fact]
    public void ThunderingFalls_EtbTrigger_IsBattlefieldActive()
    {
        var land = ThunderingFallsFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void ThunderingFalls_SurveilEffect_PutsTopCardInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = ThunderingFallsFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top,
            "default surveil decision puts the top card in the graveyard");
        top.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void ThunderingFalls_SurveilEffect_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);
        var land = ThunderingFallsFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in trigger.Effects) effect.Execute(); };

        act.Should().NotThrow();
    }
}

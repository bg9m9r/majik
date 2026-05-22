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
/// Unit tests for <see cref="ElegantParlorFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Two mana abilities ({R} + {W})
/// - ETB triggered ability presence + battlefield-active default
/// - Surveil 1 effect moves top card to graveyard (default decision)
/// </summary>
public class ElegantParlorTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ElegantParlor_IsLand()
    {
        var land = ElegantParlorFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ElegantParlor_NameIsCorrect()
    {
        var land = ElegantParlorFactory.Create(_alice);

        land.Name.Should().Be("Elegant Parlor");
    }

    [Fact]
    public void ElegantParlor_OwnerAndControllerAreSet()
    {
        var land = ElegantParlorFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ElegantParlor_HasTwoManaAbilities()
    {
        var land = ElegantParlorFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void ElegantParlor_HasRedManaAbility()
    {
        var land = ElegantParlorFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void ElegantParlor_HasWhiteManaAbility()
    {
        var land = ElegantParlorFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void ElegantParlor_HasExactlyOneTriggeredAbility()
    {
        var land = ElegantParlorFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ElegantParlor_EtbTrigger_IsBattlefieldActive()
    {
        var land = ElegantParlorFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void ElegantParlor_SurveilEffect_PutsTopCardInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = ElegantParlorFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Graveyard);
    }
}

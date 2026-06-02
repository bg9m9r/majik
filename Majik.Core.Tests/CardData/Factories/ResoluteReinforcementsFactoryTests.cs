using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ResoluteReinforcementsFactory"/>.
///
/// Resolute Reinforcements (Murders at Karlov Manor Commander, {1}{W}):
///   Creature — Human Soldier 1/1.
///   Flash
///   When this creature enters, create a 1/1 white Soldier creature token.
///
/// Closest shipped analogue: <see cref="GoblinInstigatorFactory"/> (same
/// {N}{C} 1/1 + ETB "create a 1/1 token") — Resolute Reinforcements is the
/// white-Soldier, Flash-bearing analogue, reusing
/// <see cref="RaiseTheAlarmFactory.CreateSoldierToken"/> for the token shape.
///
/// Covers:
///   - Identity (Human Soldier 1/1, {1}{W}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Flash keyword marker (CR 702.8).
///   - Exactly one ETB trigger (Battlefield active), self-match.
///   - Resolving the ETB trigger creates exactly one 1/1 white Soldier
///     creature token under the card's controller.
/// </summary>
public class ResoluteReinforcementsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Count == 1
                && t.ActiveZones.Contains(ZoneType.Battlefield));

    [Fact]
    public void ResoluteReinforcements_Identity()
    {
        var c = ResoluteReinforcementsFactory.Create(_alice);

        c.Name.Should().Be("Resolute Reinforcements");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ResoluteReinforcements_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Resolute Reinforcements", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Resolute Reinforcements");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    [Fact]
    public void ResoluteReinforcements_HasFlashKeyword()
    {
        var c = ResoluteReinforcementsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "Resolute Reinforcements has Flash (CR 702.8).");
    }

    [Fact]
    public void ResoluteReinforcements_HasExactlyOneEtbTrigger()
    {
        var c = ResoluteReinforcementsFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1,
            "Resolute Reinforcements has exactly one ETB token-creation trigger.");

        var etb = GetEtbTrigger(c);
        etb.ActiveZones.Should().BeEquivalentTo(new[] { ZoneType.Battlefield });
    }

    [Fact]
    public void ResoluteReinforcements_EtbTrigger_FiresOnEnterBattlefield()
    {
        var c = ResoluteReinforcementsFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var etb = GetEtbTrigger(c);
        var enter = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.IsTriggered(enter).Should().BeTrue(
            "ETB trigger fires when Resolute Reinforcements enters the battlefield (CR 603.6a).");
    }

    [Fact]
    public void ResoluteReinforcements_EtbTrigger_DoesNotFire_OnOtherCardEntering()
    {
        var c = ResoluteReinforcementsFactory.Create(_alice);
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);

        var etb = GetEtbTrigger(c);
        var other = new CardMovedEvent(bears, ZoneType.Hand, ZoneType.Battlefield);
        etb.IsTriggered(other).Should().BeFalse(
            "the ETB trigger is self-match — other creatures entering don't fire it.");
    }

    [Fact]
    public void ResoluteReinforcements_EtbResolve_CreatesOneOneWhiteSoldierToken()
    {
        var c = ResoluteReinforcementsFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var soldiersBefore = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(s => s.HasSubtype(CardSubtype.Soldier) && s.IsToken);

        var etb = GetEtbTrigger(c);
        foreach (var e in etb.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(s => s.HasSubtype(CardSubtype.Soldier) && s.IsToken)
            .ToList();
        tokens.Should().HaveCount(soldiersBefore + 1,
            "CR 111 — the ETB trigger creates exactly one 1/1 white Soldier token.");

        var token = tokens.Last();
        token.Name.Should().Be("Soldier");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
        token.TokenColorsOverride.Should().NotBeNull();
        token.TokenColorsOverride!.Should().Contain(ManaColor.White,
            "the token is white per the printed clause (CR 105 / 111.4).");
    }
}

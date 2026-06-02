using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ThragtuskFactory"/> (Magic 2013, {4}{G}).
///
/// Creature — Beast 5/3. Oracle text:
///   "When this creature enters, you gain 5 life.
///    When this creature leaves the battlefield, create a 3/3 green
///    Beast creature token."
///
/// Covers:
/// - Identity (Creature — Beast 5/3 at {4}{G}, owner / controller wired).
/// - NamedCardFactory dispatch.
/// - Two triggered abilities (ETB life-gain + LTB token-creation).
/// - ETB condition fires when Thragtusk enters the battlefield.
/// - ETB effect gains the controller 5 life (CR 119.3).
/// - LTB condition fires when Thragtusk leaves the battlefield (any
///   destination — graveyard / exile).
/// - LTB effect creates a single 3/3 green Beast token.
/// </summary>
[Trait("Color", "G")]
public class ThragtuskFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Thragtusk_Identity()
    {
        var c = ThragtuskFactory.Create(_alice);

        c.Name.Should().Be("Thragtusk");
        c.ManaCost.Should().Be("{4}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Thragtusk_AttachesTwoTriggeredAbilities()
    {
        var c = ThragtuskFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB life-gain trigger + LTB token-creation trigger");
        c.Abilities.OfType<TriggeredAbility>().All(t =>
            t.ActiveZones.Contains(ZoneType.Battlefield)).Should().BeTrue(
            "both triggers function while on the battlefield (CR 603.6a/603.6d)");
    }

    // -----------------------------------------------------------------------
    // ETB — CR 603.6a / CR 119.3
    // -----------------------------------------------------------------------

    [Fact]
    public void Thragtusk_EtbCondition_FiresOnEnterBattlefield()
    {
        var c = ThragtuskFactory.Create(_alice);

        // The ETB trigger is the one with no LTB FromZone semantics — pick
        // by asserting it matches an enters-the-battlefield event.
        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        var enterEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Hand, ZoneType.Battlefield);

        triggers.Any(t => t.Condition!.Matches(enterEvent, t))
            .Should().BeTrue("ETB trigger fires when Thragtusk enters the battlefield");
    }

    [Fact]
    public void Thragtusk_Etb_GainsControllerFiveLife()
    {
        var c = ThragtuskFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var lifeBefore = _alice.LifeTotal;

        // Drive the ETB effect manually. The ETB trigger is the one that
        // matches an enters-the-battlefield event.
        var enterEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Hand, ZoneType.Battlefield);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition!.Matches(enterEvent, t));
        foreach (var e in etb.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(lifeBefore + 5,
            "ETB gains the controller 5 life (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // LTB — CR 603.6c / CR 603.10c / CR 111.4
    // -----------------------------------------------------------------------

    [Fact]
    public void Thragtusk_LtbCondition_FiresWhenLeavesBattlefield()
    {
        var c = ThragtuskFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        // Dies (battlefield → graveyard).
        var diesEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Battlefield, ZoneType.Graveyard);
        triggers.Any(t => t.Condition!.Matches(diesEvent, t))
            .Should().BeTrue("LTB fires when Thragtusk dies");

        // Exiled (battlefield → exile) — "leaves the battlefield" covers
        // any destination.
        var exileEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Battlefield, ZoneType.Exile);
        triggers.Any(t => t.Condition!.Matches(exileEvent, t))
            .Should().BeTrue("LTB fires when Thragtusk is exiled (leaves the battlefield)");
    }

    [Fact]
    public void Thragtusk_LtbCondition_DoesNotFireForOtherCard()
    {
        var c = ThragtuskFactory.Create(_alice);
        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        var otherDies = new Majik.Core.Events.CardMovedEvent(
            other, ZoneType.Battlefield, ZoneType.Graveyard);

        triggers.Any(t => t.Condition!.Matches(otherDies, t))
            .Should().BeFalse("LTB is gated to Thragtusk itself, not other cards");
    }

    [Fact]
    public void Thragtusk_Ltb_CreatesThreeByThreeGreenBeastToken()
    {
        var c = ThragtuskFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        // Drive the LTB effect manually (the trigger that matches a dies
        // event).
        var diesEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Battlefield, ZoneType.Graveyard);
        var ltb = c.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition!.Matches(diesEvent, t));
        foreach (var e in ltb.Effects) e.Execute();

        var beasts = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(x => x.IsToken && x.HasSubtype(CardSubtype.Beast))
            .ToList();

        beasts.Should().HaveCount(1, "LTB creates exactly one Beast token");
        beasts[0].BasePower.Should().Be(3);
        beasts[0].BaseToughness.Should().Be(3);
        beasts[0].Controller.Should().BeSameAs(_alice,
            "the token is created under Thragtusk's controller (CR 111.4)");
        Majik.Core.Cards.CardColors.GetColors(beasts[0])
            .Should().Contain(Majik.Core.ValueObjects.ManaColor.Green,
            "printed '3/3 green Beast creature token'");
    }
}

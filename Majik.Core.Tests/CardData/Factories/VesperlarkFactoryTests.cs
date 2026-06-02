using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VesperlarkFactory"/> (Morningtide, {2}{W}).
///
/// Creature — Elemental 2/1. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature leaves the battlefield, return target creature card
///    with power 1 or less from your graveyard to the battlefield.
///    Evoke {1}{W}"
///
/// Covers:
/// - Identity (Creature — Elemental 2/1 at {2}{W}, owner / controller wired).
/// - Keyword markers — Flying + Evoke (CR 702.9 / CR 702.74).
/// - Evoke sacrifice trigger has the intervening-if reading EvokeWasPaid
///   (CR 702.74b).
/// - LTB condition fires when Vesperlark leaves the battlefield (any
///   destination — graveyard / exile), and only for itself.
/// - LTB effect returns one power-1-or-less creature card from the
///   controller's graveyard to the battlefield; respects the single-target
///   cap and the "power 1 or less" filter; empty / no-legal-target = no-op.
/// </summary>
[Trait("Color", "W")]
public class VesperlarkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Vesperlark_Identity()
    {
        var c = VesperlarkFactory.Create(_alice);

        c.Name.Should().Be("Vesperlark");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Vesperlark_HasFlyingAndEvokeMarkers()
    {
        var c = VesperlarkFactory.Create(_alice);

        var keywordNames = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flying", "Evoke" });
    }

    // -----------------------------------------------------------------------
    // Evoke sacrifice intervening-if — CR 702.74b
    // -----------------------------------------------------------------------

    [Fact]
    public void Vesperlark_EvokeSacTrigger_HasInterveningIf_ReadsEvokeWasPaid()
    {
        var c = VesperlarkFactory.Create(_alice);

        var sacTrigger = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is not null);

        c.EvokeWasPaid.Should().BeFalse();
        sacTrigger.InterveningIf!().Should().BeFalse(
            "CR 603.4 — Evoke sacrifice trigger drops at queue-time when EvokeWasPaid is false");

        c.EvokeWasPaid = true;
        sacTrigger.InterveningIf!().Should().BeTrue(
            "CR 702.74b — Evoke sacrifice trigger queues when the alt-cost was paid");
    }

    // -----------------------------------------------------------------------
    // LTB condition — CR 603.6c / CR 603.10c
    // -----------------------------------------------------------------------

    [Fact]
    public void Vesperlark_LtbCondition_FiresWhenLeavesBattlefield()
    {
        var c = VesperlarkFactory.Create(_alice);

        var ltb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        var diesEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.Condition!.Matches(diesEvent, ltb)
            .Should().BeTrue("LTB fires when Vesperlark dies");

        var exileEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Battlefield, ZoneType.Exile);
        ltb.Condition!.Matches(exileEvent, ltb)
            .Should().BeTrue("LTB fires when Vesperlark is exiled (leaves the battlefield)");
    }

    [Fact]
    public void Vesperlark_LtbCondition_DoesNotFireForOtherCard()
    {
        var c = VesperlarkFactory.Create(_alice);
        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        var ltb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        var otherDies = new Majik.Core.Events.CardMovedEvent(
            other, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.Condition!.Matches(otherDies, ltb)
            .Should().BeFalse("LTB is gated to Vesperlark itself, not other cards");
    }

    // -----------------------------------------------------------------------
    // LTB effect — CR 701.20 reanimation
    // -----------------------------------------------------------------------

    [Fact]
    public void Vesperlark_Ltb_ReturnsOnePowerOneOrLessCreature()
    {
        var alice = new Player("Alice", 20);

        // Graveyard: one power<=1 creature + one power-2 (illegal) creature.
        var small = MakeGraveyardCreature(alice, "Small One", 1, 1);
        var big = MakeGraveyardCreature(alice, "Big One", 2, 2);

        var vesperlark = VesperlarkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(vesperlark);
        vesperlark.SetZone(ZoneType.Battlefield);

        var ltb = vesperlark.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        foreach (var e in ltb.Effects) e.Execute();

        // The power<=1 creature returned to battlefield.
        alice.Zones.Battlefield.GetCards().Should().Contain(small);
        small.Zone.Should().Be(ZoneType.Battlefield);

        // Power-2 creature stays in the graveyard (CR — "power 1 or less").
        alice.Zones.Graveyard.GetCards().Should().Contain(big);
        big.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Vesperlark_Ltb_ReturnsAtMostOneCreature()
    {
        var alice = new Player("Alice", 20);

        var a = MakeGraveyardCreature(alice, "A", 1, 1);
        var b = MakeGraveyardCreature(alice, "B", 0, 1);

        var vesperlark = VesperlarkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(vesperlark);
        vesperlark.SetZone(ZoneType.Battlefield);

        var ltb = vesperlark.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);
        foreach (var e in ltb.Effects) e.Execute();

        var returned = new[] { a, b }
            .Count(x => x.Zone == ZoneType.Battlefield);
        returned.Should().Be(1, "CR — Vesperlark returns a single target creature card");
    }

    [Fact]
    public void Vesperlark_Ltb_EmptyGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var vesperlark = VesperlarkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(vesperlark);
        vesperlark.SetZone(ZoneType.Battlefield);

        var ltb = vesperlark.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        var act = () => { foreach (var e in ltb.Effects) e.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Should().ContainSingle(x => ReferenceEquals(x, vesperlark),
            "no legal targets in the graveyard → clean no-op (CR 608.2b)");
    }

    private static Creature MakeGraveyardCreature(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, "{1}", power, toughness);
        c.SetOwner(owner);
        owner.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        return c;
    }
}

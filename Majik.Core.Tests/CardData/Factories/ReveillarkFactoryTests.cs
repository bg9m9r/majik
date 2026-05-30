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
/// Unit tests for <see cref="ReveillarkFactory"/> (Morningtide, {4}{W}).
///
/// Creature — Elemental 4/3. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature leaves the battlefield, return up to two target
///    creature cards with power 2 or less from your graveyard to the
///    battlefield.
///    Evoke {5}{W}"
///
/// Covers:
/// - Identity (Creature — Elemental 4/3 at {4}{W}, owner / controller wired).
/// - NamedCardFactory dispatch.
/// - Keyword markers — Flying + Evoke (CR 702.9 / CR 702.74).
/// - Evoke sacrifice trigger has the intervening-if reading EvokeWasPaid
///   (CR 702.74b).
/// - LTB condition fires when Reveillark leaves the battlefield (any
///   destination — graveyard / exile), and only for itself.
/// - LTB effect returns up to two power-2-or-less creature cards from the
///   controller's graveyard to the battlefield; respects the "up to two"
///   cap and the "power 2 or less" filter; empty / no-legal-target = no-op.
/// </summary>
public class ReveillarkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Reveillark_Identity()
    {
        var c = ReveillarkFactory.Create(_alice);

        c.Name.Should().Be("Reveillark");
        c.ManaCost.Should().Be("{4}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Reveillark_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Reveillark", _alice);

        card.Should().BeOfType<Creature>("Reveillark is a Creature");
        card.Name.Should().Be("Reveillark");
        card.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Reveillark_HasFlyingAndEvokeMarkers()
    {
        var c = ReveillarkFactory.Create(_alice);

        var keywordNames = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flying", "Evoke" });
    }

    // -----------------------------------------------------------------------
    // Evoke sacrifice intervening-if — CR 702.74b
    // -----------------------------------------------------------------------

    [Fact]
    public void Reveillark_EvokeSacTrigger_HasInterveningIf_ReadsEvokeWasPaid()
    {
        var c = ReveillarkFactory.Create(_alice);

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
    public void Reveillark_LtbCondition_FiresWhenLeavesBattlefield()
    {
        var c = ReveillarkFactory.Create(_alice);

        // The LTB trigger is the one with no intervening-if (the evoke-sac
        // trigger is the intervening-if one).
        var ltb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        var diesEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.Condition!.Matches(diesEvent, ltb)
            .Should().BeTrue("LTB fires when Reveillark dies");

        var exileEvent = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Battlefield, ZoneType.Exile);
        ltb.Condition!.Matches(exileEvent, ltb)
            .Should().BeTrue("LTB fires when Reveillark is exiled (leaves the battlefield)");
    }

    [Fact]
    public void Reveillark_LtbCondition_DoesNotFireForOtherCard()
    {
        var c = ReveillarkFactory.Create(_alice);
        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        var ltb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        var otherDies = new Majik.Core.Events.CardMovedEvent(
            other, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.Condition!.Matches(otherDies, ltb)
            .Should().BeFalse("LTB is gated to Reveillark itself, not other cards");
    }

    // -----------------------------------------------------------------------
    // LTB effect — CR 701.20 reanimation
    // -----------------------------------------------------------------------

    [Fact]
    public void Reveillark_Ltb_ReturnsUpToTwoPowerTwoOrLessCreatures()
    {
        var alice = new Player("Alice", 20);

        // Graveyard: two power<=2 creatures + one power-3 (illegal) creature.
        var small1 = MakeGraveyardCreature(alice, "Small One", 1, 1);
        var small2 = MakeGraveyardCreature(alice, "Small Two", 2, 3);
        var big = MakeGraveyardCreature(alice, "Big One", 3, 3);

        var reveillark = ReveillarkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(reveillark);
        reveillark.SetZone(ZoneType.Battlefield);

        var ltb = reveillark.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        foreach (var e in ltb.Effects) e.Execute();

        // Both power<=2 creatures returned to battlefield.
        alice.Zones.Battlefield.GetCards().Should().Contain(small1);
        alice.Zones.Battlefield.GetCards().Should().Contain(small2);
        small1.Zone.Should().Be(ZoneType.Battlefield);
        small2.Zone.Should().Be(ZoneType.Battlefield);

        // Power-3 creature stays in the graveyard (CR — "power 2 or less").
        alice.Zones.Graveyard.GetCards().Should().Contain(big);
        big.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Reveillark_Ltb_CapsAtTwoCreatures()
    {
        var alice = new Player("Alice", 20);

        var a = MakeGraveyardCreature(alice, "A", 1, 1);
        var b = MakeGraveyardCreature(alice, "B", 1, 1);
        var cc = MakeGraveyardCreature(alice, "C", 1, 1);

        var reveillark = ReveillarkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(reveillark);
        reveillark.SetZone(ZoneType.Battlefield);

        var ltb = reveillark.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);
        foreach (var e in ltb.Effects) e.Execute();

        var returned = new[] { a, b, cc }
            .Count(x => x.Zone == ZoneType.Battlefield);
        returned.Should().Be(2, "CR — 'up to two' caps the reanimation at two cards");
    }

    [Fact]
    public void Reveillark_Ltb_EmptyGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var reveillark = ReveillarkFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(reveillark);
        reveillark.SetZone(ZoneType.Battlefield);

        var ltb = reveillark.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        var act = () => { foreach (var e in ltb.Effects) e.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Should().ContainSingle(x => ReferenceEquals(x, reveillark),
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

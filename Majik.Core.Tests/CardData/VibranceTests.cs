using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Vibrance (Modern Horizons 3, {3}{R/G}{R/G}, Creature —
/// Elemental Incarnation 4/4).
///
/// Covers:
/// - Identity (Elemental + Incarnation, {3}{R/G}{R/G}, 4/4).
/// - NamedCardFactory dispatch.
/// - Evoke marker + sacrifice trigger.
/// - RR-conditional ETB: deals 3 to any target ONLY when {R}{R} spent
///   (intervening-if), no-op for {R}{G} / {G}{G}.
/// - GG-conditional ETB: tutor a land + gain 2 life ONLY when {G}{G} spent.
/// - {R}{G} cast: NEITHER conditional fires (the deferral #15 bug — distinct
///   set couldn't tell {R}{G} apart; the count ledger can).
/// </summary>
public class VibranceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static IReadOnlyDictionary<ManaColor, int> Counts(
        params (ManaColor Color, int N)[] pairs)
    {
        var d = new Dictionary<ManaColor, int>();
        foreach (var (c, n) in pairs) d[c] = n;
        return d;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Vibrance_Identity()
    {
        var v = VibranceFactory.Create(_alice);

        v.Name.Should().Be("Vibrance");
        v.ManaCost.Should().Be("{3}{R/G}{R/G}");
        v.HasType(CardType.Creature).Should().BeTrue();
        v.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        v.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();
        v.BasePower.Should().Be(4);
        v.BaseToughness.Should().Be(4);
        v.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Vibrance_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Vibrance", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Vibrance");
        c.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();
    }

    [Fact]
    public void Vibrance_HasEvokeMarker()
    {
        var v = VibranceFactory.Create(_alice);

        v.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Evoke");
    }

    // -----------------------------------------------------------------------
    // RR ETB — deals 3 to any target only when {R}{R} spent
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_RRSpent_Deals3ToTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var v = VibranceFactory.Create(_alice);
        v.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(v);
        triggers.BindCard(v);

        var bear = new Creature("Bear", "{1}{G}", 4, 4);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        // {R}{R} spent on the cast.
        v.SetPendingCastColorCounts(Counts((ManaColor.Red, 2)));

        zones.MoveCardTo(v, ZoneType.Battlefield);

        // Only the RR trigger should be pending (GG intervening-if false).
        var rr = v.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        rr.SetChosenTargets(new[] { new[] { (object)bear } });

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        bear.Damage.Should().Be(3, "{R}{R} spent → 3 damage to any target");
    }

    [Fact]
    public void Etb_GGSpent_RRTriggerDoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var v = VibranceFactory.Create(_alice);
        v.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(v);
        triggers.BindCard(v);

        var bear = new Creature("Bear", "{1}{G}", 4, 4);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        // {G}{G} spent — RR intervening-if is false.
        v.SetPendingCastColorCounts(Counts((ManaColor.Green, 2)));

        zones.MoveCardTo(v, ZoneType.Battlefield);
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        bear.Damage.Should().Be(0, "no {R}{R} spent → RR damage trigger never fires");
    }

    [Fact]
    public void Etb_RGSpent_NeitherConditionalFires()
    {
        // The deferral #15 bug: distinct set {R,G} couldn't tell {R}{G} from
        // {R}{R} / {G}{G}. With the count ledger, {R}{G} satisfies NEITHER
        // "≥2 of one color" intervening-if.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var v = VibranceFactory.Create(_alice);
        v.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(v);
        triggers.BindCard(v);

        var bear = new Creature("Bear", "{1}{G}", 4, 4);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var aliceLife = _alice.LifeTotal;

        // {R}{G} spent (one each) — neither GG nor RR.
        v.SetPendingCastColorCounts(Counts((ManaColor.Red, 1), (ManaColor.Green, 1)));

        zones.MoveCardTo(v, ZoneType.Battlefield);
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        bear.Damage.Should().Be(0, "{R}{G} is not {R}{R} → no damage");
        _alice.LifeTotal.Should().Be(aliceLife, "{R}{G} is not {G}{G} → no lifegain");
    }

    // -----------------------------------------------------------------------
    // GG ETB — tutor a land + gain 2 life only when {G}{G} spent
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_GGSpent_TutorsLandAndGains2Life()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var v = VibranceFactory.Create(_alice);
        v.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(v);
        triggers.BindCard(v);

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(forest);

        var aliceLife = _alice.LifeTotal;

        // {G}{G} spent.
        v.SetPendingCastColorCounts(Counts((ManaColor.Green, 2)));

        zones.MoveCardTo(v, ZoneType.Battlefield);
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(forest,
            "{G}{G} spent → search a land to hand");
        (_alice.LifeTotal - aliceLife).Should().Be(2, "gain 2 life");
    }

    [Fact]
    public void Etb_RRSpent_GGTriggerDoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var v = VibranceFactory.Create(_alice);
        v.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(v);
        triggers.BindCard(v);

        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(forest);

        var aliceLife = _alice.LifeTotal;

        // {R}{R} only — GG intervening-if false. (No target set for RR; the
        // RR effect no-ops harmlessly without a target.)
        v.SetPendingCastColorCounts(Counts((ManaColor.Red, 2)));

        zones.MoveCardTo(v, ZoneType.Battlefield);
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(forest,
            "no {G}{G} → no tutor");
        _alice.LifeTotal.Should().Be(aliceLife, "no {G}{G} → no lifegain");
    }
}

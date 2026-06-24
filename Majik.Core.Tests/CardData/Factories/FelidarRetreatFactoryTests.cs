using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FelidarRetreatFactory"/> (Zendikar Rising,
/// {3}{W}, Enchantment).
///
/// Oracle text (verified against Scryfall):
///   "Landfall — Whenever a land you control enters, choose one —
///      • Create a 2/2 white Cat Beast creature token.
///      • Put a +1/+1 counter on each creature you control. Those creatures
///        gain vigilance until end of turn."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (Enchantment, white, {3}{W}, CMC 4; exactly one TriggeredAbility).
/// - End-to-end landfall trigger via bus + stack: a land entering under the
///   controller fires the trigger and resolves into a 2/2 white Cat Beast
///   token by default (no agent registered → deterministic token pick).
/// - Trigger does NOT fire when an opponent controls the entering land
///   ("under YOUR control"); does NOT fire for non-land ETB.
/// - Mode 1 (direct): +1/+1 counter on each creature you control + vigilance
///   until end of turn, with cleanup expiry (CR 514.2).
/// </summary>
[Trait("Color", "W")]
public class FelidarRetreatFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void FelidarRetreat_Identity()
    {
        var c = FelidarRetreatFactory.Create(_alice);

        c.Name.Should().Be("Felidar Retreat");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        CardColors.GetColors(c).Should().Contain(ManaColor.White);
        c.ManaCostValue.TotalValue.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void LandEntersUnderController_DefaultsToCatBeastToken()
    {
        var (zones, stack, triggers) = BuildEngine();

        var retreat = FelidarRetreatFactory.Create(_alice, zones, triggers);
        retreat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(retreat);

        var plains = new Land("Plains",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(plains);
        plains.SetZone(ZoneType.Hand);

        zones.MoveCardTo(plains, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "exactly one landfall trigger should be queued");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // No agent registered → token default. A 2/2 white Cat Beast token.
        var cats = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.IsToken && t.HasSubtype(CardSubtype.Cat) && t.HasSubtype(CardSubtype.Beast))
            .ToList();
        cats.Should().HaveCount(1, "default mode pick (no agent) creates a Cat Beast token");
        cats[0].BasePower.Should().Be(2);
        cats[0].BaseToughness.Should().Be(2);
        CardColors.GetColors(cats[0]).Should().Equal(ManaColor.White);
    }

    [Fact]
    public void LandEntersUnderOpponent_NoTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var retreat = FelidarRetreatFactory.Create(_alice, zones, triggers);
        retreat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(retreat);

        // Bob plays a land — Alice's Felidar Retreat must NOT trigger.
        var bobPlains = new Land("Plains",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        bobPlains.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobPlains);
        bobPlains.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobPlains, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "opponent's land does not satisfy 'under your control'");
    }

    [Fact]
    public void NonLandEnters_NoTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var retreat = FelidarRetreatFactory.Create(_alice, zones, triggers);
        retreat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(retreat);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "trigger gates on HasType(Land); a creature ETB doesn't match");
    }

    [Fact]
    public void Mode1_PutsCounterOnEachCreatureAndGrantsVigilanceUntilEndOfTurn()
    {
        var a = NewBattlefieldCreature("A");
        var b = NewBattlefieldCreature("B");

        FelidarRetreatFactory.ApplyCountersAndVigilance(_alice);

        // CR 122 — one +1/+1 counter on each creature you control.
        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        b.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // CR 702.21 — those creatures gain vigilance until end of turn.
        CombatAbilities.HasVigilance(a).Should().BeTrue();
        CombatAbilities.HasVigilance(b).Should().BeTrue();

        // CR 514.2 — the grant expires in the cleanup step; the counter stays.
        a.ActiveEffects!.ExpireEndOfTurn();
        b.ActiveEffects!.ExpireEndOfTurn();

        CombatAbilities.HasVigilance(a).Should().BeFalse();
        CombatAbilities.HasVigilance(b).Should().BeFalse();
        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the +1/+1 counter is permanent; only vigilance is until end of turn");
    }

    private Creature NewBattlefieldCreature(string name)
    {
        var c = new Creature(name, "{1}{W}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}

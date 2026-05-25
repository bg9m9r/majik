using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Munitions Expert (Mercadian Masques, {R}, Creature — Goblin
/// Warrior 1/1).
///
/// Covers:
/// - Identity (Goblin + Warrior, {R}, 1/1, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB trigger shape: declares one 1..1 any-target request.
/// - ETB resolution: deals X damage = Goblins controller controls
///   (including Munitions Expert himself).
/// - Solo Munitions Expert: X = 1.
/// - With two friendly Goblins: X = 3.
/// - Opponent Goblins are NOT counted.
/// - No target picked (optional "may") = no-op.
/// </summary>
public class MunitionsExpertTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeGoblin(Player owner, string name = "Mogg Fanatic")
    {
        var c = new Creature(name, "{R}", 1, 1, subtypes: new[] { CardSubtype.Goblin });
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MunitionsExpert_Identity()
    {
        var me = MunitionsExpertFactory.Create(_alice);

        me.Name.Should().Be("Munitions Expert");
        me.ManaCost.Should().Be("{R}");
        me.HasType(CardType.Creature).Should().BeTrue();
        me.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        me.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        me.BasePower.Should().Be(MunitionsExpertFactory.Power);
        me.BaseToughness.Should().Be(MunitionsExpertFactory.Toughness);
        me.Owner.Should().BeSameAs(_alice);
        me.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MunitionsExpert_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Munitions Expert", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Munitions Expert");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MunitionsExpert_HasEtbTrigger_WithSingleAnyTargetRequest()
    {
        var me = MunitionsExpertFactory.Create(_alice);

        var etb = me.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolution — X = 1 (solo Munitions Expert)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_Solo_Deals1DamageToTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var me = MunitionsExpertFactory.Create(_alice);
        me.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(me);
        triggers.BindCard(me);

        // Bob's bear absorbs damage.
        var bear = new Creature("Bear", "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(me, ZoneType.Battlefield);

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1);

        var etb = me.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new[] { new[] { (object)bear } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Damage.Should().Be(1,
            "solo Munitions Expert = 1 Goblin he controls (himself) → X = 1 damage");
    }

    // -----------------------------------------------------------------------
    // Resolution — X = 3 (Munitions Expert + two friendly Goblins)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_WithTwoFriendlyGoblins_Deals3DamageToTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Two friendly Goblins already on the battlefield.
        MakeGoblin(_alice, "Mogg Fanatic");
        MakeGoblin(_alice, "Goblin Lackey");

        var me = MunitionsExpertFactory.Create(_alice);
        me.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(me);
        triggers.BindCard(me);

        var bear = new Creature("Bear", "{1}{G}", 4, 4, subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(me, ZoneType.Battlefield);

        var etb = me.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new[] { new[] { (object)bear } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Damage.Should().Be(3,
            "Munitions Expert + 2 friends = 3 Goblins → X = 3 damage");
    }

    // -----------------------------------------------------------------------
    // Resolution — opponent Goblins are not counted
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_OpponentGoblins_NotCounted()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Bob has a Goblin; Alice has only Munitions Expert (after ETB).
        MakeGoblin(_bob, "Bob's Goblin");

        var me = MunitionsExpertFactory.Create(_alice);
        me.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(me);
        triggers.BindCard(me);

        var bobLife = _bob.LifeTotal;
        zones.MoveCardTo(me, ZoneType.Battlefield);

        var etb = me.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new[] { new[] { (object)_bob } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        (bobLife - _bob.LifeTotal).Should().Be(1,
            "CR 109.5 — Munitions Expert counts only Alice's Goblins (just himself)");
    }

    // -----------------------------------------------------------------------
    // Optional "may" — no target picked = no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_NoTargetPicked_IsNoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var me = MunitionsExpertFactory.Create(_alice);
        me.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(me);
        triggers.BindCard(me);

        var bobLife = _bob.LifeTotal;
        zones.MoveCardTo(me, ZoneType.Battlefield);

        // No target picked — the "may" rider declines / no target chosen.
        var etb = me.Abilities.OfType<TriggeredAbility>().Single();
        // Leave ChosenTargets empty.

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLife,
            "no target picked → effect is a no-op (CR 605.1)");
    }
}

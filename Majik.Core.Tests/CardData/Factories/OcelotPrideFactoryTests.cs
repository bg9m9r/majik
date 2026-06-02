using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Ocelot Pride (MH3) — Legendary Creature — Cat {W}
/// 1/1.
///   "Lifelink"
///   "Whenever Ocelot Pride attacks, create a 1/1 white Cat creature
///    token. If you have the city's blessing, instead create two of those
///    tokens."
///   "At the beginning of your end step, if a creature you controlled
///    dealt combat damage to a player this turn, exile this card, then
///    return it to the battlefield under its owner's control."
///
/// Validates:
///   * Card identity (legendary Cat at {W}, 1/1) + dispatcher entry.
///   * Lifelink keyword marker attached (CR 702.15).
///   * CR 508.1f attack trigger creates a single 1/1 Cat token (city's
///     blessing / Ascend gate is stubbed — always 1, see factory xmldoc).
///   * CR 500.4 + CR 701.20 end-step flicker: with a "creature you
///     controlled dealt combat damage to a player this turn" latch set,
///     the trigger exile-and-returns Ocelot Pride under its owner's
///     control; with no such damage, the trigger no-ops.
/// </summary>
[Trait("Color", "W")]
public class OcelotPrideFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ------------------------------------------------------------------
    // Card identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void OcelotPride_IsLegendaryCreatureCat_AtCostW_1_1()
    {
        var ocelot = OcelotPrideFactory.Create(_alice);

        ocelot.Name.Should().Be("Ocelot Pride");
        ocelot.HasType(CardType.Creature).Should().BeTrue();
        ocelot.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Ocelot Pride is Legendary (MH3 print)");
        ocelot.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        ocelot.ManaCost.Should().Be("{W}");
        ocelot.Power.Should().Be(1);
        ocelot.Toughness.Should().Be(1);
        ocelot.Owner.Should().BeSameAs(_alice);
        ocelot.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OcelotPride_HasLifelinkKeyword()
    {
        var ocelot = OcelotPrideFactory.Create(_alice);

        // CR 702.15 — Lifelink keyword marker, consumed by the standard
        // combat-damage life-gain pipeline.
        ocelot.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Lifelink",
                "Ocelot Pride has Lifelink (CR 702.15)");
    }
    // ------------------------------------------------------------------
    // CR 508.1f — attack trigger: 1 Cat token (city's blessing deferred)
    // ------------------------------------------------------------------

    [Fact]
    public void OcelotPride_Attack_CreatesOneOneOneCatToken()
    {
        var alice = new Player("Alice", 20);
        var ocelot = OcelotPrideFactory.Create(alice);

        alice.Zones.Battlefield.AddCard(ocelot);
        ocelot.SetZone(ZoneType.Battlefield);

        // Locate the attack trigger by matching against the printed
        // CreatureAttacksEvent shape.
        var attack = ocelot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CreatureAttacksEvent(ocelot, alice)));

        foreach (var effect in attack.Effects) effect.Execute();

        var cats = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Cat")
            .ToList();

        cats.Should().HaveCount(1,
            "the attack trigger creates exactly one 1/1 Cat token " +
            "(city's blessing / Ascend gate stubbed — always 1, see " +
            "OcelotPrideFactory xmldoc)");
        var cat = cats.Single();
        cat.BasePower.Should().Be(1);
        cat.BaseToughness.Should().Be(1);
        cat.HasType(CardType.Creature).Should().BeTrue();
        cat.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        cat.Controller.Should().BeSameAs(alice,
            "tokens enter under Ocelot Pride's controller (CR 111.6)");
    }

    [Fact]
    public void OcelotPride_Attack_WithCitysBlessing_CreatesTwoCatTokens()
    {
        // CR 702.131 — once the controller has had 10+ permanents the
        // city's blessing latches, and the attack trigger creates two 1/1
        // Cat tokens instead of one.
        var alice = new Player("Alice", 20);
        for (var i = 0; i < 10; i++)
        {
            var dummy = new Creature($"Dummy {i}", "{0}", 1, 1)
            {
                Owner = alice,
                Controller = alice,
            };
            dummy.SetZone(ZoneType.Battlefield);
            alice.Zones.Battlefield.AddCard(dummy);
        }
        alice.EvaluateCitysBlessing();
        alice.HasCitysBlessing.Should().BeTrue(
            "10 permanents pushes the controller past the Ascend threshold (CR 702.131)");

        var ocelot = OcelotPrideFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ocelot);
        ocelot.SetZone(ZoneType.Battlefield);

        var attack = ocelot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CreatureAttacksEvent(ocelot, alice)));

        foreach (var effect in attack.Effects) effect.Execute();

        var cats = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Cat")
            .ToList();

        cats.Should().HaveCount(2,
            "with the city's blessing the attack trigger doubles to two " +
            "1/1 Cat tokens (CR 702.131)");
        cats.Should().AllSatisfy(c =>
        {
            c.BasePower.Should().Be(1);
            c.BaseToughness.Should().Be(1);
            c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
            c.Controller.Should().BeSameAs(alice);
        });
    }

    // ------------------------------------------------------------------
    // CR 500.4 + CR 701.20 — end-step flicker trigger
    // ------------------------------------------------------------------

    /// <summary>
    /// With a creature you controlled having dealt combat damage to a
    /// player this turn, the end-step trigger fires and exile-and-returns
    /// Ocelot Pride under its owner's control. The card ends up back on
    /// the battlefield (same instance — v1 reuses the Card object).
    /// </summary>
    [Fact]
    public void OcelotPride_EndStep_AfterCombatDamageToPlayer_FlickersExileAndReturn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var ocelot = OcelotPrideFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ocelot);
        ocelot.SetZone(ZoneType.Battlefield);

        // A creature Alice controls deals combat damage to Bob. The
        // Ocelot Pride factory subscribes a CombatDamageDealtEvent watcher
        // gated on (source's controller == Ocelot controller, TargetPlayer
        // != null) — this latches the per-turn "dealt combat damage to a
        // player" flag.
        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        bus.Publish(new CombatDamageDealtEvent(bear, _bob, amount: 2));

        // Fire the End step on the controller's turn — the trigger should
        // queue and resolve to exile-and-return Ocelot Pride.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "the end-step flicker trigger fires at the start of the " +
            "controller's End step when the intervening-if latch is set");

        triggers.PutPendingTriggersOnStack(_alice);
        // Resolve any pending triggers (attack trigger might also be in
        // there from registration but it shouldn't fire without a
        // CreatureAttacksEvent — only the end-step flicker should be on
        // the stack).
        while (stack.Count > 0) stack.Pop()!.Resolve();

        ocelot.Zone.Should().Be(ZoneType.Battlefield,
            "Ocelot Pride exits to exile then returns to the battlefield " +
            "in the same resolution (CR 701.20)");
        ocelot.Controller.Should().BeSameAs(_alice,
            "the card returns under its owner's control (CR 110.2)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(ocelot);
        _alice.Zones.Exile.GetCards().Should().NotContain(ocelot,
            "after the flicker the card is no longer in exile");
    }

    /// <summary>
    /// With no combat damage dealt to a player this turn, the end-step
    /// trigger's intervening-if (CR 603.4) fails and the flicker no-ops —
    /// Ocelot Pride stays on the battlefield untouched.
    /// </summary>
    [Fact]
    public void OcelotPride_EndStep_NoCombatDamageToPlayer_DoesNotFlicker()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var ocelot = OcelotPrideFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ocelot);
        ocelot.SetZone(ZoneType.Battlefield);

        // Combat damage was dealt this turn but only to a CREATURE (not a
        // player). The factory's watcher gates on TargetPlayer != null so
        // the latch should remain false.
        var attacker = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        var blocker = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        bus.Publish(new CombatDamageDealtEvent(attacker, blocker, amount: 2));

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        ocelot.Zone.Should().Be(ZoneType.Battlefield,
            "no creature-to-player combat damage → intervening-if (CR 603.4) " +
            "fails → flicker no-ops");
        _alice.Zones.Battlefield.GetCards().Should().Contain(ocelot);
        _alice.Zones.Exile.GetCards().Should().NotContain(ocelot);
    }

    /// <summary>
    /// The per-turn latch resets on TurnStartedEvent — combat damage from
    /// a prior turn must not flicker Ocelot Pride on this turn's end step.
    /// </summary>
    [Fact]
    public void OcelotPride_EndStep_LatchResetsAtTurnStart_NoFlickerFromPriorTurnDamage()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var ocelot = OcelotPrideFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ocelot);
        ocelot.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        bus.Publish(new CombatDamageDealtEvent(bear, _bob, amount: 2));

        // New turn — the latch resets.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        ocelot.Zone.Should().Be(ZoneType.Battlefield,
            "the latch resets on TurnStartedEvent so prior-turn combat " +
            "damage doesn't satisfy the intervening-if");
        _alice.Zones.Exile.GetCards().Should().NotContain(ocelot);
    }
}

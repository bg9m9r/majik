using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Resplendent Angel (Core Set 2019) — Creature —
/// Angel {1}{W}{W} 3/3.
///   "Flying
///    At the beginning of each end step, if you gained 5 or more life this
///    turn, create a 4/4 white Angel creature token with flying and
///    vigilance.
///    {3}{W}{W}{W}: Until end of turn, this creature gets +2/+2 and gains
///    lifelink."
///
/// Validates:
///   * Card identity (Angel at {1}{W}{W}, 3/3) + dispatcher entry + Flying.
///   * The activated {3}{W}{W}{W}: +2/+2 and gains lifelink (Layer 7c pump +
///     Layer 6 keyword grant, both EOT).
///   * CR 603.4 end-step intervening-if: with 5+ life gained this turn the
///     trigger creates a 4/4 white flying+vigilance Angel token; with <5 it
///     no-ops. Fires on EACH player's end step (no controller filter).
///     The per-turn latch resets on TurnStartedEvent.
/// </summary>
[Trait("Color", "W")]
public class ResplendentAngelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ------------------------------------------------------------------
    // Card identity + dispatch + Flying
    // ------------------------------------------------------------------

    [Fact]
    public void ResplendentAngel_IsAngel_AtCost1WW_3_3_WithFlying()
    {
        var angel = ResplendentAngelFactory.Create(_alice);

        angel.Name.Should().Be("Resplendent Angel");
        angel.HasType(CardType.Creature).Should().BeTrue();
        angel.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        angel.ManaCost.Should().Be("{1}{W}{W}");
        angel.Power.Should().Be(3);
        angel.Toughness.Should().Be(3);
        angel.Owner.Should().BeSameAs(_alice);
        angel.Controller.Should().BeSameAs(_alice);

        // CR 702.9 — Flying keyword marker.
        angel.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "Resplendent Angel has Flying (CR 702.9)");
    }
    // ------------------------------------------------------------------
    // {3}{W}{W}{W}: +2/+2 and gains lifelink until end of turn
    // ------------------------------------------------------------------

    [Fact]
    public void ResplendentAngel_HasPumpActivatedAbility_At3WWW()
    {
        var angel = ResplendentAngelFactory.Create(_alice);

        var ability = angel.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().ContainSingle()
            .Which.Should().BeOfType<Majik.Core.Costs.ManaCostCost>();
    }

    [Fact]
    public void ResplendentAngel_PumpAbility_GivesPlus2Plus2AndLifelink_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var angel = ResplendentAngelFactory.Create(
            _alice, zoneService: null, eventBus: null, triggers: null, effects: effects);

        _alice.Zones.Battlefield.AddCard(angel);
        angel.SetZone(ZoneType.Battlefield);

        // Base 3/3, no lifelink.
        angel.Power.Should().Be(3);
        angel.Toughness.Should().Be(3);
        CombatAbilities.HasLifelink(angel).Should().BeFalse(
            "Resplendent Angel has no printed lifelink");

        // Activate {3}{W}{W}{W}: +2/+2 and gains lifelink until EOT.
        var ability = angel.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        angel.Power.Should().Be(5, "CR 613.1f Layer 7c — +2 power until EOT");
        angel.Toughness.Should().Be(5, "CR 613.1f Layer 7c — +2 toughness until EOT");
        CombatAbilities.HasLifelink(angel).Should().BeTrue(
            "CR 613.1c Layer 6 — gains lifelink until EOT");

        // CR 514.2 — both effects expire in the cleanup step.
        effects.ExpireEndOfTurn();
        angel.Power.Should().Be(3);
        angel.Toughness.Should().Be(3);
        CombatAbilities.HasLifelink(angel).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // CR 603.4 — end-step "gained 5+ life this turn" token trigger
    // ------------------------------------------------------------------

    [Fact]
    public void ResplendentAngel_EndStep_AfterGaining5Life_CreatesAngelToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var angel = ResplendentAngelFactory.Create(
            _alice, zones, bus, triggers, effects: null);
        _alice.Zones.Battlefield.AddCard(angel);
        angel.SetZone(ZoneType.Battlefield);

        // Alice gains 5 life this turn → latch satisfies the intervening-if.
        // Route through Player.GainLife so a LifeChangedEvent fires.
        bus.Publish(new LifeChangedEvent(_alice, previousLife: 20, newLife: 25));

        // End step fires (Alice's turn).
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "the end-step token trigger fires when 5+ life was gained this turn");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Angel")
            .ToList();

        tokens.Should().HaveCount(1,
            "with 5+ life gained the trigger creates one 4/4 Angel token");
        var token = tokens.Single();
        token.BasePower.Should().Be(4);
        token.BaseToughness.Should().Be(4);
        token.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying");
        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Vigilance");
        token.Controller.Should().BeSameAs(_alice,
            "the token enters under Resplendent Angel's controller (CR 111.6)");
    }

    [Fact]
    public void ResplendentAngel_EndStep_LessThan5Life_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var angel = ResplendentAngelFactory.Create(
            _alice, zones, bus, triggers, effects: null);
        _alice.Zones.Battlefield.AddCard(angel);
        angel.SetZone(ZoneType.Battlefield);

        // Only 4 life gained → intervening-if (CR 603.4) fails.
        bus.Publish(new LifeChangedEvent(_alice, previousLife: 20, newLife: 24));

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Angel")
            .Should().BeEmpty("fewer than 5 life gained → no token (CR 603.4)");
    }

    [Fact]
    public void ResplendentAngel_EndStep_FiresOnEachPlayersEndStep_NotJustController()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var angel = ResplendentAngelFactory.Create(
            _alice, zones, bus, triggers, effects: null);
        _alice.Zones.Battlefield.AddCard(angel);
        angel.SetZone(ZoneType.Battlefield);

        // Alice gained 5 life this turn.
        bus.Publish(new LifeChangedEvent(_alice, previousLife: 20, newLife: 25));

        // The End step belongs to BOB (the opponent), not the Angel's
        // controller — "each end step" must still fire (CR 500.7).
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _bob));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "\"each end step\" fires on every player's end step, not just the controller's");

        triggers.PutPendingTriggersOnStack(_bob);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Angel")
            .Should().HaveCount(1,
                "the token enters under the Angel's controller (Alice) even on Bob's end step");
    }

    [Fact]
    public void ResplendentAngel_LifeGainLatch_ResetsAtTurnStart()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var angel = ResplendentAngelFactory.Create(
            _alice, zones, bus, triggers, effects: null);
        _alice.Zones.Battlefield.AddCard(angel);
        angel.SetZone(ZoneType.Battlefield);

        // 5 life gained, but on a PRIOR turn — the latch must reset on the
        // new turn so this end step does not create a token.
        bus.Publish(new LifeChangedEvent(_alice, previousLife: 20, newLife: 25));
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Angel")
            .Should().BeEmpty(
                "the per-turn life-gained latch resets on TurnStartedEvent");
    }
}

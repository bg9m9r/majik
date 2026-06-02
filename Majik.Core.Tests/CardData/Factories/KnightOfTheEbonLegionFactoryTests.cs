using System.Collections.Generic;
using System.Linq;
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
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Knight of the Ebon Legion (Core Set 2020) — Creature —
/// Vampire Knight {B} 1/2.
///   "{2}{B}: This creature gets +3/+3 and gains deathtouch until end of turn.
///    At the beginning of your end step, if a player lost 4 or more life this
///    turn, put a +1/+1 counter on this creature. (Damage causes loss of
///    life.)"
///
/// Validates:
///   * Card identity (Vampire Knight at {B}, 1/2) + dispatcher entry.
///   * The activated {2}{B}: +3/+3 and gains deathtouch (Layer 7c pump +
///     Layer 6 keyword grant, both EOT — CR 514.2).
///   * CR 603.4 end-step intervening-if: with a player having lost 4+ life
///     this turn the "your end step" trigger puts a +1/+1 counter on the
///     Knight; with <4 it no-ops; and it only fires on the controller's end
///     step.
/// </summary>
[Trait("Color", "B")]
public class KnightOfTheEbonLegionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Func<IReadOnlyList<Player>> AllPlayers()
        => () => new[] { _alice, _bob };

    // ------------------------------------------------------------------
    // Card identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void KnightOfTheEbonLegion_IsVampireKnight_AtCostB_1_2()
    {
        var knight = KnightOfTheEbonLegionFactory.Create(_alice);

        knight.Name.Should().Be("Knight of the Ebon Legion");
        knight.HasType(CardType.Creature).Should().BeTrue();
        knight.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        knight.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        knight.ManaCost.Should().Be("{B}");
        knight.Power.Should().Be(1);
        knight.Toughness.Should().Be(2);
        knight.Owner.Should().BeSameAs(_alice);
        knight.Controller.Should().BeSameAs(_alice);
    }

    // ------------------------------------------------------------------
    // {2}{B}: +3/+3 and gains deathtouch until end of turn
    // ------------------------------------------------------------------

    [Fact]
    public void KnightOfTheEbonLegion_HasPumpActivatedAbility_At2B()
    {
        var knight = KnightOfTheEbonLegionFactory.Create(_alice);

        var ability = knight.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().ContainSingle()
            .Which.Should().BeOfType<Majik.Core.Costs.ManaCostCost>();
    }

    [Fact]
    public void KnightOfTheEbonLegion_PumpAbility_GivesPlus3Plus3AndDeathtouch_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var knight = KnightOfTheEbonLegionFactory.Create(
            _alice, eventBus: null, triggers: null, effects: effects,
            replacements: null, playerResolver: null);

        _alice.Zones.Battlefield.AddCard(knight);
        knight.SetZone(ZoneType.Battlefield);

        // Base 1/2, no deathtouch.
        knight.Power.Should().Be(1);
        knight.Toughness.Should().Be(2);
        CombatAbilities.HasDeathtouch(knight).Should().BeFalse(
            "Knight of the Ebon Legion has no printed deathtouch");

        // Activate {2}{B}: +3/+3 and gains deathtouch until EOT.
        var ability = knight.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        knight.Power.Should().Be(4, "CR 613.1f Layer 7c — +3 power until EOT");
        knight.Toughness.Should().Be(5, "CR 613.1f Layer 7c — +3 toughness until EOT");
        CombatAbilities.HasDeathtouch(knight).Should().BeTrue(
            "CR 613.1c Layer 6 — gains deathtouch until EOT");

        // CR 514.2 — both effects expire in the cleanup step.
        effects.ExpireEndOfTurn();
        knight.Power.Should().Be(1);
        knight.Toughness.Should().Be(2);
        CombatAbilities.HasDeathtouch(knight).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // CR 603.4 — end-step "a player lost 4+ life this turn" counter trigger
    // ------------------------------------------------------------------

    [Fact]
    public void KnightOfTheEbonLegion_EndStep_AfterPlayerLost4Life_PutsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var knight = KnightOfTheEbonLegionFactory.Create(
            _alice, bus, triggers, effects: null, replacements: null,
            playerResolver: AllPlayers());
        _alice.Zones.Battlefield.AddCard(knight);
        knight.SetZone(ZoneType.Battlefield);

        // Bob lost 4 life this turn → satisfies the intervening-if (CR 603.4).
        // "a player" includes any player; Player.LifeLostThisTurn accumulates.
        _bob.LoseLife(4);

        // Alice's (the controller's) end step fires.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "the end-step counter trigger fires when a player lost 4+ life this turn");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        knight.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a +1/+1 counter is put on the Knight (CR 121.1)");
    }

    [Fact]
    public void KnightOfTheEbonLegion_EndStep_LessThan4Life_DoesNotPutCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var knight = KnightOfTheEbonLegionFactory.Create(
            _alice, bus, triggers, effects: null, replacements: null,
            playerResolver: AllPlayers());
        _alice.Zones.Battlefield.AddCard(knight);
        knight.SetZone(ZoneType.Battlefield);

        // Only 3 life lost → intervening-if (CR 603.4) fails.
        _bob.LoseLife(3);

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        knight.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "fewer than 4 life lost → no counter (CR 603.4)");
    }

    [Fact]
    public void KnightOfTheEbonLegion_EndStep_OnlyFiresOnControllersEndStep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var knight = KnightOfTheEbonLegionFactory.Create(
            _alice, bus, triggers, effects: null, replacements: null,
            playerResolver: AllPlayers());
        _alice.Zones.Battlefield.AddCard(knight);
        knight.SetZone(ZoneType.Battlefield);

        _bob.LoseLife(4);

        // The End step belongs to BOB, not the Knight's controller — "your end
        // step" must NOT fire (CR 500).
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _bob));

        triggers.PendingCount.Should().Be(0,
            "\"your end step\" fires only on the controller's end step");
    }
}

using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WitchstalkerFrenzyFactory"/> (Innistrad:
/// Midnight Hunt, {3}{R}).
///
/// Instant. Oracle text:
///   "This spell costs {1} less to cast for each creature that attacked this
///    turn.
///    Witchstalker Frenzy deals 5 damage to target creature."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity ({3}{R} Instant, mana value 4, red) — single _Identity assert.
/// - Cost reduction: {1} less per creature that attacked this turn
///   (bus-driven distinct-attacker tally, any controller).
/// - CR 117.7c floor at the coloured pip ({R} never reduced).
/// - "this turn" window reset on TurnStartedEvent.
/// - Resolve body deals 5 damage to a creature target (no-op on non-creature).
///
/// (Dispatch + well-formedness are covered automatically by
/// CardFactoryContractTests — no dispatch test here.)
/// </summary>
[Trait("Color", "R")]
public class WitchstalkerFrenzyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Attacker(Player controller, string name)
    {
        var c = new Creature(name, "{1}{R}", 2, 2,
            Array.Empty<CardSupertype>(), Array.Empty<CardSubtype>());
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void WitchstalkerFrenzy_Identity_InstantAt3R()
    {
        var card = WitchstalkerFrenzyFactory.Create(_alice);

        card.Name.Should().Be("Witchstalker Frenzy");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{3}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        // The cost reducer is attached even on the shape-only path.
        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void WitchstalkerFrenzy_NoAttackers_PaysFullCost()
    {
        // No creatures attacked this turn → no reduction. {3}{R}: generic 3,
        // red pip 1.
        var card = WitchstalkerFrenzyFactory.Create(_alice, eventBus: new EventBus());

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(3);
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void WitchstalkerFrenzy_TwoAttackers_ReducesTwoGeneric()
    {
        // CR 117.7 — {1} less per creature that attacked this turn. Two
        // distinct attackers (any controller) → {1}{R}: generic 1, red 1.
        var bus = new EventBus();
        var card = WitchstalkerFrenzyFactory.Create(_alice, bus);

        bus.Publish(new CreatureAttacksEvent(Attacker(_alice, "A1"), _bob));
        bus.Publish(new CreatureAttacksEvent(Attacker(_bob, "B1"), _alice));

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(1);
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void WitchstalkerFrenzy_FourAttackers_FloorsAtColouredPip()
    {
        // CR 117.7c — reduction is generic-only; the {R} pip is never reduced.
        // Four attackers → 4 generic reduction; printed generic is 3, so it
        // floors at 0 generic, still pays {R}.
        var bus = new EventBus();
        var card = WitchstalkerFrenzyFactory.Create(_alice, bus);

        bus.Publish(new CreatureAttacksEvent(Attacker(_alice, "A1"), _bob));
        bus.Publish(new CreatureAttacksEvent(Attacker(_alice, "A2"), _bob));
        bus.Publish(new CreatureAttacksEvent(Attacker(_bob, "B1"), _alice));
        bus.Publish(new CreatureAttacksEvent(Attacker(_bob, "B2"), _alice));

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(0);
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void WitchstalkerFrenzy_DistinctAttackersOnly()
    {
        // The same creature declared twice (re-published event) must count
        // once — the tally is by distinct permanent (HashSet).
        var bus = new EventBus();
        var card = WitchstalkerFrenzyFactory.Create(_alice, bus);
        var solo = Attacker(_alice, "Solo");

        bus.Publish(new CreatureAttacksEvent(solo, _bob));
        bus.Publish(new CreatureAttacksEvent(solo, _bob));

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(2, "one distinct attacker → {1} reduction");
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void WitchstalkerFrenzy_TurnStarted_ResetsTheWindow()
    {
        // "this turn" — CR 500.4 / 514. A new turn clears the tally.
        var bus = new EventBus();
        var card = WitchstalkerFrenzyFactory.Create(_alice, bus);

        bus.Publish(new CreatureAttacksEvent(Attacker(_alice, "A1"), _bob));
        bus.Publish(new CreatureAttacksEvent(Attacker(_bob, "B1"), _alice));
        bus.Publish(new TurnStartedEvent(_bob, turnNumber: 2));

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(3, "the new turn reset the attacker tally");
        effective.Red.Should().Be(1);
    }

    [Fact]
    public void WitchstalkerFrenzy_SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = WitchstalkerFrenzyFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.Description.Should().Be("target creature");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void WitchstalkerFrenzy_Resolve_DealsFiveDamageToCreature()
    {
        var bear = Attacker(_bob, "Bear Cub");
        _bob.Zones.Battlefield.AddCard(bear);

        var def = WitchstalkerFrenzyFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { bear } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Damage.Should().Be(5, "Witchstalker Frenzy deals 5 damage to target creature");
    }

    [Fact]
    public void WitchstalkerFrenzy_Resolve_NonCreatureTarget_IsNoOp()
    {
        // CR 608.2b — non-creature resolved target does nothing.
        var def = WitchstalkerFrenzyFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        var act = () => { foreach (var effect in def.EffectFactory(chosen)) effect.Execute(); };

        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(20, "Witchstalker Frenzy targets creatures only");
    }
}

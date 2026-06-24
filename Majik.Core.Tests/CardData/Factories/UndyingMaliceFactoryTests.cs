using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Tests for Undying Malice (Modern Horizons 3, {B}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Until end of turn, target creature gains 'When this creature dies,
///    return it to the battlefield tapped under its owner's control with a
///    +1/+1 counter on it.'"
///
/// Unlike the intrinsic Undying keyword (CR 702.93b, Young Wolf), the granted
/// ability:
///   - returns the creature TAPPED;
///   - has NO "if it had no +1/+1 counters" intervening-if — it returns
///     unconditionally on the first death while the grant is live.
///
/// Coverage (card-unique behaviour only — CardFactoryContractTests covers
/// dispatch + well-formedness for every implemented card automatically):
/// - Identity: Instant, black, {B}.
/// - <see cref="UndyingMaliceFactory.BuildDefinition"/> shape: single 1..1
///   "target creature" request, no X.
/// - Resolve grants the death-trigger; the targeted creature that dies while
///   the grant is live returns to the battlefield TAPPED with one +1/+1
///   counter (CR 613.1f Layer-6 grant + the granted death trigger).
/// - The grant expires at end of turn (CR 514.2) — a death after cleanup does
///   NOT return the creature.
/// </summary>
[Trait("Color", "B")]
public class UndyingMaliceFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    private Creature BuildCreature(string name, string manaCost, Player owner)
    {
        var c = new Creature(name, manaCost, 2, 2)
        {
            Owner = owner,
            Controller = owner,
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // ── Identity ──────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Black_AtCostB()
    {
        var card = UndyingMaliceFactory.Create(_alice);

        card.Name.Should().Be("Undying Malice");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition shape ─────────────────────────────────────────────

    [Fact]
    public void BuildDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = UndyingMaliceFactory.BuildDefinition();

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ── Resolve: grant the death-trigger ──────────────────────────────────

    /// <summary>
    /// CR 613.1f — the targeted creature gains the death-trigger until end of
    /// turn. When it dies (Battlefield → Graveyard) the granted trigger fires
    /// and returns it to the battlefield TAPPED with one +1/+1 counter.
    /// </summary>
    [Fact]
    public void Resolve_TargetedCreatureThatDies_ReturnsTappedWithPlusOneCounter()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var bear = BuildCreature("Bear", "{1}{G}", _alice);

        // Grant the Undying-Malice death-trigger to the bear.
        UndyingMaliceFactory.Resolve(bear);

        // Death via ZoneService (fires CardMovedEvent; TriggerManager auto-binds
        // the granted trigger off the move).
        zones.MoveCardTo(bear, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1,
            "the granted death-trigger must queue on Battlefield → Graveyard");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Back on the battlefield, tapped, with one +1/+1 counter.
        bear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
        bear.IsTapped.Should().BeTrue("Undying Malice returns the creature tapped");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    /// <summary>
    /// CR 514.2 — the grant expires at end of turn. A death AFTER cleanup does
    /// not return the creature (the granted trigger is gone).
    /// </summary>
    [Fact]
    public void Resolve_GrantExpiresAtEndOfTurn_DeathAfterCleanupDoesNotReturn()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var bear = BuildCreature("Bear", "{1}{G}", _alice);
        var svc = bear.ActiveEffects!;

        UndyingMaliceFactory.Resolve(bear);

        // CR 514.2 — cleanup expires the grant; re-sync the layer pass so the
        // granted trigger is removed from Abilities.
        svc.ExpireEndOfTurn();
        svc.Compute(bear);

        bear.Abilities.OfType<ITriggeredAbility>().Should().BeEmpty(
            "the granted death-trigger is revoked at end of turn (CR 514.2)");

        zones.MoveCardTo(bear, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(0,
            "no death-trigger should fire after the grant expires");
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }
}

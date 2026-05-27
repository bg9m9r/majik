using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Fury (Modern Horizons 2, {3}{R}). Mirrors
/// <see cref="SolitudeFactoryTests"/>: both cast paths (normal + evoke)
/// plus shape + dispatch coverage. The ETB damage trigger reads
/// X = cards in controller's hand at resolution and routes per the
/// caller-supplied distribute Func (deterministic in tests).
/// </summary>
public class FuryFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FuryFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape + dispatch ──────────────────────────────────────────────────────

    [Fact]
    public void Create_HasCorrectShape()
    {
        var fury = FuryFactory.Create(_alice);

        fury.Name.Should().Be("Fury");
        fury.BasePower.Should().Be(3);
        fury.BaseToughness.Should().Be(3);
        fury.HasType(CardType.Creature).Should().BeTrue();
        fury.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        fury.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();

        var keywords = fury.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain(new[] { "Double strike", "Evoke" });

        // Two triggered abilities: ETB damage + Evoke sacrifice.
        fury.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchesFury()
    {
        var card = NamedCardFactory.Create("Fury", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fury");
        card.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        card.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();
    }

    // ── ETB damage trigger ────────────────────────────────────────────────────

    [Fact]
    public async Task CastForEvoke_FiveCardHand_DealsFiveDamageSplitPerDistribute()
    {
        // 5 cards remain in Alice's hand AFTER Fury leaves (we'll seed 6 then
        // pitch one for evoke = 4 left after announcement, +1 because Fury
        // itself also leaves hand on cast → check the resolution-time read).
        // Simpler: seed enough so that at resolution time the hand size is
        // exactly 5, and assert the deterministic split (3 to grizzly, 2 to bear).
        var fury = FuryInHand(_alice);
        var pitch = new Creature("Mountain Lion", "R", 2, 1) { Owner = _alice };
        pitch.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch);

        // Top up hand so at resolution (Fury + pitch already exiled / on stack)
        // the controller hand size = 5.
        for (var i = 0; i < 5; i++)
        {
            var filler = new Creature($"Filler{i}", "R", 1, 1) { Owner = _alice };
            filler.SetZone(ZoneType.Hand);
            _alice.Zones.Hand.AddCard(filler);
        }

        // Two Bob creatures to take damage.
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var bear = new Creature("Runeclaw Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        // Deterministic distribution: 3 to grizzly, 2 to bear (verifies X=5).
        IReadOnlyDictionary<Permanent, int> Distribute(Player controller, int x)
        {
            x.Should().Be(5);
            return new Dictionary<Permanent, int>
            {
                [grizzly] = 3,
                [bear]    = 2,
            };
        }

        // Recreate Fury with the distribute Func (Create takes it as a ctor arg).
        _alice.Zones.Hand.RemoveCard(fury);
        fury = FuryFactory.Create(_alice, Distribute);
        fury.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fury);

        // Cast via Evoke (pitch the red Mountain Lion; no mana paid).
        var evokeCost = new EvokeAlternativeCost(
            ManaCost.Zero, ManaColor.Red, pitch);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fury,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        _resolver.ResolveTop(_stack);

        fury.Zone.Should().Be(ZoneType.Battlefield);
        fury.EvokeWasPaid.Should().BeTrue();
        pitch.Zone.Should().Be(ZoneType.Exile);

        // Two triggers fired on ETB: damage-distribute + evoke sacrifice.
        _triggers.PendingCount.Should().Be(2);

        // Wire chosen targets on the ETB damage trigger.
        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly, bear },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty) _resolver.ResolveTop(_stack);

        grizzly.Damage.Should().Be(3);
        bear.Damage.Should().Be(2);
        // Evoke sacrifice trigger fired → Fury in graveyard.
        fury.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task CastNormal_KeepsFuryOnBattlefield_AndDealsDamage()
    {
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        IReadOnlyDictionary<Permanent, int> Distribute(Player _, int x) =>
            new Dictionary<Permanent, int> { [grizzly] = x };

        var fury = FuryFactory.Create(_alice, Distribute);
        fury.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fury);

        // Seed two extra cards so resolution-time hand size = 2.
        for (var i = 0; i < 2; i++)
        {
            var filler = new Creature($"Filler{i}", "R", 1, 1) { Owner = _alice };
            filler.SetZone(ZoneType.Hand);
            _alice.Zones.Hand.AddCard(filler);
        }

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fury,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        fury.Zone.Should().Be(ZoneType.Battlefield);
        fury.EvokeWasPaid.Should().BeFalse();

        // Only ETB damage trigger pending (evoke sac dropped by intervening-if).
        _triggers.PendingCount.Should().Be(1);

        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty) _resolver.ResolveTop(_stack);

        grizzly.Damage.Should().Be(2);
        fury.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task CastForEvoke_EmptyHand_NoDamage_StillSacrificed()
    {
        // After paying evoke (which exiles the pitch card AND moves Fury to the
        // stack), Alice's hand is empty at ETB resolution → X = 0, the damage
        // trigger no-ops, but the evoke sacrifice still fires.
        var fury = FuryInHand(_alice);
        var pitch = new Creature("Mountain Lion", "R", 2, 1) { Owner = _alice };
        pitch.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch);

        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var evokeCost = new EvokeAlternativeCost(
            ManaCost.Zero, ManaColor.Red, pitch);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fury,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        _resolver.ResolveTop(_stack);

        fury.EvokeWasPaid.Should().BeTrue();
        _triggers.PendingCount.Should().Be(2);

        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty) _resolver.ResolveTop(_stack);

        // Hand was empty at resolution → no damage dealt.
        grizzly.Damage.Should().Be(0);
        // Evoke sacrifice still resolved.
        fury.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DefaultDistribution_DealsAllXToFirstTarget()
    {
        // Documents the v1 degradation: when no distribute Func is supplied,
        // all X damage is sent to the first chosen target.
        var fury = FuryFactory.Create(_alice);
        fury.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fury);

        // Seed 3 hand cards → X = 3.
        for (var i = 0; i < 3; i++)
        {
            var filler = new Creature($"F{i}", "R", 1, 1) { Owner = _alice };
            filler.SetZone(ZoneType.Hand);
            _alice.Zones.Hand.AddCard(filler);
        }

        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var bear = new Creature("Runeclaw Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly, bear },
        });

        damageTrigger.Resolve();

        // All 3 damage to the first target.
        grizzly.Damage.Should().Be(3);
        bear.Damage.Should().Be(0);
    }

    [Fact]
    public void EtbDamage_DealsToPlaneswalkerViaRemoveLoyalty()
    {
        // The damage trigger routes to Planeswalker.RemoveLoyalty for
        // planeswalker targets (CR 120.3 — damage to a planeswalker is
        // dealt to a planeswalker on the battlefield, removing that many
        // loyalty counters).
        var pw = new Planeswalker("Test Walker", "{2}{B}", startingLoyalty: 5)
        { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        IReadOnlyDictionary<Permanent, int> Distribute(Player _, int x) =>
            new Dictionary<Permanent, int> { [pw] = x };

        var fury = FuryFactory.Create(_alice, Distribute);
        fury.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fury);

        // Seed hand size = 4.
        for (var i = 0; i < 4; i++)
        {
            var filler = new Creature($"F{i}", "R", 1, 1) { Owner = _alice };
            filler.SetZone(ZoneType.Hand);
            _alice.Zones.Hand.AddCard(filler);
        }

        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        damageTrigger.Resolve();

        pw.Loyalty.Should().Be(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature FuryInHand(Player owner)
    {
        var f = FuryFactory.Create(owner);
        f.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(f);
        return f;
    }
}

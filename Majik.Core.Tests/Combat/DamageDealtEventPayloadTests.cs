using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Covers the per-source/per-target payload portal needs to animate
/// damage being dealt — one event per source→target pair, with
/// stable Guid identifiers, target-is-player flag, amount, and the
/// damage-type discriminator (CR 119, CR 510).
/// </summary>
public class DamageDealtEventPayloadTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DamageDealtEventPayloadTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task CombatDamage_AttackerVsPlayer_EmitsEventWithCreatureSourceAndPlayerTarget()
    {
        var attacker = NewCreature("Bear", 2, 2, _alice);
        var events = new List<DamageDealtEvent>();
        // Combat damage publishes the concrete CombatDamageDealtEvent
        // subclass; SubscribeAll is the production wire-payload bridge
        // path (see GameFacade.BridgeEvent → EventPayloadBuilder).
        _bus.SubscribeAll(e => { if (e is DamageDealtEvent d) events.Add(d); });

        await RunCombat(attacker, blocker: null);

        events.Should().ContainSingle();
        var e = events[0];
        e.Amount.Should().Be(2);
        e.SourceInstanceId.Should().Be(attacker.InstanceId);
        e.TargetInstanceId.Should().Be(_bob.Id);
        e.TargetIsPlayer.Should().BeTrue();
        e.DamageType.Should().Be(DamageType.Combat);
    }

    [Fact]
    public async Task CombatDamage_AttackerVsBlocker_EmitsTwoEventsOnePerSourceTargetPair()
    {
        var attacker = NewCreature("AttackerBear", 2, 2, _alice);
        var blocker = NewCreature("BlockerBear", 2, 2, _bob);
        var events = new List<DamageDealtEvent>();
        // Combat damage publishes the concrete CombatDamageDealtEvent
        // subclass; SubscribeAll is the production wire-payload bridge
        // path (see GameFacade.BridgeEvent → EventPayloadBuilder).
        _bus.SubscribeAll(e => { if (e is DamageDealtEvent d) events.Add(d); });

        await RunCombat(attacker, blocker);

        // attacker → blocker AND blocker → attacker = two damage events.
        events.Should().HaveCount(2);
        events.Should().Contain(e =>
            e.SourceInstanceId == attacker.InstanceId &&
            e.TargetInstanceId == blocker.InstanceId &&
            e.Amount == 2 &&
            !e.TargetIsPlayer &&
            e.DamageType == DamageType.Combat);
        events.Should().Contain(e =>
            e.SourceInstanceId == blocker.InstanceId &&
            e.TargetInstanceId == attacker.InstanceId &&
            e.Amount == 2 &&
            !e.TargetIsPlayer &&
            e.DamageType == DamageType.Combat);
    }

    [Fact]
    public async Task CombatDamage_NonLethal_StillEmitsEvent()
    {
        // 1/4 attacker vs no blocker: 1 damage, player isn't killed but
        // event still fires (death is a separate SBA, CR 704.5g).
        var attacker = NewCreature("PingBear", 1, 4, _alice);
        var events = new List<DamageDealtEvent>();
        // Combat damage publishes the concrete CombatDamageDealtEvent
        // subclass; SubscribeAll is the production wire-payload bridge
        // path (see GameFacade.BridgeEvent → EventPayloadBuilder).
        _bus.SubscribeAll(e => { if (e is DamageDealtEvent d) events.Add(d); });

        await RunCombat(attacker, blocker: null);

        events.Should().ContainSingle()
            .Which.Amount.Should().Be(1);
        _bob.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void CombatDamageDealtEvent_ReachesGlobalDamageSubscribers_ViaInheritance()
    {
        // The wire-payload bridge (GameFacade.BridgeEvent) routes every
        // GameEvent through SubscribeAll, so the EventPayloadBuilder's
        // pattern match on `DamageDealtEvent` catches both Combat- and
        // generic damage events. Inheritance is what makes the single
        // wire shape possible — lock it down with an explicit test.
        var bus = new EventBus();
        var seen = new List<DamageDealtEvent>();
        bus.SubscribeAll(e => { if (e is DamageDealtEvent d) seen.Add(d); });

        var src = new Creature("S", "1", 2, 2);
        var tgt = new Creature("T", "1", 2, 2);
        bus.Publish(new CombatDamageDealtEvent(src, tgt, 2));

        seen.Should().ContainSingle();
        seen[0].DamageType.Should().Be(DamageType.Combat);
        seen[0].SourceInstanceId.Should().Be(src.InstanceId);
        seen[0].TargetInstanceId.Should().Be(tgt.InstanceId);
        seen[0].TargetIsPlayer.Should().BeFalse();
    }

    // ---- Helpers (mirror CombatKeywordsTests.RunCombat) ----

    private async Task RunCombat(Creature attacker, Creature? blocker)
    {
        attacker.SetOwner(_alice); attacker.SetController(_alice);
        attacker.SetZone(ZoneType.Battlefield);
        attacker.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(attacker);

        if (blocker != null)
        {
            blocker.SetOwner(_bob); blocker.SetController(_bob);
            blocker.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(blocker);
        }

        var flow = new CombatFlow(_bus, _sba);
        var atkAgent = new ScriptedAgent();
        atkAgent.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(attacker, _bob),
        }));
        var blkAgent = new ScriptedAgent();
        blkAgent.QueueBlockers(blocker == null
            ? BlockPlan.None
            : new BlockPlan(new[]
            {
                new Majik.Core.Players.Agents.BlockerDeclaration(blocker, attacker),
            }));

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(
            _alice, _bob, atkAgent, blkAgent,
            new[] { attacker },
            blocker == null ? Array.Empty<Creature>() : new[] { blocker },
            ctx);
    }

    private static Creature NewCreature(string name, int p, int t, Player owner, params string[] keywords)
    {
        var c = new Creature(name, "1", p, t) { Owner = owner, Controller = owner };
        foreach (var kw in keywords)
        {
            c.AddAbility(new KeywordAbility(kw, c, owner));
        }
        return c;
    }
}

using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Combat;

public class CombatFlowTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CombatFlowTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task NoAttackers_DefenderTakesNoDamage()
    {
        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(CombatPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: new DeterministicBotAgent(),
            attackers: Array.Empty<Creature>(), blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task OneAttackerNoBlockers_DefenderLosesPowerLife_AttackerTapped()
    {
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.HasSummoningSickness = false;
        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new Majik.Core.Players.Agents.AttackerDeclaration(bear, _bob) }));
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear }, blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(18);
        bear.IsTapped.Should().BeTrue();
    }

    [Fact]
    public async Task TwoTwoVsTwoTwo_BlockedAttacker_BothCreaturesTakeLethalDamage()
    {
        var atk = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        var blk = (Creature)NamedCardFactory.Create("Grizzly Bears", _bob);
        atk.SetOwner(_alice); atk.SetController(_alice); atk.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(atk);
        blk.SetOwner(_bob); blk.SetController(_bob); blk.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(blk);
        atk.HasSummoningSickness = false;
        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new Majik.Core.Players.Agents.AttackerDeclaration(atk, _bob) }));
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(new BlockPlan(new[] { new Majik.Core.Players.Agents.BlockerDeclaration(blk, atk) }));

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { atk }, blockers: new[] { blk },
            ctx: NewContext());

        // Both 2/2 → both take 2 damage → both die via SBA → graveyard.
        _bob.LifeTotal.Should().Be(20);
        atk.Zone.Should().Be(ZoneType.Graveyard);
        blk.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task CombatDamageIntent_IsStampedWithIsCombatDamage()
    {
        // CR 510.1c — combat damage must be discriminable from non-combat
        // damage at the replacement layer. CombatFlow stamps
        // DamageIntent.IsCombatDamage = true on every intent it pumps
        // through the ReplacementBus (covers all three Apply{ToCreature
        // |ToPlaneswalker|ToPlayer} routes).
        var replacements = new ReplacementBus();
        var captured = new List<DamageIntent>();
        replacements.Register<DamageIntent>(new LambdaReplacement<DamageIntent>(
            (i, _) => { captured.Add(i); return false; },
            (i, _) => i));

        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.HasSummoningSickness = false;
        var flow = new CombatFlow(_bus, _sba, replacements);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] {
            new Majik.Core.Players.Agents.AttackerDeclaration(bear, _bob) }));
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear }, blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        captured.Should().NotBeEmpty("CombatFlow pushed the player-damage intent through the bus");
        captured.Should().OnlyContain(i => i.IsCombatDamage,
            "every intent CombatFlow emits is combat damage (CR 510.1)");
    }

    [Fact]
    public async Task RunCombatFromBlocks_DealsDamage_WithoutRedeclaringAttack()
    {
        // Combat entered with a pre-built attack plan (declaration happened
        // "live": attacker already tapped) must run blocks + damage WITHOUT
        // re-declaring or re-firing CR 508.1f attack events.
        var atk = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        atk.SetOwner(_alice); atk.SetController(_alice); atk.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(atk);
        atk.HasSummoningSickness = false;
        atk.Tap(); // declaration already happened live

        var blk = (Creature)NamedCardFactory.Create("Grizzly Bears", _bob);
        blk.SetOwner(_bob); blk.SetController(_bob); blk.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(blk);

        var attacksFired = 0;
        _bus.Subscribe<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(_ => attacksFired++);

        var flow = new CombatFlow(_bus, _sba);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);
        var attackPlan = new CombatPlan(new[] {
            new Majik.Core.Players.Agents.AttackerDeclaration(atk, _bob) });

        await flow.RunCombatFromBlocksAsync(
            _alice, _bob, bobAgent,
            attackPlan, new[] { blk }, NewContext());

        attacksFired.Should().Be(0, "the declaration half already ran live");
        _bob.LifeTotal.Should().BeLessThan(20, "the damage half ran (no-block agent)");
    }

    [Fact]
    public async Task MustAttackCreature_OmittedByAgent_IsForcedIntoCombat()
    {
        // CR 508.1a / 702.43 — a creature with "attacks each combat if able"
        // must be declared as an attacker if it CAN legally attack. The
        // attacking player's agent declared NO attackers; the engine must
        // force the must-attack creature in so it taps, fires its "attacks"
        // trigger, and deals combat damage.
        var crusher = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        crusher.SetOwner(_alice); crusher.SetController(_alice); crusher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(crusher);
        crusher.HasSummoningSickness = false;
        crusher.AddAbility(new Majik.Core.Abilities.KeywordAbility(
            "AttacksEachCombat", crusher, _alice));

        var attacksFired = 0;
        _bus.Subscribe<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(_ => attacksFired++);

        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(CombatPlan.None); // agent declines to attack
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { crusher }, blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        attacksFired.Should().Be(1, "the must-attack creature was forced into combat");
        crusher.IsTapped.Should().BeTrue("a forced attacker still taps (no vigilance)");
        _bob.LifeTotal.Should().Be(18, "the forced attacker dealt its 2 combat damage");
    }

    [Fact]
    public async Task MustAttackCreature_AlreadyDeclared_IsNotDuplicated()
    {
        // CR 508.1a — the must-attack creature the agent ALREADY declared is
        // not force-added a second time (no double-tap / double-trigger).
        var crusher = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        crusher.SetOwner(_alice); crusher.SetController(_alice); crusher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(crusher);
        crusher.HasSummoningSickness = false;
        crusher.AddAbility(new Majik.Core.Abilities.KeywordAbility(
            "AttacksEachCombat", crusher, _alice));

        var attacksFired = 0;
        _bus.Subscribe<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(_ => attacksFired++);

        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] {
            new Majik.Core.Players.Agents.AttackerDeclaration(crusher, _bob) }));
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { crusher }, blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        attacksFired.Should().Be(1, "the already-declared must-attack creature attacks exactly once");
        _bob.LifeTotal.Should().Be(18);
    }

    [Fact]
    public async Task MustAttackCreature_NotEligible_IsNotForced()
    {
        // CR 508.1a — "if able". A must-attack creature that CANNOT legally
        // attack (here: not in the eligible-attacker list — e.g. tapped /
        // summoning-sick) is NOT forced into combat.
        var crusher = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        crusher.SetOwner(_alice); crusher.SetController(_alice); crusher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(crusher);
        crusher.HasSummoningSickness = false;
        crusher.AddAbility(new Majik.Core.Abilities.KeywordAbility(
            "AttacksEachCombat", crusher, _alice));

        var attacksFired = 0;
        _bus.Subscribe<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(_ => attacksFired++);

        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(CombatPlan.None);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        // Eligible list is EMPTY → crusher cannot legally attack this combat.
        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: Array.Empty<Creature>(), blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        attacksFired.Should().Be(0, "a must-attack creature that can't attack is not forced");
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task AttacksThisCombatMarker_OmittedByAgent_IsForcedIntoCombat()
    {
        // CR 508.1a — "attacks this combat if able" (the one-combat variant the
        // Legion Warboss Goblin token gains) imposes the same must-attack
        // declaration obligation as the permanent "attacks each combat" static.
        var token = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        token.SetOwner(_alice); token.SetController(_alice); token.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(token);
        token.HasSummoningSickness = false;
        token.AddAbility(new Majik.Core.Abilities.KeywordAbility(
            "AttacksThisCombat", token, _alice));

        var attacksFired = 0;
        _bus.Subscribe<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(_ => attacksFired++);

        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(CombatPlan.None);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { token }, blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        attacksFired.Should().Be(1, "the 'attacks this combat if able' token was forced into combat");
        _bob.LifeTotal.Should().Be(18);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());
}

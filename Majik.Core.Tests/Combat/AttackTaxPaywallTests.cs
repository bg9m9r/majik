using System.Linq;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using AttackerDeclaration = Majik.Core.Players.Agents.AttackerDeclaration;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// CR 508.1g — declare-attackers "unless its controller pays" tax (Ghostly
/// Prison / Propaganda / Sphere of Safety). The attack-tax paywall is enforced
/// in <see cref="CombatFlow"/> right after the agent declares attackers: each
/// declared attacker on a protected defender must have its per-attacker cost
/// paid or it is removed from the attack (the declaration is illegal — CR
/// 508.1g — and the engine "un-declares" it).
/// </summary>
public class AttackTaxPaywallTests
{
    private readonly Majik.Core.Events.EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AttackTaxPaywallTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

    private Creature MakeBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        bear.HasSummoningSickness = false;
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }

    private static ManaCost Generic(int n) => ManaCost.Zero.AddGenericCost(n);

    [Fact]
    public async Task GhostlyPrison_PaidTax_AttackerHitsForDamage()
    {
        var bear = MakeBear(_alice);
        _alice.AddManaToPool(Generic(2)); // {2} to pay the tax.

        var registry = new AttackRestrictionRegistry();
        registry.Register(PayPerAttackerRestriction.FlatMana(_bob, Generic(2)));

        var flow = new CombatFlow(_bus, _sba, attackRestrictions: registry);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new AttackerDeclaration(bear, _bob) }));
        aliceAgent.QueueYesNo(true); // pay the {2} tax.
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear }, blockers: System.Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(18, "the tax was paid so the attacker connected");
        _alice.ManaPool.Total.Should().Be(0, "the {2} tax was deducted from the pool");
        bear.IsTapped.Should().BeTrue("a paid attacker is declared normally and taps");
    }

    [Fact]
    public async Task GhostlyPrison_UnpaidTax_AttackerRemoved_NoDamage()
    {
        var bear = MakeBear(_alice);
        // No mana added — Alice cannot pay.

        var registry = new AttackRestrictionRegistry();
        registry.Register(PayPerAttackerRestriction.FlatMana(_bob, Generic(2)));

        var flow = new CombatFlow(_bus, _sba, attackRestrictions: registry);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new AttackerDeclaration(bear, _bob) }));
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear }, blockers: System.Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(20, "the unpaid attacker was removed from combat");
        bear.IsTapped.Should().BeFalse("a removed attacker is never declared, so it does not tap (CR 508.1g)");
    }

    [Fact]
    public async Task GhostlyPrison_PerAttacker_ChargesEachSeparately()
    {
        var bear1 = MakeBear(_alice);
        var bear2 = MakeBear(_alice);
        _alice.AddManaToPool(Generic(4));

        var registry = new AttackRestrictionRegistry();
        registry.Register(PayPerAttackerRestriction.FlatMana(_bob, Generic(2)));

        var flow = new CombatFlow(_bus, _sba, attackRestrictions: registry);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[]
        {
            new AttackerDeclaration(bear1, _bob),
            new AttackerDeclaration(bear2, _bob),
        }));
        aliceAgent.QueueYesNo(true);
        aliceAgent.QueueYesNo(true);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear1, bear2 }, blockers: System.Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(16, "both attackers paid {2} each and connected for 2 each");
        _alice.ManaPool.Total.Should().Be(0);
    }

    [Fact]
    public async Task GhostlyPrison_DeclinedTax_AttackerRemovedEvenThoughAffordable()
    {
        var bear = MakeBear(_alice);
        _alice.AddManaToPool(Generic(2)); // Alice COULD pay but declines.

        var registry = new AttackRestrictionRegistry();
        registry.Register(PayPerAttackerRestriction.FlatMana(_bob, Generic(2)));

        var flow = new CombatFlow(_bus, _sba, attackRestrictions: registry);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new AttackerDeclaration(bear, _bob) }));
        aliceAgent.QueueYesNo(false); // decline.
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear }, blockers: System.Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(20, "declining the tax un-declares the attacker (CR 508.1g)");
        _alice.ManaPool.Total.Should().Be(2, "declined tax leaves the mana unspent");
    }

    [Fact]
    public async Task SphereOfSafety_DynamicCost_ScalesWithEnchantments()
    {
        // Sphere of Safety: pay {X} per attacker where X = enchantments Bob
        // controls. Bob controls 3 enchantments → {3} per attacker.
        var bear = MakeBear(_alice);
        _alice.AddManaToPool(Generic(3));

        for (var i = 0; i < 3; i++)
        {
            var ench = new Enchantment($"Aura{i}", "W")
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
            _bob.Zones.Battlefield.AddCard(ench);
        }

        var registry = new AttackRestrictionRegistry();
        registry.Register(PayPerAttackerRestriction.Dynamic(
            _bob,
            costPerAttacker: () => Generic(
                _bob.Zones.Battlefield.GetCards().OfType<Enchantment>().Count()),
            protectsPlaneswalkers: true));

        var flow = new CombatFlow(_bus, _sba, attackRestrictions: registry);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new AttackerDeclaration(bear, _bob) }));
        aliceAgent.QueueYesNo(true);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear }, blockers: System.Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(18, "the {3} tax was paid so the attacker connected");
        _alice.ManaPool.Total.Should().Be(0, "{3} = 3 enchantments was deducted");
    }

    [Fact]
    public async Task TaxMustBePaidEachCombat_NotJustOnce()
    {
        // CR 508.1g — the tax is per declare-attackers. A creature paid-for in
        // one combat must pay again in the next; a stale paid-mark must NOT
        // unlock a later free attack.
        var bear = MakeBear(_alice);
        _alice.AddManaToPool(Generic(2));

        var registry = new AttackRestrictionRegistry();
        registry.Register(PayPerAttackerRestriction.FlatMana(_bob, Generic(2)));
        var flow = new CombatFlow(_bus, _sba, attackRestrictions: registry);

        // Combat 1 — pay and connect.
        var a1 = new ScriptedAgent();
        a1.QueueAttackers(new CombatPlan(new[] { new AttackerDeclaration(bear, _bob) }));
        a1.QueueYesNo(true);
        var b1 = new ScriptedAgent();
        b1.QueueBlockers(BlockPlan.None);
        await flow.RunCombatAsync(_alice, _bob, a1, b1,
            new[] { bear }, System.Array.Empty<Creature>(), NewContext());
        _bob.LifeTotal.Should().Be(18);

        // Untap for combat 2, no mana this time → must pay again, can't.
        if (bear.IsTapped) bear.Untap();
        var a2 = new ScriptedAgent();
        a2.QueueAttackers(new CombatPlan(new[] { new AttackerDeclaration(bear, _bob) }));
        var b2 = new ScriptedAgent();
        b2.QueueBlockers(BlockPlan.None);
        await flow.RunCombatAsync(_alice, _bob, a2, b2,
            new[] { bear }, System.Array.Empty<Creature>(), NewContext());

        _bob.LifeTotal.Should().Be(18, "the prior paid-mark must not unlock a free second attack");
    }

    [Fact]
    public async Task NoRestriction_AttacksFreely()
    {
        var bear = MakeBear(_alice);
        var registry = new AttackRestrictionRegistry(); // empty.

        var flow = new CombatFlow(_bus, _sba, attackRestrictions: registry);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new AttackerDeclaration(bear, _bob) }));
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear }, blockers: System.Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(18, "no paywall → no tax, the attacker connects");
    }
}

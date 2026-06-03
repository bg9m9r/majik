using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// CR 702.90c — Infect. A source with infect deals its damage to a CREATURE
/// as that many -1/-1 counters (identical to wither, CR 702.90b) and to a
/// PLAYER as that many poison counters instead of normal life loss. The form
/// applies to ALL damage — combat AND noncombat (ability) — and is consistent
/// across both. The 10-poison loss is a state-based action (CR 704.5c).
/// </summary>
public class InfectCombatTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public InfectCombatTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task Infect_CombatDamageToPlayer_GivesPoisonNotLifeLoss()
    {
        // CR 702.90c — a 1/1 infect creature dealing combat damage to a player
        // gives one poison counter, NOT one life loss.
        var attacker = NewCreature("Glistener Elf", 1, 1, _alice, "Infect");

        await RunCombat(attacker, blocker: null);

        _bob.LifeTotal.Should().Be(20, "infect damage to a player is poison, not life loss (CR 702.90c)");
        _bob.PoisonCounters.Should().Be(1);
    }

    [Fact]
    public async Task Infect_TenPoison_PlayerLosesViaSba()
    {
        // CR 704.5c — a player with ten or more poison counters loses the game.
        _bob.AddPoisonCounters(9);
        var attacker = NewCreature("Glistener Elf", 1, 1, _alice, "Infect");

        await RunCombat(attacker, blocker: null);

        _bob.PoisonCounters.Should().Be(10);
        _sba.CheckStateBasedActions(new[] { _alice, _bob }, System.Array.Empty<Majik.Core.Cards.ICard>());
        _bob.HasLost.Should().BeTrue("ten poison counters is a loss SBA (CR 704.5c)");
    }

    [Fact]
    public async Task Infect_CombatDamageToCreature_AppliesMinusCounters()
    {
        // CR 702.90c — infect's creature-damage form is -1/-1 counters, same
        // as wither. Already handled by the shared helper; assert it holds.
        var attacker = NewCreature("Phyrexian Crusader", 2, 2, _alice, "Infect");
        var blocker = NewCreature("Big Bear", 4, 4, _bob);

        await RunCombat(attacker, blocker);

        blocker.Zone.Should().Be(ZoneType.Battlefield);
        blocker.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(2);
        blocker.Damage.Should().Be(0, "infect damage to a creature is -1/-1 counters, not marked damage");
        blocker.Toughness.Should().Be(2);
    }

    [Fact]
    public async Task Infect_WithLifelink_ControllerGainsLifeEvenThoughPoison()
    {
        // CR 702.15g / 119.3 — lifelink keys off damage dealt, not life loss.
        // An infect + lifelink source's controller still gains that much life
        // even though the player took poison rather than losing life.
        var attacker = NewCreature("Lifelinked Infector", 3, 3, _alice, "Infect", "Lifelink");

        await RunCombat(attacker, blocker: null);

        _bob.PoisonCounters.Should().Be(3);
        _bob.LifeTotal.Should().Be(20);
        _alice.LifeTotal.Should().Be(23, "lifelink gains life equal to damage dealt (CR 702.15g)");
    }

    [Fact]
    public void Infect_NoncombatAbilityDamageToPlayer_GivesPoison()
    {
        // CR 702.90c — infect changes the form of ALL damage, not just combat.
        // A noncombat "deals N damage to target player" from an infect source
        // is dealt as poison counters.
        var svc = new ContinuousEffectsService();
        var source = new Creature("Infect Pinger", "1", 1, 1)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        source.AddAbility(new KeywordAbility("Infect", source, _alice));

        Fx.DealDamageAny(_bob, 3, source);

        _bob.PoisonCounters.Should().Be(3);
        _bob.LifeTotal.Should().Be(20, "infect noncombat damage to a player is poison (CR 702.90c)");
    }

    [Fact]
    public void Infect_NoncombatAbilityDamageToPlayer_NonInfectSourceIsNormalLifeLoss()
    {
        // Control: a non-infect source's noncombat damage to a player is normal
        // life loss, even through the source-aware overload.
        var source = new Creature("Plain Pinger", "1", 1, 1)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };

        Fx.DealDamageAny(_bob, 3, source);

        _bob.PoisonCounters.Should().Be(0);
        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public async Task GlistenerElf_FactoryCreatureDealsPoisonInCombat()
    {
        // End-to-end: the real Glistener Elf factory creature, run through
        // combat, gives poison to the defending player.
        var elf = GlistenerElfFactory.Create(_alice);

        await RunCombat(elf, blocker: null);

        _bob.PoisonCounters.Should().Be(1);
        _bob.LifeTotal.Should().Be(20);
    }

    // ---- Helpers ----

    private async Task RunCombat(Creature attacker, Creature? blocker)
    {
        var svc = new ContinuousEffectsService();

        attacker.ActiveEffects = svc;
        attacker.SetOwner(_alice); attacker.SetController(_alice);
        attacker.SetZone(ZoneType.Battlefield);
        attacker.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(attacker);

        if (blocker != null)
        {
            blocker.ActiveEffects = svc;
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
            1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

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

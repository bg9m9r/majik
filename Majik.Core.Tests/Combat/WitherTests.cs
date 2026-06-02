using FluentAssertions;
using Majik.Core.Abilities;
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
/// CR 702.90 — Wither. A source with wither deals its damage to a CREATURE
/// in the form of that many -1/-1 counters instead of normal marked damage
/// (CR 702.90b). The form applies to ALL damage to creatures — combat AND
/// noncombat (fight / "deals damage" abilities). Damage to players is
/// unaffected (normal life loss). The layer system applies the -1/-1
/// P/T mod (CR 122.1g / Layer 7c) so a creature reduced to 0 toughness dies
/// to the CR 704.5f/g state-based action.
/// </summary>
public class WitherTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public WitherTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task Wither_CombatDamageToBiggerCreature_AppliesMinusCountersAndSurvives()
    {
        // 3/3 wither attacker blocked by a 4/4 → 4/4 gets three -1/-1 counters
        // → becomes 1/1 and SURVIVES (toughness 1 > 0). No marked damage.
        var attacker = NewCreature("Ram-Gang", 3, 3, _alice, "Wither");
        var blocker = NewCreature("Big Bear", 4, 4, _bob);

        await RunCombat(attacker, blocker);

        blocker.Zone.Should().Be(ZoneType.Battlefield);
        blocker.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(3);
        blocker.Damage.Should().Be(0, "wither damage is -1/-1 counters, not marked damage (CR 702.90b)");
        blocker.Toughness.Should().Be(1);
        blocker.Power.Should().Be(1);
    }

    [Fact]
    public async Task Wither_CombatDamageLethalByCounters_CreatureDies()
    {
        // 3/3 wither attacker blocked by a 2/2 → 2/2 gets (lethal-assigned) two
        // -1/-1 counters → toughness 0 → dies to SBA (CR 704.5g).
        var attacker = NewCreature("Ram-Gang", 3, 3, _alice, "Wither");
        var blocker = NewCreature("Bear", 2, 2, _bob);

        await RunCombat(attacker, blocker);

        blocker.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Wither_NoncombatFightDamage_AppliesMinusCounters()
    {
        // CR 702.90b — wither applies to ALL damage to creatures, including a
        // wither creature's fight (noncombat) damage.
        var svc = new ContinuousEffectsService();
        var witherFighter = new Creature("Ram-Gang", "RGG", 3, 3)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        witherFighter.AddAbility(new KeywordAbility("Wither", witherFighter, _alice));
        var foe = new Creature("Bear", "1G", 4, 4)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        Fx.Fight(witherFighter, foe);

        foe.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(3);
        foe.Damage.Should().Be(0);
        foe.Toughness.Should().Be(1);
        // The non-wither fighter deals normal marked damage back.
        witherFighter.Damage.Should().Be(4);
        witherFighter.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public async Task Wither_DamageToPlayer_IsNormalLifeLoss()
    {
        // CR 702.90b — wither only changes the form of damage to CREATURES.
        // Damage to a player is normal life loss.
        var attacker = NewCreature("Ram-Gang", 3, 3, _alice, "Wither");

        await RunCombat(attacker, blocker: null);

        _bob.LifeTotal.Should().Be(17);
        _bob.PoisonCounters.Should().Be(0, "wither does not give poison counters (that is infect)");
    }

    [Fact]
    public async Task Wither_WithDeathtouch_StillLethalViaCounters()
    {
        // A deathtouch + wither 1/1 deals 1 damage to a 5/5 → one -1/-1 counter
        // (5/5 → 4/4) AND deathtouch marks it for destruction → it dies.
        var attacker = NewCreature("Stigma Lasher", 1, 1, _alice, "Wither", "Deathtouch");
        var blocker = NewCreature("Big Bear", 5, 5, _bob);

        await RunCombat(attacker, blocker);

        blocker.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        blocker.Zone.Should().Be(ZoneType.Graveyard, "deathtouch marks any nonzero damage as lethal (CR 702.2b)");
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

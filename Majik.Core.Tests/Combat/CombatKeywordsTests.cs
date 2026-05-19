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

public class CombatKeywordsTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CombatKeywordsTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task FirstStrike_KillsVanillaBlocker_BeforeReturnDamage()
    {
        var attacker = NewCreature("FS Bear", 2, 2, _alice, "First strike");
        var blocker = NewCreature("Bear", 2, 2, _bob);

        await RunCombat(attacker, blocker);

        attacker.Zone.Should().Be(ZoneType.Battlefield);
        blocker.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task BothFirstStrike_BothDie_Normally()
    {
        var attacker = NewCreature("Knight", 2, 2, _alice, "First strike");
        var blocker = NewCreature("Knight", 2, 2, _bob, "First strike");

        await RunCombat(attacker, blocker);

        attacker.Zone.Should().Be(ZoneType.Graveyard);
        blocker.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task DoubleStrike_KillsBlockerThenStillDealsDamage()
    {
        var attacker = NewCreature("DS Knight", 2, 2, _alice, "Double strike");
        var blocker = NewCreature("Bear", 2, 2, _bob);

        await RunCombat(attacker, blocker);

        attacker.Zone.Should().Be(ZoneType.Battlefield);
        blocker.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task Deathtouch_KillsAnyCreatureWithAnyDamage()
    {
        var attacker = NewCreature("DT Snake", 1, 1, _alice, "Deathtouch");
        var blocker = NewCreature("Big Bear", 5, 5, _bob);

        await RunCombat(attacker, blocker);

        attacker.Zone.Should().Be(ZoneType.Graveyard);  // 5 dmg from bear
        blocker.Zone.Should().Be(ZoneType.Graveyard);   // 1 dmg from snake, lethal because deathtouch
    }

    [Fact]
    public async Task Lifelink_AttackerControllerGainsLife()
    {
        var attacker = NewCreature("Lifelinker", 3, 3, _alice, "Lifelink");
        // unblocked
        await RunCombat(attacker, blocker: null);

        _alice.LifeTotal.Should().Be(23);
        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public async Task Trample_OverflowsToDefender()
    {
        var attacker = NewCreature("Tramp", 4, 4, _alice, "Trample");
        var blocker = NewCreature("Bear", 2, 2, _bob);

        await RunCombat(attacker, blocker);

        _bob.LifeTotal.Should().Be(18); // 2 to blocker (lethal) + 2 overflow
        blocker.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task Indestructible_TakesDamageButSurvives()
    {
        var attacker = NewCreature("Wall", 1, 1, _alice, "Indestructible");
        var blocker = NewCreature("Bear", 5, 5, _bob);

        await RunCombat(attacker, blocker);

        attacker.Zone.Should().Be(ZoneType.Battlefield); // 5 damage but indestructible
        attacker.Damage.Should().Be(5);
        blocker.Zone.Should().Be(ZoneType.Battlefield); // 1 dmg, not lethal
    }

    [Fact]
    public async Task CombatDamage_PublishesEventPerInstance()
    {
        var attacker = NewCreature("Bear", 2, 2, _alice);
        var blocker = NewCreature("Bear", 2, 2, _bob);
        var events = new List<CombatDamageDealtEvent>();
        _bus.Subscribe<CombatDamageDealtEvent>(events.Add);

        await RunCombat(attacker, blocker);

        events.Should().HaveCount(2); // attacker→blocker, blocker→attacker
    }

    // ---- Helpers ----

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

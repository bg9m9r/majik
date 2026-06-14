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
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// CR 702.180 — Toxic N. "Toxic N" means "Whenever this creature deals combat
/// damage to a player, that player gets N poison counters." (CR 702.180b).
/// Unlike infect (CR 702.90c), toxic does NOT change the FORM of the damage:
/// the player still loses life AND, separately, gets N poison counters.
/// Multiple toxic instances on one creature are cumulative (CR 702.180c). The
/// 10-poison loss is a state-based action (CR 704.5c).
///
/// Reminder text on Mirrex's Phyrexian Mite token (Scryfall, verified
/// 2026-06-01): "(Players dealt combat damage by it also get a poison
/// counter.)" — i.e. poison in ADDITION to the normal life loss.
/// </summary>
public class ToxicCombatTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ToxicCombatTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task Toxic_CombatDamageToPlayer_GivesPoisonAndLifeLoss()
    {
        // CR 702.180b — a 1/1 toxic 1 creature dealing combat damage to a player
        // makes that player both lose 1 life AND get 1 poison counter (toxic is
        // additional, NOT a replacement of the damage form like infect).
        var attacker = NewToxicCreature("Toxic Biter", 1, 1, _alice, toxic: 1);

        await RunCombat(attacker, blocker: null);

        _bob.LifeTotal.Should().Be(19, "toxic does not replace the life loss (CR 702.180b)");
        _bob.PoisonCounters.Should().Be(1, "toxic 1 adds one poison counter on combat damage");
    }

    [Fact]
    public async Task Toxic_Cumulative_SumsAllMarkers()
    {
        // CR 702.180c — multiple instances of toxic are cumulative: toxic 2 +
        // toxic 1 on the same creature is toxic 3.
        var attacker = NewToxicCreature("Stacked Toxin", 2, 2, _alice, toxic: 2);
        attacker.AddAbility(new KeywordAbility("toxic", attacker, _alice, arg: 1));

        await RunCombat(attacker, blocker: null);

        _bob.PoisonCounters.Should().Be(3, "toxic 2 + toxic 1 is cumulative (CR 702.180c)");
        _bob.LifeTotal.Should().Be(18, "two combat damage still lost as life");
    }

    [Fact]
    public async Task Toxic_NoPoisonWhenDamagePrevented()
    {
        // CR 702.180b — toxic only triggers on combat damage actually dealt to a
        // player; a toxic creature that is blocked deals no damage to the player,
        // so no poison.
        var attacker = NewToxicCreature("Toxic Biter", 1, 1, _alice, toxic: 1);
        var blocker = NewToxicCreature("Wall", 0, 4, _bob, toxic: 0);

        await RunCombat(attacker, blocker);

        _bob.PoisonCounters.Should().Be(0, "the toxic creature dealt no combat damage to the player");
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task ToxicPlusInfect_GivesInfectPoisonPlusToxicPoison()
    {
        // CR 702.90c + 702.180b — an infect+toxic source deals its damage as
        // poison (infect form: amount), then toxic adds N MORE poison on top.
        // A 1/1 with infect + toxic 1 deals 1 (infect) + 1 (toxic) = 2 poison,
        // and no life loss (infect replaces the life loss).
        var attacker = NewToxicCreature("Corrupt Stinger", 1, 1, _alice, toxic: 1);
        attacker.AddAbility(new KeywordAbility("Infect", attacker, _alice));

        await RunCombat(attacker, blocker: null);

        _bob.LifeTotal.Should().Be(20, "infect replaces the life loss with poison");
        _bob.PoisonCounters.Should().Be(2, "1 infect poison + 1 toxic poison");
    }

    [Fact]
    public async Task Toxic_TenPoison_PlayerLosesViaSba()
    {
        // CR 704.5c — a player with ten or more poison counters loses the game.
        _bob.AddPoisonCounters(9);
        var attacker = NewToxicCreature("Toxic Biter", 1, 1, _alice, toxic: 1);

        await RunCombat(attacker, blocker: null);

        _bob.PoisonCounters.Should().Be(10);
        _sba.CheckStateBasedActions(new[] { _alice, _bob }, System.Array.Empty<Majik.Core.Cards.ICard>());
        _bob.HasLost.Should().BeTrue("ten poison counters is a loss SBA (CR 704.5c)");
    }

    [Fact]
    public async Task MirrexMite_FactoryTokenDealsPoisonInCombat()
    {
        // End-to-end: the real Mirrex Phyrexian Mite token (toxic 1), run through
        // combat, gives the defending player a poison counter on top of the life
        // loss. ("Players dealt combat damage by it also get a poison counter.")
        var mirrex = MirrexFactory.Create(_alice);
        mirrex.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mirrex);

        var mite = MirrexFactory.CreateMiteToken(mirrex, _alice, zones: null);

        await RunCombat(mite, blocker: null);

        _bob.PoisonCounters.Should().Be(1, "the Mite has toxic 1");
        _bob.LifeTotal.Should().Be(19, "the Mite still deals 1 combat damage as life loss");
    }

    // ---- Helpers ----

    private async Task RunCombat(Creature attacker, Creature? blocker)
    {
        var svc = new ContinuousEffectsService();

        attacker.ActiveEffects = svc;
        attacker.SetOwner(_alice); attacker.SetController(_alice);
        if (attacker.Zone != ZoneType.Battlefield)
        {
            attacker.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(attacker);
        }
        attacker.HasSummoningSickness = false;

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

    private static Creature NewToxicCreature(string name, int p, int t, Player owner, int toxic)
    {
        var c = new Creature(name, "1", p, t) { Owner = owner, Controller = owner };
        if (toxic > 0)
        {
            c.AddAbility(new KeywordAbility("toxic", c, owner, arg: toxic));
        }
        return c;
    }
}
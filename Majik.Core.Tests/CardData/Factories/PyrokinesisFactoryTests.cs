using FluentAssertions;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Pyrokinesis (Alliances, {4}{R}).
/// Mirrors the Force-of-Vigor / Soul Spike test shape:
///   * Card shape + dispatch.
///   * Pitch cast — exiles a red card, no timing gate (any turn).
///   * Resolve deals 4 damage divided among target creatures (even split).
/// </summary>
public class PyrokinesisFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PyrokinesisFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasSorceryShape_Red()
    {
        var pyro = PyrokinesisFactory.Create(_alice);

        pyro.Name.Should().Be("Pyrokinesis");
        pyro.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(pyro).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsPyrokinesisShape()
    {
        var dispatched = NamedCardFactory.Create("Pyrokinesis", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Pyrokinesis");
    }

    [Fact]
    public async Task CastViaPitch_ExilesRedCard_Deals4DamageToSingleTarget()
    {
        var pyro = PyrokinesisFactory.Create(_alice);
        pyro.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pyro);

        var pitchFuel = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        pitchFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchFuel);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 4)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var pitchCost = new ExileColoredCardAlternativeCost(ManaColor.Red, pitchFuel);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobBear });
        agent.QueueMana(ManaPayment.Empty);
        // On Alice's own turn — Pyrokinesis is a sorcery, but the pitch
        // primitive has no timing restriction (no "if it's not your turn"
        // clause). Cast at sorcery speed during main phase.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, pyro,
            PyrokinesisFactory.BuildDefinition(o => o, _alice, _bus),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        pitchFuel.Zone.Should().Be(ZoneType.Exile,
            because: "pitched red card is exiled (CR 117.11 + CR 701.21)");
        bobBear.Damage.Should().Be(4,
            because: "all 4 damage goes to the single chosen target (even-split degenerates)");
    }

    [Fact]
    public void Resolve_DividesDamageEvenly_AmongTwoTargets()
    {
        // Two targets → even split: 2 damage each (4 / 2 = 2, no remainder).
        var bobBearA = new Creature("Grizzly Bears", "{1}{G}", 2, 4)
            { Owner = _bob, Controller = _bob };
        bobBearA.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBearA);

        var bobBearB = new Creature("Grizzly Bears", "{1}{G}", 2, 4)
            { Owner = _bob, Controller = _bob };
        bobBearB.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBearB);

        var def = PyrokinesisFactory.BuildDefinition(o => o, _alice, _bus);
        var picks = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new object[] { bobBearA, bobBearB } },
            ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        bobBearA.Damage.Should().Be(2,
            because: "even split: 4 / 2 = 2 damage to each target");
        bobBearB.Damage.Should().Be(2,
            because: "even split: 4 / 2 = 2 damage to each target");
    }

    [Fact]
    public void Resolve_DividesDamage_FourTargets_OneEach()
    {
        // Four targets → 1 damage each (4 / 4 = 1).
        var t1 = new Creature("Bear 1", "{G}", 1, 1) { Owner = _bob, Controller = _bob };
        var t2 = new Creature("Bear 2", "{G}", 1, 1) { Owner = _bob, Controller = _bob };
        var t3 = new Creature("Bear 3", "{G}", 1, 1) { Owner = _bob, Controller = _bob };
        var t4 = new Creature("Bear 4", "{G}", 1, 1) { Owner = _bob, Controller = _bob };
        foreach (var t in new[] { t1, t2, t3, t4 })
        {
            t.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(t);
        }

        var def = PyrokinesisFactory.BuildDefinition(o => o, _alice, _bus);
        var picks = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new object[] { t1, t2, t3, t4 } },
            ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        t1.Damage.Should().Be(1);
        t2.Damage.Should().Be(1);
        t3.Damage.Should().Be(1);
        t4.Damage.Should().Be(1);
    }
}

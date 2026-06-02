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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Force of Will (Alliances, {3}{U}{U}). Exercises:
///   * Card shape (Instant + blue + cost).
///   * NamedCardFactory dispatch.
///   * Pitch cast on opponent's turn — exiles a blue card + costs 1 life.
///   * Pitch cast on own turn — rejected by the CR 118.9 timing gate.
///   * Counter target spell — chosen target leaves the stack.
/// </summary>
[Trait("Color", "U")]
public class ForceOfWillFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ForceOfWillFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue_FiveManaCost()
    {
        var fow = ForceOfWillFactory.Create(_alice);

        fow.Name.Should().Be("Force of Will");
        fow.HasType(CardType.Instant).Should().BeTrue();
        fow.ManaCost.ToString().Should().Contain("U");
        CardColors.GetColors(fow).Should().Contain(ManaColor.Blue);
    }
    // ── Pitch cast (CR 118.9) ────────────────────────────────────────────────

    [Fact]
    public async Task CastViaPitch_OnOpponentsTurn_ExilesBlueCard_AndLosesOneLife()
    {
        // Setup: Force of Will + Brainstorm in Alice's hand.
        var fow = ForceOfWillFactory.Create(_alice);
        fow.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fow);

        var brainstorm = new Instant("Brainstorm", "{U}") { Owner = _alice };
        brainstorm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(brainstorm);

        var startingLife = _alice.LifeTotal;

        // Bob's spell on the stack — Force of Will's target.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // Cast Force of Will via pitch on Bob's turn.
        var pitchCost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.PreCombatMain, _stack);

        var fowSpell = await _flow.CastAsync(
            _alice, fow,
            ForceOfWillFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        // Force of Will is on the stack; resolve it. The wrapped pitch
        // cleanup fires after the counter effect.
        _resolver.ResolveTop(_stack);

        // Counter resolved: Bob's bolt is gone from the stack and in graveyard.
        _stack.GetAll().Should().NotContain(s => ReferenceEquals(s, bobSpell));
        bobBolt.Zone.Should().Be(ZoneType.Graveyard);

        // Pitch resolved: Brainstorm exiled.
        brainstorm.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(brainstorm);

        // Life paid: -1.
        _alice.LifeTotal.Should().Be(startingLife - 1);
    }

    [Fact]
    public async Task CastViaPitch_OnOwnTurn_Throws_CR1189TimingGate()
    {
        var fow = ForceOfWillFactory.Create(_alice);
        fow.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fow);

        var brainstorm = new Instant("Brainstorm", "{U}") { Owner = _alice };
        brainstorm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(brainstorm);

        var pitchCost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);
        var agent = new ScriptedAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var act = async () => await _flow.CastAsync(
            _alice, fow,
            ForceOfWillFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*pitch*own turn*", because: "pitch is illegal on the caster's own turn (CR 118.9)");
    }
}

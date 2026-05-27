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
/// End-to-end tests for Force of Despair (Modern Horizons 2, {3}{B}).
/// Mirrors the Force-of-Negation / Force-of-Vigor test shape:
///   * Card shape + dispatch.
///   * Pitch cast on opponent's turn — exiles a black card, no life loss.
///   * Destroys only creatures that entered this turn — bystanders survive.
///   * PitchAltCostProbe surfaces a Black / 0-life candidate from
///     <see cref="PitchAltCostProbe.DefaultLookup"/>.
/// </summary>
public class ForceOfDespairFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ForceOfDespairFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Black()
    {
        var fod = ForceOfDespairFactory.Create(_alice);

        fod.Name.Should().Be("Force of Despair");
        fod.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(fod).Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsForceOfDespairShape()
    {
        var dispatched = NamedCardFactory.Create("Force of Despair", _alice);
        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Force of Despair");
    }

    [Fact]
    public void PitchAltCostProbe_DefaultLookup_RecognisesForceOfDespair_BlackZeroLife()
    {
        var fod = ForceOfDespairFactory.Create(_alice);
        fod.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fod);

        var blackFuel = new Instant("Fatal Push", "{B}") { Owner = _alice };
        blackFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blackFuel);

        var probe = new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 1, PhaseStateType.PreCombatMain, _stack);

        var candidates = probe.CandidatesFor(fod, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        var pitch = candidates[0].Should().BeOfType<PitchAlternativeCost>().Subject;
        pitch.RequiredColor.Should().Be(ManaColor.Black);
        pitch.LifeCost.Should().Be(0);
        pitch.ExiledCard.Should().BeSameAs(blackFuel);
    }

    [Fact]
    public void Resolve_NoTurnStateWired_IsNoOp()
    {
        // Shape-only path: BuildSpellDefinition's resolveBody when the
        // turnState callback returns null does not destroy anything.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = ForceOfDespairFactory.BuildSpellDefinition(() => null);
        var picks = new ChosenSpellParams(null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            because: "without TurnState wiring the destroy half is a clean no-op");
    }

    [Fact]
    public void Resolve_DestroysCreaturesThatEnteredThisTurn_LeavesOthers()
    {
        var turnState = new Majik.Core.Game.TurnState();

        // Pre-existing creature — NOT in the ETB-this-turn ledger.
        var bobOldBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobOldBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobOldBear);

        // Fresh creatures — recorded as entering this turn.
        var bobFreshBear = new Creature("Fresh Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobFreshBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobFreshBear);
        turnState.RecordPermanentEnteredBattlefield(bobFreshBear);

        var aliceFreshGoblin = new Creature("Goblin Guide", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };
        aliceFreshGoblin.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceFreshGoblin);
        turnState.RecordPermanentEnteredBattlefield(aliceFreshGoblin);

        // A non-creature permanent that ETB'd this turn — should NOT be
        // destroyed (Force of Despair targets creatures only).
        var bobLand = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
            { Owner = _bob, Controller = _bob };
        bobLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobLand);
        turnState.RecordPermanentEnteredBattlefield(bobLand);

        var def = ForceOfDespairFactory.BuildSpellDefinition(() => turnState);
        var picks = new ChosenSpellParams(null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        bobOldBear.Zone.Should().Be(ZoneType.Battlefield,
            because: "pre-existing creatures didn't enter this turn → spared");
        bobFreshBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "creatures that entered this turn are destroyed");
        aliceFreshGoblin.Zone.Should().Be(ZoneType.Graveyard,
            because: "Force of Despair destroys ALL creatures that entered this turn (any controller)");
        bobLand.Zone.Should().Be(ZoneType.Battlefield,
            because: "Force of Despair targets only creatures, not lands");
    }

    [Fact]
    public async Task CastViaPitch_OnOpponentsTurn_ExilesBlackCard_NoLifeLoss()
    {
        var fod = ForceOfDespairFactory.Create(_alice);
        fod.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fod);

        var pitchFuel = new Instant("Fatal Push", "{B}") { Owner = _alice };
        pitchFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchFuel);

        var startingLife = _alice.LifeTotal;

        var pitchCost = new PitchAlternativeCost(ManaColor.Black, pitchFuel, lifeCost: 0);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fod,
            ForceOfDespairFactory.BuildSpellDefinition(() => null),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        pitchFuel.Zone.Should().Be(ZoneType.Exile,
            because: "pitched black card is exiled (CR 118.9)");
        _alice.LifeTotal.Should().Be(startingLife,
            because: "Force of Despair has no life rider");
    }
}

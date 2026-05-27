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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Daze (Nemesis, {1}{U}).
/// Exercises:
///   * Card shape + dispatch.
///   * Pitch cast — returns Island to its owner's hand, no mana paid.
///   * Pitch cast with no Island controlled — alt-cost rejected.
///   * Resolve "counter target spell unless its controller pays {1}" —
///     auto-pay when {1} is available, otherwise counter.
/// </summary>
public class DazeFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DazeFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var daze = DazeFactory.Create(_alice);

        daze.Name.Should().Be("Daze");
        daze.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(daze).Should().Contain(ManaColor.Blue);
        daze.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDazeFactoryShape()
    {
        var dispatched = NamedCardFactory.Create("Daze", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Daze");
    }

    [Fact]
    public async Task CastViaPitch_ReturnsIslandToOwnersHand_NoManaPaid()
    {
        // Alice has Daze in hand + an Island on battlefield. Casts Daze via
        // the bounce-land pitch alt cost; on resolution the Island returns
        // to her hand, no mana spent from her pool, and Bob's spell is
        // countered (Bob has no {1} available to pay the unless-rider).
        var daze = DazeFactory.Create(_alice);
        daze.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(daze);

        var island = NamedCardFactory.Create("Island", _alice);
        ((Card)island).SetController(_alice);
        island.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island);

        var startingMana = _alice.ManaPool.Total;

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var pitchCost = new BounceLandPitchAlternativeCost(CardSubtype.Island, island);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, daze,
            DazeFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        island.Zone.Should().Be(ZoneType.Hand, because: "the pitch cost returns the Island to its owner's hand");
        _alice.Zones.Hand.GetCards().Should().Contain(island);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(island);
        _alice.ManaPool.Total.Should().Be(startingMana, because: "pitch pays no mana");
        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob has no {1} to pay; the unless-pay rider fails and Daze counters");
    }

    [Fact]
    public async Task CastNormal_CountersUnlessControllerPaysOne_AutoPaysWhenAble()
    {
        // Alice pays the printed {1}{U} (skipped via ManaPayment.Empty stub),
        // targets Bob's Bolt. Bob has {1} in his pool — the engine auto-pays
        // and Daze fizzles (Bolt resolves; here we assert it was NOT countered).
        var daze = DazeFactory.Create(_alice);
        daze.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(daze);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // Bob has {1} in his pool to pay the unless-rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, daze,
            DazeFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {1}; the counter no-ops and Bolt remains uncountered");
    }

    [Fact]
    public void PitchCost_RejectsCast_WhenChosenIslandNotControlledByCaster()
    {
        // Daze's pitch alt cost requires the bounced Island be on the
        // battlefield AND controlled by the caster. A "Mountain" doesn't
        // match the Island subtype predicate so CanCastFor returns false —
        // mirrors the "no Island controlled" failure case (the alt-cost
        // probe would yield zero candidates).
        var daze = DazeFactory.Create(_alice);
        daze.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(daze);

        var mountain = NamedCardFactory.Create("Mountain", _alice);
        ((Card)mountain).SetController(_alice);
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);

        // Wrong subtype — Mountain is not an Island.
        var wrongSubtype = new BounceLandPitchAlternativeCost(CardSubtype.Island, mountain);
        wrongSubtype.CanCastFor(daze, _alice).Should().BeFalse();

        // Right subtype but controlled by Bob — also rejected.
        var bobIsland = NamedCardFactory.Create("Island", _bob);
        ((Card)bobIsland).SetController(_bob);
        bobIsland.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobIsland);
        var wrongController = new BounceLandPitchAlternativeCost(CardSubtype.Island, bobIsland);
        wrongController.CanCastFor(daze, _alice).Should().BeFalse();
    }

    [Fact]
    public void PitchCost_AcceptsCast_WhenIslandControlled()
    {
        var daze = DazeFactory.Create(_alice);
        daze.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(daze);

        var island = NamedCardFactory.Create("Island", _alice);
        ((Card)island).SetController(_alice);
        island.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island);

        var pitch = new BounceLandPitchAlternativeCost(CardSubtype.Island, island);
        pitch.CanCastFor(daze, _alice).Should().BeTrue();
        pitch.AlternativeManaCost.Should().Be(ManaCost.Zero);
        pitch.IsLegalInContext(_alice).Should().BeTrue(
            because: "Daze prints no timing restriction on its pitch cost");
    }
}

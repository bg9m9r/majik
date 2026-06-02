using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
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
/// End-to-end tests for Condemn ({W}).
/// Oracle: "Put target attacking creature on the bottom of its owner's
/// library. Its controller gains life equal to its toughness."
///
/// Coverage:
///   * Card identity + dispatch by name (instant, white, MV 1).
///   * Single 1..1 "target attacking creature" request, no variable X.
///   * Candidate gatherer offers ONLY attacking creatures (a bystander
///     is not a legal target — CR 506.2); null lookup → empty.
///   * Resolve: a 3/4 attacker → bottom of OWNER's library + +4 life to its
///     controller; library bottom ordering verified.
///   * Toughness-0 target → relocated, zero lifegain (no GainLife throw).
///   * Non-creature / off-battlefield target at resolution → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class CondemnFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CondemnFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ---------------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_White()
    {
        var condemn = CondemnFactory.Create(_alice);

        condemn.Name.Should().Be("Condemn");
        condemn.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(condemn).Should().Contain(ManaColor.White);
        condemn.ManaCostValue.TotalValue.Should().Be(1);
        condemn.Owner.Should().BeSameAs(_alice);
        condemn.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BuildDefinition_HasOneRequiredTarget_NoVariableX()
    {
        var def = CondemnFactory.BuildDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target attacking creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // Candidate gatherer — only attacking creatures
    // ---------------------------------------------------------------------

    [Fact]
    public void CandidateGatherer_OnlyAttackingCreatures()
    {
        // Attacker — legal target.
        var attacker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        attacker.SetOwner(_bob);
        attacker.SetController(_bob);
        attacker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(attacker);

        // Bystander — NOT attacking, must NOT be offered.
        var bystander = new Creature("Savannah Lions", "{W}", 2, 1);
        bystander.SetOwner(_alice);
        bystander.SetController(_alice);
        bystander.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bystander);

        IReadOnlyList<Creature> AttackerLookup() => new[] { attacker };

        var def = CondemnFactory.BuildDefinition(o => o, AttackerLookup);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _bob, 1,
            PhaseStateType.DeclareBlockers, _stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(attacker, "attacking creatures are legal targets");
        candidates.Should().NotContain(bystander,
            "a creature that is not attacking is not a legal target for Condemn (CR 506.2)");
    }

    [Fact]
    public void CandidateGatherer_NullLookup_ReturnsEmpty()
    {
        var def = CondemnFactory.BuildDefinition(o => o, attackerLookup: null);
        var ctx = new GameContext(
            _alice, new[] { _alice }, _alice, 1,
            PhaseStateType.DeclareBlockers, _stack);

        def.TargetRequests[0].ResolveCandidates(ctx).Should().BeEmpty(
            "with no combat lookup wired the gatherer reports no candidates");
    }

    // ---------------------------------------------------------------------
    // Resolve semantics
    // ---------------------------------------------------------------------

    [Fact]
    public async Task TargetingAttacker_BottomsOwnersLibraryAndGainsLifeEqualToToughness()
    {
        // Bob controls a 3/4 attacker. Seed his library with a marker so we
        // can prove the creature lands on the BOTTOM (last) of the library.
        var libraryMarker = new Card("Plains", "", new[] { CardType.Land });
        libraryMarker.SetOwner(_bob);
        _bob.Zones.Library.AddCard(libraryMarker);

        var attacker = new Creature("Big Threat", "{2}{G}", 3, 4);
        attacker.SetOwner(_bob);
        attacker.SetController(_bob);
        _zones.MoveCard(attacker, ZoneType.Library, ZoneType.Battlefield, _bob);

        var startingLife = _bob.LifeTotal;

        await CastAndResolveAsync(attacker, () => new[] { attacker });

        attacker.Zone.Should().Be(ZoneType.Library,
            because: "Condemn puts its target on the bottom of its owner's library");
        _bob.Zones.Library.GetCards().Should().Contain(attacker);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(attacker);
        _bob.Zones.Library.GetCards().Last().Should().BeSameAs(attacker,
            because: "the creature goes on the BOTTOM of the library (after the existing marker)");
        _bob.LifeTotal.Should().Be(startingLife + 4,
            because: "lifegain equals the attacker's toughness (4)");
    }

    [Fact]
    public async Task TargetingToughnessZeroAttacker_BottomsWithNoLifeChange()
    {
        // A 1/0-ish creature can't normally exist on the battlefield (SBA),
        // but a 0-toughness snapshot must not call GainLife (which throws on
        // negative/zero-floored). Use a 2/0 token-shaped creature placed
        // directly to exercise the zero-lifegain branch.
        var attacker = new Creature("Glass Cannon", "{R}", 2, 0);
        attacker.SetOwner(_bob);
        attacker.SetController(_bob);
        _zones.MoveCard(attacker, ZoneType.Library, ZoneType.Battlefield, _bob);

        var startingLife = _bob.LifeTotal;

        await CastAndResolveAsync(attacker, () => new[] { attacker });

        attacker.Zone.Should().Be(ZoneType.Library);
        _bob.LifeTotal.Should().Be(startingLife,
            because: "toughness was 0 — no life is gained");
    }

    [Fact]
    public void Resolve_NonCreatureTarget_NoOp()
    {
        // CR 608.2b — non-Creature resolved object → no-op (defensive guard).
        var land = new Card("Island", "", new[] { CardType.Land });
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var def = CondemnFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            null, null, new[] { new object[] { land } }, ManaPayment.Empty);

        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow("non-Creature target at resolution is a no-op (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
    }

    [Fact]
    public void Resolve_TargetLeftBattlefield_NoOp()
    {
        // CR 608.2b — target already left the battlefield.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        var startingLife = _bob.LifeTotal;

        var def = CondemnFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            null, null, new[] { new object[] { creature } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "target was not on the battlefield at resolution — Condemn does nothing");
        _bob.LifeTotal.Should().Be(startingLife);
    }

    // ---------------------------------------------------------------------
    // Helper — full SpellCastFlow → StackResolver round-trip.
    // ---------------------------------------------------------------------

    private async Task CastAndResolveAsync(object target, Func<IReadOnlyList<Creature>> attackerLookup)
    {
        var condemn = CondemnFactory.Create(_alice);
        condemn.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(condemn);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, condemn,
            CondemnFactory.BuildDefinition(o => o, attackerLookup),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}

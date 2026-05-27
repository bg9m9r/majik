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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Swords to Plowshares (Alpha, {W}).
/// Oracle: "Exile target creature. Its controller gains life equal to its power."
///
/// Coverage:
///   * Card identity + dispatch by name.
///   * Vanilla Bear (2/2) target → exile + +2 life to its controller.
///   * Tarmogoyf with 3 card types in graveyards (3/4) → exile + +3 life to
///     its controller (validates that lifegain reads live Compute power, not
///     printed/base power).
///   * Power-0 target → exile + zero lifegain (no Player.GainLife call).
///   * Non-creature / off-battlefield target → effect is a no-op (CR 608.2b).
/// </summary>
public class SwordsToPlowsharesTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SwordsToPlowsharesTests()
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
        var stp = SwordsToPlowsharesFactory.Create(_alice);

        stp.Name.Should().Be("Swords to Plowshares");
        stp.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(stp).Should().Contain(ManaColor.White);
        stp.ManaCostValue.TotalValue.Should().Be(1);
        stp.Owner.Should().BeSameAs(_alice);
        stp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsSwordsToPlowsharesShape()
    {
        var dispatched = NamedCardFactory.Create("Swords to Plowshares", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Swords to Plowshares");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // Resolve semantics
    // ---------------------------------------------------------------------

    [Fact]
    public async Task TargetingBear_ExilesAndGainsLifeEqualToPower()
    {
        var stp = SwordsToPlowsharesFactory.Create(_alice);
        stp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(stp);

        // Bob controls a 2/2 Bear.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Battlefield, _bob);

        var startingLife = _bob.LifeTotal;

        await CastAndResolveAsync(stp, bear);

        bear.Zone.Should().Be(ZoneType.Exile, because: "Swords to Plowshares exiles its target");
        _bob.Zones.Exile.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
        _bob.LifeTotal.Should().Be(startingLife + 2, because: "Bear's power was 2");
    }

    [Fact]
    public async Task TargetingTarmogoyf_GainsLifeEqualToLiveComputePower()
    {
        // Wire Tarmogoyf with a real ContinuousEffectsService so its CDA
        // power (= distinct card types across all graveyards) drives the
        // lifegain via Compute. Three distinct types → power 3, toughness 4.
        var effects = new ContinuousEffectsService();
        Func<IEnumerable<ICard>> allGraveyards = () =>
            _alice.Zones.Graveyard.GetCards().Concat(_bob.Zones.Graveyard.GetCards());
        var goyf = TarmogoyfFactory.Create(_bob, effects, _bus, allGraveyards);
        goyf.ActiveEffects = effects;
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _bob);

        // Three distinct card types in graveyards (Instant, Sorcery, Artifact).
        var instant = new Card("Lightning Bolt", "{R}", new[] { CardType.Instant });
        instant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(instant);

        var sorcery = new Card("Wrath of God", "{2}{W}{W}", new[] { CardType.Sorcery });
        sorcery.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(sorcery);

        var artifact = new Card("Sol Ring", "{1}", new[] { CardType.Artifact });
        artifact.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(artifact);

        goyf.Power.Should().Be(3, because: "3 distinct card types across graveyards");
        goyf.Toughness.Should().Be(4);

        var stp = SwordsToPlowsharesFactory.Create(_alice);
        stp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(stp);

        var startingLife = _bob.LifeTotal;

        await CastAndResolveAsync(stp, goyf);

        goyf.Zone.Should().Be(ZoneType.Exile);
        _bob.LifeTotal.Should().Be(startingLife + 3,
            because: "Tarmogoyf's live Compute power was 3");
    }

    [Fact]
    public async Task TargetingPowerZeroCreature_ExilesWithNoLifeChange()
    {
        // A 0/1 creature (e.g. Memnite-shaped vanilla wall-like).
        var wall = new Creature("Tiny Wall", "{W}", 0, 1);
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        _zones.MoveCard(wall, ZoneType.Library, ZoneType.Battlefield, _bob);

        var stp = SwordsToPlowsharesFactory.Create(_alice);
        stp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(stp);

        var startingLife = _bob.LifeTotal;

        await CastAndResolveAsync(stp, wall);

        wall.Zone.Should().Be(ZoneType.Exile);
        _bob.LifeTotal.Should().Be(startingLife,
            because: "Power was 0 — no life is gained");
    }

    [Fact]
    public void BuildDefinition_HasOneRequiredTarget_NoVariableX()
    {
        var def = SwordsToPlowsharesFactory.BuildDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // Helper — full SpellCastFlow → StackResolver round-trip.
    // ---------------------------------------------------------------------

    private async Task CastAndResolveAsync(Instant stp, object target)
    {
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, stp,
            SwordsToPlowsharesFactory.BuildDefinition(o => o),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}

using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// TDD tests for Pillar of Light (Magic 2015 / various, {2}{W}).
/// Oracle: "Exile target creature with toughness 4 or greater."
///
/// Coverage:
///   - Card identity: Instant, {2}{W}, white, CMC 3.
///   - NamedCardFactory dispatch by name.
///   - SpellDefinition shape: 1 target request ("target creature with toughness 4 or greater"),
///     no X, no modes.
///   - Resolving against a creature with toughness 4 → exiles it (CR 701.21).
///   - Resolving against a creature with toughness 5 (1/5) → exiles it.
///   - Resolving against a creature with toughness 3 (2/3) → no-op (CR 608.2b).
///   - Target no longer on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
public class PillarOfLightFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PillarOfLightFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ------------------------------------------------------------------
    // Identity / dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_WhiteManaCostThree()
    {
        var pol = PillarOfLightFactory.Create(_alice);

        pol.Name.Should().Be("Pillar of Light");
        pol.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(pol).Should().Contain(ManaColor.White);
        pol.ManaCostValue.TotalValue.Should().Be(3);
        pol.Owner.Should().BeSameAs(_alice);
        pol.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsPillarOfLightShape()
    {
        var dispatched = NamedCardFactory.Create("Pillar of Light", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Pillar of Light");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_OneRequiredTarget_NoXNoModes()
    {
        var def = PillarOfLightFactory.BuildDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target creature with toughness 4 or greater");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Resolve — exile creature with toughness exactly 4
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetingCreatureWithToughnessFour_ExilesIt()
    {
        // A 3/4 creature — toughness exactly 4.
        var defender = new Creature("Wall of Omens", "{1}{W}", 0, 4);
        defender.SetOwner(_bob);
        defender.SetController(_bob);
        _zones.MoveCard(defender, ZoneType.Library, ZoneType.Battlefield, _bob);

        var pol = PillarOfLightFactory.Create(_alice);
        pol.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pol);

        await CastAndResolveAsync(pol, defender);

        defender.Zone.Should().Be(ZoneType.Exile, because: "Pillar of Light exiles creatures with toughness 4 or greater");
        _bob.Zones.Exile.GetCards().Should().Contain(defender);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(defender);
    }

    // ------------------------------------------------------------------
    // Resolve — exile creature with toughness 5 (1/5)
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetingCreatureWithToughnessFive_ExilesIt()
    {
        // A 1/5 creature — toughness 5, which is ≥ 4.
        var colossal = new Creature("Colossal Dreadmaw", "{4}{G}{G}", 6, 6);
        colossal.SetOwner(_bob);
        colossal.SetController(_bob);
        _zones.MoveCard(colossal, ZoneType.Library, ZoneType.Battlefield, _bob);

        var pol = PillarOfLightFactory.Create(_alice);
        pol.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pol);

        await CastAndResolveAsync(pol, colossal);

        colossal.Zone.Should().Be(ZoneType.Exile, because: "Pillar of Light exiles creatures with toughness 5 (>=4)");
        _bob.Zones.Exile.GetCards().Should().Contain(colossal);
    }

    // ------------------------------------------------------------------
    // Resolve — no-op on creature with toughness < 4 (CR 608.2b)
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetingCreatureWithToughnessTwoThree_IsNoOp()
    {
        // A 2/3 creature — toughness 3, which is < 4; effect does nothing.
        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        _zones.MoveCard(goblin, ZoneType.Library, ZoneType.Battlefield, _bob);

        var pol = PillarOfLightFactory.Create(_alice);
        pol.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pol);

        await CastAndResolveAsync(pol, goblin);

        goblin.Zone.Should().Be(ZoneType.Battlefield,
            because: "Toughness 2 is less than 4 — Pillar of Light has no effect (CR 608.2b)");
    }

    // ------------------------------------------------------------------
    // Resolve — target left battlefield before resolution (CR 608.2b)
    // ------------------------------------------------------------------

    [Fact]
    public async Task TargetLeavesFieldBeforeResolution_IsNoOp()
    {
        // A large creature that moves to the graveyard before resolution.
        var angel = new Creature("Serra Angel", "{3}{W}{W}", 4, 4);
        angel.SetOwner(_bob);
        angel.SetController(_bob);
        _zones.MoveCard(angel, ZoneType.Library, ZoneType.Battlefield, _bob);

        // Move it off the battlefield before casting/resolving.
        _zones.MoveCard(angel, ZoneType.Battlefield, ZoneType.Graveyard, _bob);

        var pol = PillarOfLightFactory.Create(_alice);
        pol.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pol);

        await CastAndResolveAsync(pol, angel);

        angel.Zone.Should().Be(ZoneType.Graveyard,
            because: "Target was not on the battlefield at resolution — no-op per CR 608.2b");
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    private async Task CastAndResolveAsync(Instant pol, object target)
    {
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, pol,
            PillarOfLightFactory.BuildDefinition(o => o),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}

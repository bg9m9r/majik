using FluentAssertions;
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
/// End-to-end tests for Exorcise (Tarkir: Dragonstorm, {1}{W}).
/// Oracle: "Exile target artifact, enchantment, or creature with power 4 or greater."
///
/// Coverage (the card's UNIQUE behaviour — the broadened exile predicate):
///   * Identity: Sorcery {1}{W}, white.
///   * Exiles an artifact (regardless of power).
///   * Exiles an enchantment (regardless of power).
///   * Exiles a creature with power 4 (boundary — "4 or greater").
///   * No-op against a creature with power 3 (below threshold, not artifact/ench).
///   * CR 608.2b — off-battlefield target is a no-op.
/// </summary>
[Trait("Color", "W")]
public class ExorciseTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ExorciseTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ---------------------------------------------------------------------
    // Identity
    // ---------------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_White_TwoMana()
    {
        var card = ExorciseFactory.Create(_alice);

        card.Name.Should().Be("Exorcise");
        card.Should().BeOfType<Sorcery>();
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_HasOneRequiredTarget_NoVariableX()
    {
        var def = ExorciseFactory.BuildDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // Predicate — what qualifies
    // ---------------------------------------------------------------------

    [Fact]
    public async Task TargetingArtifact_ExilesRegardlessOfPower()
    {
        // A 0-power artifact (non-creature) still qualifies on its type alone.
        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        _zones.MoveCard(artifact, ZoneType.Library, ZoneType.Battlefield, _bob);

        await CastAndResolveAsync(artifact);

        artifact.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public async Task TargetingEnchantment_ExilesRegardlessOfPower()
    {
        var enchantment = new Enchantment("Oblivion Ring", "{2}{W}");
        enchantment.SetOwner(_bob);
        enchantment.SetController(_bob);
        _zones.MoveCard(enchantment, ZoneType.Library, ZoneType.Battlefield, _bob);

        await CastAndResolveAsync(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(enchantment);
    }

    [Fact]
    public async Task TargetingCreaturePower4_Exiles_BoundaryInclusive()
    {
        // Power exactly 4 — "4 or greater" includes the boundary.
        var beast = new Creature("Hulking Beast", "{4}{G}", 4, 4);
        beast.SetOwner(_bob);
        beast.SetController(_bob);
        _zones.MoveCard(beast, ZoneType.Library, ZoneType.Battlefield, _bob);

        await CastAndResolveAsync(beast);

        beast.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(beast);
    }

    [Fact]
    public async Task TargetingCreaturePower3_IsNoOp_BelowThreshold()
    {
        // Power 3 creature that is NOT an artifact/enchantment — illegal target;
        // CR 608.2b makes the resolution a no-op.
        var bear = new Creature("Big Bear", "{2}{G}", 3, 3);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Battlefield, _bob);

        ExorciseFactory.IsLegalTarget(bear).Should().BeFalse();

        await CastAndResolveAsync(bear);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            because: "power 3 is below the 'power 4 or greater' creature gate");
        _bob.Zones.Exile.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public async Task TargetingOffBattlefieldPermanent_IsNoOp()
    {
        // CR 608.2b — a target that has left the battlefield is illegal.
        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        await CastAndResolveAsync(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "target was not on the battlefield at resolution");
    }

    // ---------------------------------------------------------------------
    // Helper — full SpellCastFlow → StackResolver round-trip.
    // ---------------------------------------------------------------------

    private async Task CastAndResolveAsync(object target)
    {
        var card = ExorciseFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            ExorciseFactory.BuildDefinition(o => o),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}

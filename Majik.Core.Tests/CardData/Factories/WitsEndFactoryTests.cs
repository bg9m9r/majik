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
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Wit's End ({5}{B}{B}, Sorcery — "Target player discards their hand.").
///
/// Coverage:
/// - Card identity: Sorcery, black, {5}{B}{B}, owner/controller wired.
/// - NamedCardFactory dispatcher returns the correct shape.
/// - SpellDefinition shape: 1 target-player request, no modes, no X.
/// - Resolve: target player with 4 cards in hand discards all 4 → hand empty,
///   4 in graveyard (CR 701.7).
/// - Resolve: target player with empty hand → no-op (CR 701.7c).
/// </summary>
public class WitsEndFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public WitsEndFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_Black_AtCost5BB()
    {
        var card = WitsEndFactory.Create(_alice);

        card.Name.Should().Be("Wit's End");
        card.ManaCost.Should().Be("{5}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
    }

    // ------------------------------------------------------------------
    // Dispatcher
    // ------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsWitsEnd()
    {
        var dispatched = NamedCardFactory.Create("Wit's End", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Wit's End");
    }

    // ------------------------------------------------------------------
    // SpellDefinition shape
    // ------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_DeclaresOneTargetPlayerRequest_NoModesNoX()
    {
        var def = WitsEndFactory.BuildSpellDefinition(
            resolver: x => x,
            targetAgent: null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().ContainEquivalentOf("player");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // Resolution — full hand (4 cards): all discarded
    // ------------------------------------------------------------------

    [Fact]
    public async Task Resolve_TargetWithFourCards_DiscardsAll_HandEmpty()
    {
        FillHand(_bob, 4);

        var card = WitsEndFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var def = WitsEndFactory.BuildSpellDefinition(
            resolver: x => x,
            targetAgent: null);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);
        await _flow.CastAsync(_alice, card, def, agent, ctx);
        _resolver.ResolveTop(_stack);

        _bob.Zones.Hand.Count.Should().Be(0);
        _bob.Zones.Graveyard.Count.Should().Be(4);
    }

    // ------------------------------------------------------------------
    // Resolution — empty hand: no-op (CR 701.7c)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Resolve_TargetWithEmptyHand_IsNoOp()
    {
        var card = WitsEndFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var def = WitsEndFactory.BuildSpellDefinition(
            resolver: x => x,
            targetAgent: null);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);
        await _flow.CastAsync(_alice, card, def, agent, ctx);

        var act = () => _resolver.ResolveTop(_stack);
        act.Should().NotThrow();

        _bob.Zones.Hand.Count.Should().Be(0);
        _bob.Zones.Graveyard.Count.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void FillHand(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var s = new Sorcery($"Card{i + 1}", "{1}");
            s.SetOwner(player);
            s.SetController(player);
            s.SetZone(ZoneType.Hand);
            player.Zones.Hand.AddCard(s);
        }
    }
}

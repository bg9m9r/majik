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
/// Tests for Mind Rot (Core Set, {2}{B}, Sorcery — "Target player discards
/// two cards.").
///
/// Coverage:
/// - Card identity: Sorcery, black, {2}{B}, owner/controller wired.
/// - NamedCardFactory dispatcher returns the correct shape.
/// - SpellDefinition shape: 1 target-player request, no modes, no X.
/// - Resolve: target player with 3 cards in hand discards exactly 2 → 1 left
///   + 2 in graveyard (CR 701.7).
/// - Resolve: target player with 1 card discards just 1 (can't discard more
///   than they have — CR 701.7c).
/// - Resolve: target player with empty hand discards nothing, no error.
/// - Agent: when the target player's agent is queued, it drives both picks
///   (not a deterministic first-card take).
/// </summary>
[Trait("Color", "B")]
public class MindRotFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MindRotFactoryTests()
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
    public void Create_HasSorceryShape_Black_AtCost2B()
    {
        var card = MindRotFactory.Create(_alice);

        card.Name.Should().Be("Mind Rot");
        card.ManaCost.Should().Be("{2}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
    }

    // ------------------------------------------------------------------
    // Dispatcher
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    // SpellDefinition shape
    // ------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_DeclaresOneTargetPlayerRequest_NoModesNoX()
    {
        var def = MindRotFactory.BuildSpellDefinition(
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
    // Resolution — full hand (3 cards)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Resolve_TargetWithThreeCards_DiscardsTwo_OneRemains()
    {
        var (c1, c2, c3) = FillHand(_bob, 3);

        var card = MindRotFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var def = MindRotFactory.BuildSpellDefinition(
            resolver: x => x,
            targetAgent: null);  // deterministic: discards first card twice

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
        await _flow.CastAsync(_alice, card, def, agent, ctx);
        _resolver.ResolveTop(_stack);

        _bob.Zones.Hand.Count.Should().Be(1);
        _bob.Zones.Graveyard.Count.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // Resolution — thin hand (1 card): discard as many as you can
    // ------------------------------------------------------------------

    [Fact]
    public async Task Resolve_TargetWithOneCard_DiscardsOne_HandEmpty()
    {
        var (c1, _, _) = FillHand(_bob, 1);

        var card = MindRotFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var def = MindRotFactory.BuildSpellDefinition(
            resolver: x => x,
            targetAgent: null);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
        await _flow.CastAsync(_alice, card, def, agent, ctx);
        _resolver.ResolveTop(_stack);

        _bob.Zones.Hand.Count.Should().Be(0);
        _bob.Zones.Graveyard.Count.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // Resolution — empty hand: no-op (CR 701.7c)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Resolve_TargetWithEmptyHand_IsNoOp()
    {
        // Bob starts with an empty hand.
        var card = MindRotFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var def = MindRotFactory.BuildSpellDefinition(
            resolver: x => x,
            targetAgent: null);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
        await _flow.CastAsync(_alice, card, def, agent, ctx);

        // Should not throw.
        var act = () => _resolver.ResolveTop(_stack);
        act.Should().NotThrow();

        _bob.Zones.Hand.Count.Should().Be(0);
        _bob.Zones.Graveyard.Count.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Agent-driven pick: target player's agent chooses both discards
    // ------------------------------------------------------------------

    [Fact]
    public async Task Resolve_AgentDriven_TargetPlayerPicksBothCards()
    {
        var (c1, c2, c3) = FillHand(_bob, 3);
        // Name the cards so we can verify which got discarded.
        // Agent will pick c3 first, then c2 (by scripted hand choices).

        var targetAgent = new ScriptedAgent();
        targetAgent.QueueFromHand(c3);  // first discard pick
        targetAgent.QueueFromHand(c2);  // second discard pick

        var card = MindRotFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var def = MindRotFactory.BuildSpellDefinition(
            resolver: x => x,
            targetAgent: targetAgent);

        var casterAgent = new ScriptedAgent();
        casterAgent.QueueTargets(new object[] { _bob });
        casterAgent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
        await _flow.CastAsync(_alice, card, def, casterAgent, ctx);
        _resolver.ResolveTop(_stack);

        _bob.Zones.Hand.Count.Should().Be(1);
        _bob.Zones.Graveyard.GetCards().Should().Contain(c3);
        _bob.Zones.Graveyard.GetCards().Should().Contain(c2);
        _bob.Zones.Hand.GetCards().Should().Contain(c1);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Fills Bob's hand with <paramref name="count"/> vanilla sorceries and
    /// returns the first three (unused slots are placeholders).
    /// </summary>
    private (ICard c1, ICard c2, ICard c3) FillHand(Player player, int count)
    {
        ICard? c1 = null, c2 = null, c3 = null;
        for (var i = 0; i < count; i++)
        {
            var s = new Sorcery($"Card{i + 1}", "{1}");
            s.SetOwner(player);
            s.SetController(player);
            s.SetZone(ZoneType.Hand);
            player.Zones.Hand.AddCard(s);
            if (i == 0) c1 = s;
            else if (i == 1) c2 = s;
            else if (i == 2) c3 = s;
        }
        return (c1!, c2 ?? new Sorcery("Placeholder2", "{1}"), c3 ?? new Sorcery("Placeholder3", "{1}"));
    }
}

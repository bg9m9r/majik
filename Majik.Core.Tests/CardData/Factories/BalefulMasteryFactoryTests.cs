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
/// End-to-end tests for Baleful Mastery (Commander Legends: Battle for
/// Baldur's Gate, {3}{B}). Oracle:
///   "You may pay {1}{B} rather than pay this spell's mana cost.
///    If the {1}{B} cost was paid, an opponent draws a card.
///    Exile target creature or planeswalker."
///
/// Coverage:
///   * Card identity ({3}{B} Instant, black, dispatch by name).
///   * SpellDefinition shape (1 target creature-or-planeswalker request).
///   * Exile target creature (CR 701.21), no opponent draw when the printed
///     cost was paid (alternativeCostPaid = false).
///   * Exile target planeswalker (CR 701.21).
///   * When the {1}{B} alternative cost was paid, an opponent draws a card
///     (CR 121.1) in addition to the exile.
///   * Illegal target at resolution (target left the battlefield) → exile
///     no-ops, but the opponent-draw rider still fires when the alt cost was
///     paid (the two clauses are independent sentences).
/// </summary>
public class BalefulMasteryFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BalefulMasteryFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Black_AtCost3B()
    {
        var card = BalefulMasteryFactory.Create(_alice);

        card.Name.Should().Be("Baleful Mastery");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{B}");
        CardColors.GetColors(card).Should().Contain(ManaColor.Black,
            "Baleful Mastery has black in its cost {3}{B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsInstantShape()
    {
        var dispatched = NamedCardFactory.Create("Baleful Mastery", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Baleful Mastery");
        dispatched.ManaCost.Should().Be("{3}{B}");
    }

    [Fact]
    public void AlternativeManaCost_Is1B()
    {
        BalefulMasteryFactory.AlternativeManaCost.Should().Be("{1}{B}");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleCreatureOrPlaneswalkerTarget()
    {
        var def = BalefulMasteryFactory.BuildSpellDefinition(
            _alice, o => o, alternativeCostPaid: false);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].Description.Should().Contain("planeswalker");
    }

    // -----------------------------------------------------------------------
    // Exile target — printed cost (no opponent draw)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PrintedCost_ExilesTargetCreature_NoOpponentDraw()
    {
        // Bob controls Grizzly Bears; Bob has a card in his library so we can
        // assert he does NOT draw it (printed cost path).
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        var bobTop = new Card("Lightning Bolt", "{R}");
        bobTop.AddCardType(CardType.Instant);
        bobTop.SetOwner(_bob);
        bobTop.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(bobTop);

        await CastAndResolve(bears, alternativeCostPaid: false);

        bears.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        _bob.Zones.Exile.GetCards().Should().Contain(bears);

        // Printed cost paid → no opponent-draw rider. Bob's library card stays.
        bobTop.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Hand.GetCards().Should().NotContain(bobTop);
    }

    [Fact]
    public async Task PrintedCost_ExilesTargetPlaneswalker()
    {
        var pw = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3)
        { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        await CastAndResolve(pw, alternativeCostPaid: false);

        pw.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(pw);
    }

    // -----------------------------------------------------------------------
    // Alternative cost — an opponent draws a card
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AlternativeCostPaid_ExilesTarget_AndAnOpponentDraws()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        // Bob (the opponent) has a card on top of his library to draw.
        var bobTop = new Card("Lightning Bolt", "{R}");
        bobTop.AddCardType(CardType.Instant);
        bobTop.SetOwner(_bob);
        bobTop.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(bobTop);

        await CastAndResolve(bears, alternativeCostPaid: true);

        // Exile half.
        bears.Zone.Should().Be(ZoneType.Exile);

        // CR 121.1 — the opponent (Bob) drew the top card of his library.
        bobTop.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bobTop);
        _bob.Zones.Library.GetCards().Should().NotContain(bobTop);
    }

    [Fact]
    public async Task AlternativeCostPaid_IllegalTarget_StillMakesOpponentDraw()
    {
        // Target left the battlefield before resolution → exile no-ops, but
        // the opponent-draw rider is an independent sentence and still fires.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bears.SetZone(ZoneType.Graveyard); // already gone from the battlefield
        _bob.Zones.Graveyard.AddCard(bears);

        var bobTop = new Card("Lightning Bolt", "{R}");
        bobTop.AddCardType(CardType.Instant);
        bobTop.SetOwner(_bob);
        bobTop.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(bobTop);

        await CastAndResolve(bears, alternativeCostPaid: true);

        // Exile half no-ops (target not on battlefield).
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Exile.GetCards().Should().NotContain(bears);

        // Opponent-draw rider still fired.
        bobTop.Zone.Should().Be(ZoneType.Hand);
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private async Task CastAndResolve(object target, bool alternativeCostPaid)
    {
        var card = BalefulMasteryFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            BalefulMasteryFactory.BuildSpellDefinition(_alice, t => t, alternativeCostPaid),
            agent, ctx,
            alternativeCost: null);

        card.Zone.Should().Be(ZoneType.Stack);

        _resolver.ResolveTop(_stack);
    }
}

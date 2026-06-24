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
/// End-to-end tests for Refute (Modern Horizons 3, {1}{U}{U}).
/// Oracle (verified against Scryfall):
///   "Counter target spell. Draw a card, then discard a card."
///
/// A vanilla hard counter (Cancel shape — any spell, no rider) stitched to a
/// 1-for-1 loot (Izzet Charm's draw-then-discard body, scaled to one).
///
/// Coverage (unique behaviour only — dispatch + well-formedness are covered by
/// CardFactoryContractTests for every implemented card):
///   * Identity: Instant {1}{U}{U}, blue.
///   * Counters any spell into the graveyard (CR 701.5) AND loots: draws the
///     top of library, then discards a card.
/// </summary>
[Trait("Color", "U")]
public class RefuteFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RefuteFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue_OneUU()
    {
        var card = RefuteFactory.Create(_alice);

        card.Name.Should().Be("Refute");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.Should().Be("{1}{U}{U}");
        card.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public async Task CountersTargetSpell_ThenLoots_DrawsTopAndDiscards()
    {
        var card = RefuteFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // A card already in Alice's hand that will be the deterministic discard.
        var inHand = new Instant("Opt", "{U}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Hand.AddCard(inHand);
        inHand.SetZone(ZoneType.Hand);

        // Top of Alice's library — will be drawn by the loot.
        var topOfLibrary = new Instant("Brainstorm", "{U}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(topOfLibrary);
        topOfLibrary.SetZone(ZoneType.Library);

        // Bob's spell on the stack to be countered.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        // Refute leaves the caster's hand on cast; hand then holds [Opt].
        var handSizeBeforeLoot = 1;

        await _flow.CastAsync(
            _alice, card,
            RefuteFactory.BuildSpellDefinition(_alice, o => o, _stack),
            agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Counter half (CR 701.5).
        bobBolt.Zone.Should().Be(ZoneType.Graveyard, because: "Refute counters the target spell");

        // Loot half — "Draw a card, then discard a card":
        //   * the top of library was drawn off the top (left the library),
        topOfLibrary.Zone.Should().NotBe(ZoneType.Library, because: "Refute drew the top card");
        _alice.Zones.Library.GetCards().Should().NotContain(topOfLibrary);
        //   * exactly one card was discarded to the caster's graveyard (the
        //     drawn card, picked deterministically — prompt deferred). Refute
        //     itself is also in the graveyard now (spell resolved, CR 608.2m),
        //     so the discard is the graveyard delta beyond Refute.
        _alice.Zones.Graveyard.GetCards().Should().Contain(card,
            because: "the resolved Refute spell goes to its owner's graveyard");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2,
            because: "Refute (resolved) plus the one discarded card");
        //   * net hand size is unchanged (drew one, discarded one).
        _alice.Zones.Hand.GetCards().Should().HaveCount(handSizeBeforeLoot,
            because: "draw one then discard one leaves hand size unchanged");
    }
}

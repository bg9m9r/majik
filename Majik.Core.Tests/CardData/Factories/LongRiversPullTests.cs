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
/// End-to-end tests for Long River's Pull (Tarkir: Dragonstorm, {U}{U}).
///
/// Printed oracle (verified against Scryfall):
///   "Gift a card (You may promise an opponent a gift as you cast this spell.
///    If you do, they draw a card before its other effects.)
///    Counter target creature spell. If the gift was promised, instead
///    counter target spell."
///
/// Unique behaviour under test (the gift-conditional counter):
///   * No gift → base mode: counters ONLY a creature spell. A noncreature
///     spell is an illegal target at resolution (CR 608.2b) and survives.
///   * Gift promised → upgraded mode: counters ANY spell, and the recipient
///     draws a card at cast time (the "Gift a card" clause, CR 701.59).
///   * Identity assert (non-vanilla mana cost): Instant, {U}{U}, blue, mv 2.
/// </summary>
[Trait("Color", "U")]
public class LongRiversPullTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public LongRiversPullTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue_TwoManaValue()
    {
        var card = LongRiversPullFactory.Create(_alice);

        card.Name.Should().Be("Long River's Pull");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.Should().Be("{U}{U}");
        card.ManaCostValue.TotalValue.Should().Be(2, because: "two {U} pips");
    }

    [Fact]
    public async Task BaseMode_CountersCreatureSpell()
    {
        var card = LongRiversPullFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob casts a creature spell (Grizzly Bears).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        // No gift promised — ScriptedAgent declines the optional gift.
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            LongRiversPullFactory.BuildDefinition(_alice, _stack, card),
            agent, ctx,
            alternativeCost: null);

        spell.GiftRecipient.Should().BeNull();
        card.HasGiftPromised.Should().BeFalse();

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            because: "base mode counters a creature spell (CR 701.5)");
    }

    [Fact]
    public async Task BaseMode_NoncreatureSpellIsIllegalTarget_Survives()
    {
        var card = LongRiversPullFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob casts a noncreature spell (Lightning Bolt {R}).
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            LongRiversPullFactory.BuildDefinition(_alice, _stack, card),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — base mode only counters creature spells; the bolt is
        // an illegal target and remains on the stack.
        bolt.Zone.Should().NotBe(ZoneType.Graveyard);
        _stack.GetAll().Should().Contain(s => ReferenceEquals(s, bobSpell));
    }

    [Fact]
    public async Task GiftPromised_DrawsCard_AndCountersAnySpell()
    {
        var card = LongRiversPullFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Give Bob a library card so the gift draw has something to pull.
        var libCard = new Instant("Opt", "{U}") { Owner = _bob, Controller = _bob };
        libCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libCard);
        var bobHandBefore = _bob.Zones.Hand.GetCards().Count();

        // Bob casts a NONCREATURE spell — gift mode upgrades to "counter
        // target spell" so this becomes a legal target.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueGiftRecipient(_bob);
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            LongRiversPullFactory.BuildDefinition(_alice, _stack, card),
            agent, ctx,
            alternativeCost: null);

        spell.GiftRecipient.Should().BeSameAs(_bob);
        card.HasGiftPromised.Should().BeTrue();

        // CR 701.59 — "they draw a card" at cast time (engine v1 cast-time
        // gift delivery). Bob drew the gift card before resolution.
        _bob.Zones.Hand.GetCards().Count().Should().Be(bobHandBefore + 1);
        _bob.Zones.Hand.GetCards().Should().Contain(libCard);

        _resolver.ResolveTop(_stack);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "gift mode upgrades to 'counter target spell' — noncreature spells are legal targets (CR 701.5)");
    }
}

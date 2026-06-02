using FluentAssertions;
using Majik.Core.Abilities;
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
/// End-to-end tests for Miscalculation (Urza's Saga, {1}{U}).
/// Oracle (verified against Scryfall):
///   "Counter target spell unless its controller pays {2}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// The exact "soft counter + cycling" shape of Censor, only with different
/// costs ({1}{U} / counter-unless-{2} / cycling {2}).
///
/// Coverage:
///   * Card shape + dispatch by name (Instant {1}{U}, blue).
///   * Counter-unless-pay: success (controller pays {2} → no-op) and failure
///     (no mana → countered into graveyard, CR 701.5).
///   * Cycling {2} activated ability shape (CR 702.32: {2}, Discard self).
///   * Cycling end-to-end: pays {2}, discards self, draws one card,
///     publishes <see cref="CardCycledEvent"/> on the bus (CR 702.32d).
/// </summary>
[Trait("Color", "U")]
public class MiscalculationFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MiscalculationFactoryTests()
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
    public void Create_HasInstantShape_Blue_OneU()
    {
        var card = MiscalculationFactory.Create(_alice);

        card.Name.Should().Be("Miscalculation");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.Should().Be("{1}{U}");
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = MiscalculationFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target spell");
    }

    // -----------------------------------------------------------------------
    // Counter unless pay {2}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Counters_TargetSpell_WhenControllerCannotPayTwo()
    {
        var card = MiscalculationFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob has 0 mana → cannot pay {2} → Miscalculation counters.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            MiscalculationFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {2} so Miscalculation counters his spell");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysTwo()
    {
        var card = MiscalculationFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            MiscalculationFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {2} so Miscalculation is countered into a no-op (CR 118.4)");
    }

    // -----------------------------------------------------------------------
    // Cycling {2} — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelf()
    {
        var card = MiscalculationFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2);
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(2, "cycling {2} charges two generic");
    }

    [Fact]
    public void Cycling_EndToEnd_PaysTwo_DiscardsSelf_DrawsCard_PublishesEvent()
    {
        var topCard = new Instant("Counterspell", "{U}{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var card = MiscalculationFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }

        card.Zone.Should().Be(ZoneType.Graveyard);

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycling drew a card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(card);
        captured.Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Cycling_DiscardSelfCost_FromLibrary_CannotPay()
    {
        var card = MiscalculationFactory.Create(_alice);
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }
}

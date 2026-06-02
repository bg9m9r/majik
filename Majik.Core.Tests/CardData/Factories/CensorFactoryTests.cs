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
/// End-to-end tests for Censor (Amonkhet, {U}).
/// Oracle:
///   "Counter target spell unless its controller pays {1}.
///    Cycling {1}."
///
/// Coverage:
///   * Card shape + dispatch by name (Instant {U}, blue).
///   * Counter-unless-pay: success (controller pays → no-op) and failure
///     (no mana → countered into graveyard, CR 701.5).
///   * Cycling {1} activated ability shape (CR 702.32: {1}, Discard self).
///   * Cycling end-to-end: pays {1}, discards self, draws one card,
///     publishes <see cref="CardCycledEvent"/> on the bus (CR 702.32d).
/// </summary>
[Trait("Color", "U")]
public class CensorFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CensorFactoryTests()
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
    public void Create_HasInstantShape_Blue_U()
    {
        var censor = CensorFactory.Create(_alice);

        censor.Name.Should().Be("Censor");
        censor.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(censor).Should().Contain(ManaColor.Blue);
        censor.ManaCost.Should().Be("{U}");
        censor.ManaCostValue.TotalValue.Should().Be(1);
        censor.Owner.Should().BeSameAs(_alice);
        censor.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = CensorFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target spell");
    }

    // -----------------------------------------------------------------------
    // Counter unless pay {1}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Counters_TargetSpell_WhenControllerCannotPayOne()
    {
        var censor = CensorFactory.Create(_alice);
        censor.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(censor);

        // Bob has 0 mana → cannot pay {1} → Censor counters.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, censor,
            CensorFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {1} so Censor counters his spell");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysOne()
    {
        var censor = CensorFactory.Create(_alice);
        censor.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(censor);

        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, censor,
            CensorFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {1} so Censor is countered into a no-op (CR 118.4)");
    }

    // -----------------------------------------------------------------------
    // Cycling {1} — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void HasCyclingActivatedAbility_WithGenericOneAndDiscardSelf()
    {
        var censor = CensorFactory.Create(_alice);
        var cycling = censor.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2);
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(1, "cycling {1} charges one generic");
    }

    [Fact]
    public void Cycling_EndToEnd_PaysOne_DiscardsSelf_DrawsCard_PublishesEvent()
    {
        var topCard = new Instant("Counterspell", "{U}{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var censor = CensorFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(censor);
        censor.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var cycling = censor.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }

        censor.Zone.Should().Be(ZoneType.Graveyard);

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycling drew a card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(censor);
        captured.Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Cycling_DiscardSelfCost_FromLibrary_CannotPay()
    {
        var censor = CensorFactory.Create(_alice);
        censor.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(censor);

        var cycling = censor.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling activates only from hand");
    }
}

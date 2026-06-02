using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="NimbleObstructionistFactory"/> (Modern Horizons,
/// <c>{2}{U}</c>).
///
/// Oracle text (Scryfall, MH1):
///   "Flash
///    Flying
///    Cycling {2}{U} ({2}{U}, Discard this card: Draw a card.)
///    When you cycle this card, counter target activated or triggered
///    ability you don't control."
///
/// Covers:
/// - Identity ({2}{U} Creature — Bird Wizard 3/1).
/// - Flash + Flying keyword markers.
/// - Cycling {2}{U} activated ability shape (mana + DiscardSelfCost).
/// - On-cycle trigger shape — subscribes to <see cref="CardCycledEvent"/>,
///   gated to <c>ReferenceEquals(e.Card, card)</c> (self-cycle) and
///   functions from the Graveyard (CR 702.32d).
/// - Trigger declares one "target activated or triggered ability you
///   don't control" <see cref="Majik.Core.Players.Agents.TargetRequest"/>.
/// - Resolve: counter a triggered ability an opponent controls
///   (CR 701.5b).
/// - Resolve: counter an activated ability an opponent controls.
/// - Resolve: ability the controller DOES control → no-op ("you don't
///   control" gate).
/// - Resolve: target no longer on the stack → no-op (CR 608.2b).
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "U")]
public class NimbleObstructionistFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public NimbleObstructionistFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_Identity_BirdWizard_3_1_At2U()
    {
        var card = NimbleObstructionistFactory.Create(_alice);

        card.Name.Should().Be("Nimble Obstructionist");
        card.ManaCost.ToString().Should().Be("{2}{U}");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(1);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NimbleObstructionist_HasFlashAndFlying()
    {
        var card = NimbleObstructionistFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_HasCyclingActivatedAbility_TwoGenericOneBlue_PlusDiscardSelf()
    {
        var card = NimbleObstructionistFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling {2}{U} + Discard self");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Blue.Should().Be(1, "cycling {2}{U} charges one blue");
        mana.Generic.Should().Be(2, "cycling {2}{U} charges two generic");
        cycling.TargetRequests.Should().BeEmpty("cycling draws a card — no targets");
    }

    // -----------------------------------------------------------------------
    // On-cycle trigger shape — CR 702.32d over CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_TriggerSubscribesToCardCycledEvent_FromGraveyard()
    {
        var card = NimbleObstructionistFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Should().BeOfType<EventTriggerCondition<CardCycledEvent>>();
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "the on-cycle trigger functions while the cycled card is in the graveyard (CR 702.32d)");
    }

    [Fact]
    public void NimbleObstructionist_Trigger_DeclaresOne_AbilityTarget()
    {
        var card = NimbleObstructionistFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        var req = trigger.TargetRequests[0];
        req.MinTargets.Should().Be(1, "the counter is not optional — it's a mandatory target");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("activated");
        req.Description.Should().Contain("triggered");
    }

    // -----------------------------------------------------------------------
    // "You cycle this card" self-cycle gate
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_TriggerCondition_Fires_OnSelfCycle()
    {
        var card = NimbleObstructionistFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var selfEvent = new CardCycledEvent(card, _alice);
        trigger.Condition.Matches(selfEvent, trigger).Should().BeTrue(
            "Nimble Obstructionist's trigger fires when you cycle THIS card");
    }

    [Fact]
    public void NimbleObstructionist_TriggerCondition_DoesNotFire_OnOtherCardCycle()
    {
        var card = NimbleObstructionistFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var otherCard = new Card("Some Cycler", "");
        var otherEvent = new CardCycledEvent(otherCard, _alice);
        trigger.Condition.Matches(otherEvent, trigger).Should().BeFalse(
            "the trigger is gated to cycling THIS card, not another card");
    }

    // -----------------------------------------------------------------------
    // Counter a triggered ability an opponent controls — CR 701.5b
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_Counters_OpponentTriggeredAbility_OnStack()
    {
        var card = NimbleObstructionistFactory.Create(_alice, _stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var bobSource = new Creature("Bob's Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ranEffect = false;
        var bobTrigger = new TriggeredAbility(
            bobSource,
            _bob,
            Triggers.OnEnterBattlefieldSelf(bobSource),
            effects: new IEffect[] { new Effect("eff", () => ranEffect = true) });
        _stack.Push(bobTrigger);

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobTrigger },
        });

        foreach (var eff in trigger.Effects) eff.Execute();

        _stack.GetAll().Should().NotContain(bobTrigger,
            "the targeted triggered ability is countered and removed from the stack (CR 701.5b)");
        ranEffect.Should().BeFalse("a countered ability's effects never run");
    }

    // -----------------------------------------------------------------------
    // Counter an activated ability an opponent controls
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_Counters_OpponentActivatedAbility_OnStack()
    {
        var card = NimbleObstructionistFactory.Create(_alice, _stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var bobSource = new Creature("Bob's Pinger", "{1}{U}", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ranEffect = false;
        var bobAbility = new ActivatedAbility(
            bobSource,
            _bob,
            effects: new IEffect[] { new Effect("eff", () => ranEffect = true) });
        _stack.Push(bobAbility);

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobAbility },
        });

        foreach (var eff in trigger.Effects) eff.Execute();

        _stack.GetAll().Should().NotContain(bobAbility,
            "the targeted activated ability is countered (CR 701.5b)");
        ranEffect.Should().BeFalse("a countered ability never resolves");
    }

    // -----------------------------------------------------------------------
    // "You don't control" gate — own ability is not a legal target
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_DoesNotCounter_OwnAbility()
    {
        var card = NimbleObstructionistFactory.Create(_alice, _stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var aliceSource = new Creature("Alice's Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var aliceTrigger = new TriggeredAbility(
            aliceSource,
            _alice,
            Triggers.OnEnterBattlefieldSelf(aliceSource),
            effects: new IEffect[] { new Effect("eff", () => { }) });
        _stack.Push(aliceTrigger);

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceTrigger },
        });

        foreach (var eff in trigger.Effects) eff.Execute();

        _stack.GetAll().Should().Contain(aliceTrigger,
            "you can't counter an ability you control — 'you don't control' gate (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Target left the stack before resolution — clean no-op (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_TargetGoneFromStack_IsCleanNoOp()
    {
        var card = NimbleObstructionistFactory.Create(_alice, _stack);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var bobSource = new Creature("Bob's Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var bobTrigger = new TriggeredAbility(
            bobSource,
            _bob,
            Triggers.OnEnterBattlefieldSelf(bobSource),
            effects: new IEffect[] { new Effect("eff", () => { }) });
        // NOT pushed onto the stack — target no longer present.

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobTrigger },
        });

        var act = () => { foreach (var eff in trigger.Effects) eff.Execute(); };
        act.Should().NotThrow("a target that left the stack is a clean no-op (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling publishes CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_Cycling_EndToEnd_PublishesCardCycledEvent()
    {
        var topCard = new Instant("Counterspell", "{U}{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var card = NimbleObstructionistFactory.Create(_alice, stack: null, triggers: null, eventBus: bus);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("2U"));

        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs) cost.Pay(_alice);
        foreach (var effect in cycling.Effects) effect.Execute();

        card.Zone.Should().Be(ZoneType.Graveyard);
        captured.Should().NotBeNull();
        captured!.Card.Should().BeSameAs(card);
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NimbleObstructionist_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Nimble Obstructionist", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Nimble Obstructionist");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }
}

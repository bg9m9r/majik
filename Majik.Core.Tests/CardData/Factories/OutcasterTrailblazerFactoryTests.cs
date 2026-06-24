using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Outcaster Trailblazer (Outlaws of Thunder Junction, {2}{G},
/// Creature — Human Druid 4/2).
///
/// Oracle text (verified against Scryfall):
///   "When this creature enters, add one mana of any color.
///    Whenever another creature you control with power 4 or greater enters,
///    draw a card.
///    Plot {2}{G} (...)"
///
/// Covers:
///   - Identity ({2}{G}, Human Druid 4/2 mono-green, owner / controller).
///   - Both triggers attached structurally on the shape-only path.
///   - ETB add-mana trigger: fills the controller's pool with one mana (Green
///     on the no-agent fallback path — the card's own colour).
///   - Power-4+-enters draw trigger: draws when ANOTHER power-4+ creature you
///     control enters; does NOT draw for a power-3 creature, an opponent's
///     creature, or the Trailblazer's own ETB ("another creature").
///   - Plot (CR 718) deferral guardrail — no activated ability from hand.
/// </summary>
[Trait("Color", "G")]
public class OutcasterTrailblazerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void AddToLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Sorcery($"Lib{i}", "{1}");
            c.SetOwner(p);
            c.SetController(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }

    private static Creature MakeCreature(Player owner, int power, int toughness = 2, string name = "Other")
    {
        var c = new Creature(name, "{G}", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    // The factory attaches the two triggers in printed (oracle) order:
    //   (0) ETB add-mana, (1) another-power-4+-enters draw.
    private static TriggeredAbility ManaTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>().ElementAt(0);

    private static TriggeredAbility DrawTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>().ElementAt(1);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void OutcasterTrailblazer_Identity_HumanDruid_4_2_AtCost2G()
    {
        var c = OutcasterTrailblazerFactory.Create(_alice);

        c.Name.Should().Be("Outcaster Trailblazer");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // {2}{G} carries one green pip — Outcaster Trailblazer is mono-green.
        CardColors.GetColors(c).Should().BeEquivalentTo(new[] { ManaColor.Green });
    }

    [Fact]
    public void OutcasterTrailblazer_HasTwoTriggeredAbilities()
    {
        var c = OutcasterTrailblazerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(2, "the ETB add-mana trigger and the power-4+-enters draw trigger");
    }

    // -----------------------------------------------------------------------
    // ETB add one mana of any color (no-agent fallback = Green)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTrigger_AddsOneManaToPool_GreenOnNoAgentFallback()
    {
        var card = OutcasterTrailblazerFactory.Create(_alice);
        card.SetController(_alice);

        _alice.ManaPool.Total.Should().Be(0);

        // Drive the ETB effect directly (no agent → fallback Green).
        var manaTrigger = ManaTrigger(card);
        foreach (var e in manaTrigger.Effects) e.Execute();

        _alice.ManaPool.Total.Should().Be(1, "the ETB trigger adds one mana of any color");
        _alice.ManaPool.Green.Should().Be(1, "no-agent fallback colour is Green (the card's own colour)");
    }

    // -----------------------------------------------------------------------
    // Another creature you control with power 4+ enters → draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawTrigger_FiresAndDraws_WhenAnotherPower4CreatureYouControlEnters()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = OutcasterTrailblazerFactory.Create(_alice, triggers);
        card.SetController(_alice);
        card.SetZone(ZoneType.Battlefield);

        AddToLibrary(_alice, 3);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        // Another power-4 creature Alice controls enters.
        var ogre = MakeCreature(_alice, power: 4, name: "Big Ogre");
        bus.Publish(new CardMovedEvent(ogre, ZoneType.Hand, ZoneType.Battlefield, _alice));

        triggers.PendingCount.Should().Be(1, "the power-4+ enters trigger fired");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            "another power-4 creature entering draws a card");
    }

    [Fact]
    public void DrawTrigger_DoesNotFire_ForPower3Creature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = OutcasterTrailblazerFactory.Create(_alice, triggers);
        card.SetController(_alice);
        card.SetZone(ZoneType.Battlefield);

        var bear = MakeCreature(_alice, power: 3, name: "Grizzly");
        bus.Publish(new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield, _alice));

        triggers.PendingCount.Should().Be(0, "power 3 is below the power-4-or-greater gate");
    }

    [Fact]
    public void DrawTrigger_DoesNotFire_ForOpponentsCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = OutcasterTrailblazerFactory.Create(_alice, triggers);
        card.SetController(_alice);
        card.SetZone(ZoneType.Battlefield);

        var enemy = MakeCreature(_bob, power: 5, name: "Enemy Giant");
        bus.Publish(new CardMovedEvent(enemy, ZoneType.Hand, ZoneType.Battlefield, _bob));

        triggers.PendingCount.Should().Be(0, "only creatures YOU control trigger the draw");
    }

    [Fact]
    public void DrawTrigger_DoesNotFire_ForOutcasterItself()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = OutcasterTrailblazerFactory.Create(_alice, triggers);
        card.SetController(_alice);

        // Outcaster Trailblazer is itself power 4, but the "ANOTHER creature"
        // clause excludes it — its own entry must not satisfy the draw trigger.
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield, _alice));

        // The draw trigger (1) must not have fired. The ETB add-mana trigger
        // (0) is registered too and DOES match self, so PendingCount may be 1
        // — assert specifically that the DRAW trigger is not the pending one by
        // checking no card was drawn after resolving everything.
        AddToLibrary(_alice, 1);
        var handBefore = _alice.Zones.Hand.GetCards().Count();
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "the Trailblazer's own ETB does not draw — it is not ANOTHER creature");
    }

    // -----------------------------------------------------------------------
    // Plot deferral guardrail (CR 718) — no activated ability from hand.
    // -----------------------------------------------------------------------

    [Fact]
    public void OutcasterTrailblazer_PlotMechanicDeferred_NoActivatedAbilityFromHand()
    {
        var card = OutcasterTrailblazerFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Plot (CR 718) is deferred — no activated-from-hand alt-cost yet");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }
}

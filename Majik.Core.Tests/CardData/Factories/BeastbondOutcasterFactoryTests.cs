using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BeastbondOutcasterFactory"/> (Bloomburrow,
/// {2}{G}).
///
/// Creature — Human Druid 3/3. Oracle text (verified against Scryfall):
///   "When this creature enters, if you control a creature with power 4 or
///    greater, draw a card.
///    Plot {1}{G} (...)"
///
/// Covers ONLY the card's unique behaviour:
/// - Identity (mana cost / P-T / subtypes — non-vanilla stats).
/// - Conditional ETB draw fires (draws) when the controller controls a
///   creature with power >= 4.
/// - Conditional ETB draw is suppressed (no draw) when the controller controls
///   no creature with power >= 4 (intervening-if, CR 603.4) — including a
///   lone Outcaster (its own 3/3 base power is below the threshold).
/// - Plot (CR 718) is deferred — no activated ability is wired.
/// </summary>
[Trait("Color", "G")]
public class BeastbondOutcasterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static Creature NewVanillaCreatureOnBattlefield(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, "", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void BeastbondOutcaster_Identity_HumanDruid_3_3_AtCost2G()
    {
        var card = BeastbondOutcasterFactory.Create(_alice);

        card.Name.Should().Be("Beastbond Outcaster");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------
    // Conditional ETB draw
    // -------------------------------------------------------------------

    [Fact]
    public void Etb_WithControlledPower4Creature_DrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Alice already controls a power-4 creature.
        NewVanillaCreatureOnBattlefield(_alice, "Big Beast", 4, 4);

        var card = BeastbondOutcasterFactory.Create(_alice, eventBus: bus, triggers: triggers);
        var top = NewCardInLibrary(_alice, "TopCard");

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 603.6a + 603.4 — condition met, draw a card.
        _alice.Zones.Hand.GetCards().Should().Contain(top);
    }

    [Fact]
    public void Etb_WithNoPower4Creature_DoesNotDraw()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Alice controls only a small creature (power 3 < 4). The Outcaster's
        // own base power (3) is likewise below the threshold.
        NewVanillaCreatureOnBattlefield(_alice, "Small Beast", 3, 3);

        var card = BeastbondOutcasterFactory.Create(_alice, eventBus: bus, triggers: triggers);
        var top = NewCardInLibrary(_alice, "TopCard");

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield));

        // The trigger still goes on the stack (CR 603.4 — the trigger's "if"
        // is re-checked on resolution, not at trigger time)...
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // ...but the intervening-if is false on resolution, so no draw.
        _alice.Zones.Hand.GetCards().Should().NotContain(top);
        _alice.Zones.Library.GetCards().Should().Contain(top);
    }

    // -------------------------------------------------------------------
    // Plot deferral guardrail — ships without Plot activation (CR 718).
    // -------------------------------------------------------------------

    [Fact]
    public void BeastbondOutcaster_PlotMechanicDeferred_NoActivatedAbility()
    {
        var card = BeastbondOutcasterFactory.Create(_alice);

        // No activated abilities are wired — Plot is deferred. The only
        // ability on the card is the conditional ETB draw trigger.
        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
}

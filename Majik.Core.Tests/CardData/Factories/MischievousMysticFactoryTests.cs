using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Mischievous Mystic (Wilds of Eldraine, {1}{U}). Creature —
/// Human Wizard 2/1. Oracle text (verified against Scryfall):
///   "Flying
///    Whenever you draw your second card each turn, create a 1/1 blue Faerie
///    creature token with flying."
///
/// Covers (this card's unique behaviour — the contract test already asserts
/// dispatch + well-formedness):
///   - Card identity (name, type, subtypes, P/T, mana cost) + Flying.
///   - One triggered ability present.
///   - Mechanic: the controller's FIRST draw each turn does not trigger; the
///     SECOND does → a 1/1 blue flying Faerie token is created. A third+ draw
///     does not retrigger. Opponents' draws never trigger.
///   - Mechanic: a TurnStartedEvent resets the per-turn count so the next
///     turn's second draw triggers again.
/// </summary>
[Trait("Color", "U")]
public class MischievousMysticFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static (Majik.Core.Stack.Stack stack, TriggerManager triggers, EventBus bus) BuildEngine()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (stack, triggers, bus);
    }

    [Fact]
    public void Identity_HumanWizard_2_1_AtCost1U_WithFlying()
    {
        var m = MischievousMysticFactory.Create(_alice);

        m.Name.Should().Be("Mischievous Mystic");
        m.ManaCost.Should().Be("{1}{U}");
        m.HasType(CardType.Creature).Should().BeTrue();
        m.HasSubtype(CardSubtype.Human).Should().BeTrue();
        m.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        m.BasePower.Should().Be(2);
        m.BaseToughness.Should().Be(1);
        CombatAbilities.HasFlying(m).Should().BeTrue();
        m.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ControllerFirstDraw_DoesNotTrigger()
    {
        var (_, triggers, bus) = BuildEngine();

        var m = MischievousMysticFactory.Create(_alice, bus, triggers);
        m.SetZone(ZoneType.Battlefield);

        var a1 = NewCardInLibrary(_alice, "A1");

        // Alice's first draw of the turn — no trigger.
        bus.Publish(new CardDrawnEvent(a1, _alice));

        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void ControllerSecondDraw_Triggers_CreatesBlueFlyingFaerieToken()
    {
        var (stack, triggers, bus) = BuildEngine();

        var m = MischievousMysticFactory.Create(_alice, bus, triggers);
        m.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(m);

        var a1 = NewCardInLibrary(_alice, "A1");
        var a2 = NewCardInLibrary(_alice, "A2");

        // First draw — no trigger.
        bus.Publish(new CardDrawnEvent(a1, _alice));
        triggers.PendingCount.Should().Be(0);

        // Second draw — Mischievous Mystic triggers.
        bus.Publish(new CardDrawnEvent(a2, _alice));
        triggers.PendingCount.Should().Be(1, "the controller's second draw triggers Mischievous Mystic");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var faeries = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Faerie))
            .ToList();
        faeries.Should().HaveCount(1);

        var faerie = faeries[0];
        faerie.BasePower.Should().Be(1);
        faerie.BaseToughness.Should().Be(1);
        CombatAbilities.HasFlying(faerie).Should().BeTrue("the Faerie token has flying");
        CardColors.GetColors(faerie).Should().Equal(new[] { ManaColor.Blue }, "the Faerie token is blue");
    }

    [Fact]
    public void ControllerThirdDraw_DoesNotRetrigger()
    {
        var (stack, triggers, bus) = BuildEngine();

        var m = MischievousMysticFactory.Create(_alice, bus, triggers);
        m.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(m);

        var a1 = NewCardInLibrary(_alice, "A1");
        var a2 = NewCardInLibrary(_alice, "A2");
        var a3 = NewCardInLibrary(_alice, "A3");

        bus.Publish(new CardDrawnEvent(a1, _alice)); // no trigger
        bus.Publish(new CardDrawnEvent(a2, _alice)); // triggers
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bus.Publish(new CardDrawnEvent(a3, _alice)); // must not retrigger
        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void OpponentDraws_NeverTrigger()
    {
        var (_, triggers, bus) = BuildEngine();

        var m = MischievousMysticFactory.Create(_alice, bus, triggers);
        m.SetZone(ZoneType.Battlefield);

        var b1 = NewCardInLibrary(_bob, "B1");
        var b2 = NewCardInLibrary(_bob, "B2");
        var b3 = NewCardInLibrary(_bob, "B3");

        // Bob (the opponent) draws several cards — "you draw" never matches an
        // opponent, so no trigger.
        bus.Publish(new CardDrawnEvent(b1, _bob));
        bus.Publish(new CardDrawnEvent(b2, _bob));
        bus.Publish(new CardDrawnEvent(b3, _bob));

        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void TurnBoundary_ResetsCount_NextTurnSecondDrawTriggersAgain()
    {
        var (stack, triggers, bus) = BuildEngine();

        var m = MischievousMysticFactory.Create(_alice, bus, triggers);
        m.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(m);

        var a1 = NewCardInLibrary(_alice, "A1");
        var a2 = NewCardInLibrary(_alice, "A2");
        var a3 = NewCardInLibrary(_alice, "A3");
        var a4 = NewCardInLibrary(_alice, "A4");

        // Turn 1 — Alice's second draw triggers.
        bus.Publish(new CardDrawnEvent(a1, _alice));
        bus.Publish(new CardDrawnEvent(a2, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Turn boundary — reset the per-turn count (CR 500.1).
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        // Turn 2 — first draw no longer counts as the "second".
        bus.Publish(new CardDrawnEvent(a3, _alice));
        triggers.PendingCount.Should().Be(0);

        bus.Publish(new CardDrawnEvent(a4, _alice));
        triggers.PendingCount.Should().Be(1);
    }
}

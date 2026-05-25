using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="VengevineFactory"/> (Rise of the Eldrazi, {2}{G}{G}).
///
/// Covers:
///   - Identity (Plant Elemental 4/3, {2}{G}{G}, owner/controller, Haste).
///   - NamedCardFactory dispatch.
///   - Creature-cast trigger fires on the controller's SECOND creature spell
///     this turn while Vengevine is in the graveyard (CR 603.6d), returns
///     the card to the battlefield.
///   - Trigger does NOT fire on a non-creature spell, on the first creature
///     spell, or on the third+ creature spells in the same turn.
///   - Trigger does NOT fire when an opponent casts the creature spells.
///   - TurnStartedEvent resets the per-turn count so a fresh "second creature
///     spell" gate fires next turn.
/// </summary>
public class VengevineTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Vengevine_Identity_PlantElemental_4_3_AtCost2GG()
    {
        var card = VengevineFactory.Create(_alice);

        card.Name.Should().Be("Vengevine");
        card.ManaCost.Should().Be("{2}{G}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Vengevine_HasHaste_AndCreatureCastTrigger()
    {
        var card = VengevineFactory.Create(_alice);

        CombatAbilities.HasHaste(card).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one creature-cast trigger attached (CR 603.6d)");
    }

    [Fact]
    public void Vengevine_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Vengevine", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Vengevine");
        card.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Second-creature-spell trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void FirstCreatureSpell_DoesNotTrigger()
    {
        var (zones, bus, stack, triggers) = BuildEngine();
        var card = VengevineFactory.Create(_alice, zones, bus, triggers, agent: null);
        SeatInGraveyard(card);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "C1")));

        triggers.PendingCount.Should().Be(0);
        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void SecondCreatureSpell_Triggers_ReturnsVengevineToBattlefield()
    {
        var (zones, bus, stack, triggers) = BuildEngine();
        var card = VengevineFactory.Create(_alice, zones, bus, triggers, agent: null);
        SeatInGraveyard(card);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "C1")));
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "C2")));

        triggers.PendingCount.Should().Be(1, "second creature spell queues the return trigger");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        card.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);
    }

    [Fact]
    public void NonCreatureSpell_DoesNotCountTowardSecondGate()
    {
        var (zones, bus, _, triggers) = BuildEngine();
        var card = VengevineFactory.Create(_alice, zones, bus, triggers, agent: null);
        SeatInGraveyard(card);

        // An instant + a creature spell — only the creature spell counts.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "I1")));
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "C1")));

        triggers.PendingCount.Should().Be(0,
            "instant spells don't increment the creature-spell count");
        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void ThirdCreatureSpell_DoesNotRetrigger_OnlySecondFires()
    {
        var (zones, bus, stack, triggers) = BuildEngine();
        var card = VengevineFactory.Create(_alice, zones, bus, triggers, agent: null);
        SeatInGraveyard(card);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "C1")));
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "C2")));

        // Resolve the second-spell trigger and pull Vengevine onto the battlefield.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Third creature spell — Vengevine is now on the battlefield so the
        // graveyard-resident trigger is inactive anyway, but also the count
        // is past 2 so the predicate would reject. Either way, no new trigger.
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "C3")));
        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void OpponentCreatureSpells_DoNotIncrementControllerCount()
    {
        var bob = new Player("Bob", 20);
        var (zones, bus, _, triggers) = BuildEngine();
        var card = VengevineFactory.Create(_alice, zones, bus, triggers, agent: null);
        SeatInGraveyard(card);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(bob, "B1")));
        bus.Publish(new SpellCastEvent(NewCreatureSpell(bob, "B2")));
        triggers.PendingCount.Should().Be(0);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "A1")));
        triggers.PendingCount.Should().Be(0,
            "opponent spells didn't bump Alice's per-turn count");

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "A2")));
        triggers.PendingCount.Should().Be(1,
            "Alice's own second creature spell triggers Vengevine");
    }

    [Fact]
    public void TurnBoundary_ResetsCount_NextTurnSecondSpellTriggersAgain()
    {
        var (zones, bus, stack, triggers) = BuildEngine();
        var card = VengevineFactory.Create(_alice, zones, bus, triggers, agent: null);
        SeatInGraveyard(card);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "T1C1")));
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "T1C2")));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        card.Zone.Should().Be(ZoneType.Battlefield);

        // Send Vengevine back to the graveyard for a second round.
        zones.MoveCard(card, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        card.Zone.Should().Be(ZoneType.Graveyard);

        // Reset the per-turn count.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "T2C1")));
        triggers.PendingCount.Should().Be(0);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "T2C2")));
        triggers.PendingCount.Should().Be(1,
            "next turn's second creature spell re-arms the return trigger");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SeatInGraveyard(Creature card)
    {
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name)
    {
        var creature = new Creature(name, "{G}", 1, 1) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name)
    {
        var instant = new Instant(name, "U") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static (ZoneService zones, EventBus bus, MajikStack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new MajikStack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, bus, stack, triggers);
    }
}

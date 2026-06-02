using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Abbot of Keral Keep (Magic Origins, {1}{R},
/// Creature — Human Monk 2/1). Oracle text (verified against Scryfall):
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    When this creature enters, exile the top card of your library. Until
///    end of turn, you may play that card."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Two triggered abilities: Prowess + ETB impulse.
///   - Prowess: casting a noncreature spell → +1/+1 EOT (CR 702.108).
///   - Prowess: casting a creature spell → no pump.
///   - ETB impulse: exiles top card of library + stamps the
///     may-play-from-exile grant (CR 603.6a / 701.20 / 118.9).
///   - ETB impulse on an empty library is a no-op (CR 701.20).
/// </summary>
[Trait("Color", "R")]
public class AbbotOfKeralKeepFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    private static Card NewCardInLibrary(Player owner, string name)
    {
        ICard c = new Card(name, "R");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return (Card)c;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Abbot_Identity_HumanMonk_2_1_AtCost1R()
    {
        var card = AbbotOfKeralKeepFactory.Create(_alice);

        card.Name.Should().Be("Abbot of Keral Keep");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Abbot_HasTwoTriggeredAbilities_ProwessAndEtb()
    {
        var card = AbbotOfKeralKeepFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Abbot_EtbTrigger_IsBattlefieldActive()
    {
        var card = AbbotOfKeralKeepFactory.Create(_alice);

        var etb = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Abbot of Keral Keep")));
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // Prowess (CR 702.108)
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_PumpsPlus1Plus1EOT()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = AbbotOfKeralKeepFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 514.2 / Layer 7c — prowess +1/+1 until end of turn. Abbot is 2/1
        // so the pump takes it to 3/2.
        card.Power.Should().Be(3);
        card.Toughness.Should().Be(2);
    }

    [Fact]
    public void CastingCreatureSpell_NoProwessPump()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = AbbotOfKeralKeepFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB impulse (CR 603.6a / 701.20 / 118.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_ExilesTopOfLibrary_AndGrantsPlay()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = AbbotOfKeralKeepFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice, "Shock");

        var etb = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Abbot of Keral Keep")));
        foreach (var e in etb.Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Exile, "the top card is exiled (CR 701.20)");
        _alice.Zones.Exile.GetCards().Should().Contain(top);
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the controller may play the exiled card until end of turn (CR 118.9)");
    }

    [Fact]
    public void Etb_EmptyLibrary_IsNoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = AbbotOfKeralKeepFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // No cards in Alice's library.
        var etb = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Abbot of Keral Keep")));

        // Exiling from an empty library finds nothing — no throw, no grant.
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow("an exile that finds nothing is a no-op (CR 701.20)");
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }
}

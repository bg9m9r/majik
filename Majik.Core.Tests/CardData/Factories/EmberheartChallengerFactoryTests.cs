using System.Linq;
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
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Emberheart Challenger (Bloomburrow, {1}{R},
/// Creature — Mouse Warrior 2/2). Oracle text (verified against Scryfall):
///   "Haste
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    Valiant — Whenever this creature becomes the target of a spell or
///    ability you control for the first time each turn, exile the top card
///    of your library. Until end of turn, you may play that card."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - Haste keyword marker attached.
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Prowess: casting a noncreature spell → +1/+1 EOT (CR 702.108).
///   - Valiant: the controller's own spell targeting Emberheart exiles the
///     top card of library + stamps the may-play-from-exile grant.
///   - Valiant once-per-turn cap: a second targeting the same turn does NOT
///     re-trigger; a turn boundary resets it.
///   - Valiant does NOT trigger off a spell/ability an opponent controls.
/// </summary>
[Trait("Color", "R")]
public class EmberheartChallengerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

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

    private static Majik.Core.Spells.Spell NewSpellTargeting(
        Player controller, Creature target, string name = "Boon")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller, new[] { Target.Permanent(target) });
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
    public void Emberheart_Identity_MouseWarrior_2_2_AtCost1R()
    {
        var card = EmberheartChallengerFactory.Create(_alice);

        card.Name.Should().Be("Emberheart Challenger");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mouse).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Emberheart_HasHasteKeywordMarker()
    {
        var card = EmberheartChallengerFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Haste");
    }
    [Fact]
    public void Emberheart_HasTwoTriggeredAbilities_ProwessAndValiant()
    {
        var card = EmberheartChallengerFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
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

        var card = EmberheartChallengerFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 514.2 / Layer 7c — prowess +1/+1 until end of turn.
        card.Power.Should().Be(3);
        card.Toughness.Should().Be(3);
    }

    [Fact]
    public void CastingCreatureSpell_NoProwessPump()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = EmberheartChallengerFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Valiant (CR 603.6c, 115.6 — first target each turn)
    // -----------------------------------------------------------------------

    [Fact]
    public void Valiant_OwnSpellTargetsEmberheart_ExilesTop_AndGrantsPlay()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = EmberheartChallengerFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice, "Shock");

        // Alice's own spell targets Emberheart — Valiant triggers.
        var spell = NewSpellTargeting(_alice, card, "Giant Growth");
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "Valiant triggers when Emberheart becomes the target of a spell its controller controls");

        var valiant = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Valiant")));
        foreach (var e in valiant.Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Exile, "the top card is exiled");
        _alice.Zones.Exile.GetCards().Should().Contain(top);
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the controller may play the exiled card until end of turn");
    }

    [Fact]
    public void Valiant_SecondTargetSameTurn_DoesNotRetrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = EmberheartChallengerFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "C1");
        NewCardInLibrary(_alice, "C2");

        var spell1 = NewSpellTargeting(_alice, card, "S1");
        bus.Publish(new TargetsChosenEvent(spell1, spell1.Targets));

        var spell2 = NewSpellTargeting(_alice, card, "S2");
        bus.Publish(new TargetsChosenEvent(spell2, spell2.Targets));

        triggers.PendingCount.Should().Be(1,
            "Valiant only triggers the FIRST time each turn (CR 603.2 / 603.3)");
    }

    [Fact]
    public void Valiant_TurnBoundary_ResetsFirstTargetCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = EmberheartChallengerFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "T1");
        NewCardInLibrary(_alice, "T2");

        var spell1 = NewSpellTargeting(_alice, card, "S1");
        bus.Publish(new TargetsChosenEvent(spell1, spell1.Targets));
        triggers.PendingCount.Should().Be(1);

        // New turn — reset the once-per-turn counter (CR 500.1).
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        var spell2 = NewSpellTargeting(_alice, card, "S2");
        bus.Publish(new TargetsChosenEvent(spell2, spell2.Targets));
        triggers.PendingCount.Should().Be(2,
            "after the turn boundary the next target re-triggers Valiant");
    }

    [Fact]
    public void Valiant_OpponentsSpellTargetingEmberheart_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = EmberheartChallengerFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "Shock");

        // Bob targets Emberheart — "you control" fails (CR — spell controller
        // must be Emberheart's controller).
        var spell = NewSpellTargeting(_bob, card, "Bob's Bolt");
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Valiant only triggers off a spell or ability YOU control");
    }
}

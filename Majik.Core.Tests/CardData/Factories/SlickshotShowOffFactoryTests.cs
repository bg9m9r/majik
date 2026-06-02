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
/// Tests for Slickshot Show-Off (Outlaws of Thunder Junction, {1}{R},
/// Creature — Human Mercenary Jock 1/1).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - Flying + Haste keyword markers attached.
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Casting a noncreature spell → +3/+0 EOT (power = 4, toughness = 1).
///   - Casting a creature spell → no pump.
///   - Two noncreature casts in one turn → +6/+0 stacks (power = 7).
///   - Plot (CR 718) is deferred — documented gap, no plot activation surface.
/// </summary>
[Trait("Color", "R")]
public class SlickshotShowOffFactoryTests
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

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SlickshotShowOff_Identity_HumanMercenaryJock_1_1_AtCost1R()
    {
        var card = SlickshotShowOffFactory.Create(_alice);

        card.Name.Should().Be("Slickshot Show-Off");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mercenary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Jock).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SlickshotShowOff_HasFlyingAndHasteKeywordMarkers()
    {
        var card = SlickshotShowOffFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Haste");
    }
    [Fact]
    public void SlickshotShowOff_HasOneCastTriggeredAbility()
    {
        var card = SlickshotShowOffFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Cast-noncreature pump
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_PumpsPlus3Plus0EOT()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = SlickshotShowOffFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 514.2 / Layer 7c — +3/+0 until end of turn.
        card.Power.Should().Be(4);
        card.Toughness.Should().Be(1);
    }

    [Fact]
    public void CastingCreatureSpell_NoPump()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = SlickshotShowOffFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        card.Power.Should().Be(1);
        card.Toughness.Should().Be(1);
    }

    [Fact]
    public void CastingMultipleNoncreatureSpells_PumpStacksAdditively()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = SlickshotShowOffFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        // First noncreature cast.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Bolt #1")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        card.Power.Should().Be(4);

        // Second noncreature cast — second PumpUntilEndOfTurnEffect stacks
        // additively per CR 613 (multiple Layer 7c effects all apply).
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Bolt #2")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        card.Power.Should().Be(7);

        // Third for good measure.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Bolt #3")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        card.Power.Should().Be(10);

        // Toughness was unaffected by the +0 toughness portion across all
        // three pumps.
        card.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Plot deferral guardrail — Slickshot ships without Plot activation
    // (CR 718). This test pins the gap so future Plot wiring (a new
    // activated-from-hand "pay {R}, exile with plot marker" + a sorcery-
    // speed-on-a-later-turn cast-from-exile permission) is observable as
    // a behavioral change.
    // -----------------------------------------------------------------------

    [Fact]
    public void SlickshotShowOff_PlotMechanicDeferred_NoActivatedAbilityFromHand()
    {
        var card = SlickshotShowOffFactory.Create(_alice);

        // No activated abilities are wired on the card today — Plot (the
        // only printed activated ability) is deferred. The only abilities
        // on the card are the two keyword markers (Flying + Haste) and the
        // cast-noncreature pump trigger.
        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        card.Abilities.OfType<KeywordAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
}

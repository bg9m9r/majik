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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Stormchaser Mage (Oath of the Gatewatch,
/// Creature — Human Wizard {(U/R)}{(U/R)} 1/3).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost,
///     owner/controller).
///   - Hybrid mana cost parses as two HybridPips (CR 107.4e).
///   - Ability set: Flying + Haste + Prowess KeywordAbility markers on
///     the shape-only path; Prowess TriggeredAbility added on the fully-
///     wired path.
///   - NamedCardFactory dispatch returns a Stormchaser Mage shape.
///   - Casting a noncreature spell pumps Stormchaser Mage +1/+1 EOT
///     (Prowess, CR 702.108).
///   - Casting a creature spell does NOT pump.
///   - End-of-turn cleanup expires the pump.
/// </summary>
public class StormchaserMageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Lightning Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bears")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void StormchaserMage_Identity_HumanWizard_1_3_AtHybridCost()
    {
        var s = StormchaserMageFactory.Create(_alice);

        s.Name.Should().Be("Stormchaser Mage");
        s.ManaCost.Should().Be("{U/R}{U/R}");
        s.HasType(CardType.Creature).Should().BeTrue();
        s.HasSubtype(CardSubtype.Human).Should().BeTrue();
        s.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        s.BasePower.Should().Be(1);
        s.BaseToughness.Should().Be(3);
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StormchaserMage_ManaCost_ParsesAsTwoHybridPips()
    {
        // CR 107.4e — each {U/R} pip decomposes into one HybridPip.
        // Stormchaser Mage prints two hybrid pips for a CMC of 2.
        var parsed = ManaCost.Parse(StormchaserMageFactory.PrintedManaCost);

        parsed.HybridPips.Should().HaveCount(2,
            "{U/R}{U/R} decomposes into two HybridPip entries");
        parsed.TotalValue.Should().Be(2,
            "two hybrid pips contribute 2 to the converted mana cost");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StormchaserMage()
    {
        var card = NamedCardFactory.Create("Stormchaser Mage", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Stormchaser Mage");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability set — keyword markers + trigger wiring
    // -----------------------------------------------------------------------

    [Fact]
    public void StormchaserMage_HasFlyingKeywordMarker()
    {
        var s = StormchaserMageFactory.Create(_alice);

        s.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Flying",
                "Stormchaser Mage has Flying (CR 702.9)");
    }

    [Fact]
    public void StormchaserMage_HasHasteKeywordMarker()
    {
        var s = StormchaserMageFactory.Create(_alice);

        s.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Haste",
                "Stormchaser Mage has Haste (CR 702.10)");
    }

    [Fact]
    public void StormchaserMage_HasProwessKeywordMarker()
    {
        var s = StormchaserMageFactory.Create(_alice);

        s.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Prowess",
                "Stormchaser Mage has Prowess (CR 702.108)");
    }

    [Fact]
    public void StormchaserMage_ShapeOnly_NoTriggeredAbility()
    {
        // Single-arg path — Prowess trigger is NOT wired without an
        // effects service (matches Monastery Swiftspear / Monastery Mentor
        // shape-only stance).
        var s = StormchaserMageFactory.Create(_alice);
        s.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void StormchaserMage_FullyWired_HasProwessTriggeredAbility()
    {
        var effects = new ContinuousEffectsService();
        var s = StormchaserMageFactory.Create(_alice, eventBus: null, triggers: null, effects: effects);

        s.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the prowess trigger is the only triggered ability");
    }

    // -----------------------------------------------------------------------
    // Prowess pump on noncreature spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_PumpsStormchaserMagePlusOnePlusOneEndOfTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var s = StormchaserMageFactory.Create(_alice, bus, triggers, effects);
        s.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Prowess: Stormchaser Mage is 2/4 until end of turn (CR 702.108
        // / Layer 7c).
        s.Power.Should().Be(2);
        s.Toughness.Should().Be(4);

        // End-of-turn cleanup expires the pump (CR 514.2).
        effects.ExpireEndOfTurn();
        s.Power.Should().Be(1);
        s.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // No pump on creature spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotPumpStormchaserMage()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var s = StormchaserMageFactory.Create(_alice, bus, triggers, effects);
        s.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        s.Power.Should().Be(1);
        s.Toughness.Should().Be(3);
    }
}

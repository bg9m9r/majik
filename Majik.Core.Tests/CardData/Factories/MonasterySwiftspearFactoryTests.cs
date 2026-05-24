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
/// Tests for Monastery Swiftspear (Khans of Tarkir + many reprints,
/// Creature — Human Monk {R} 1/2).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost,
///     owner/controller).
///   - Ability set: Haste + Prowess KeywordAbility markers attached on
///     the shape-only path; Prowess TriggeredAbility added on the
///     fully-wired path.
///   - NamedCardFactory dispatch returns a Monastery Swiftspear shape.
///   - Casting a noncreature spell pumps Swiftspear +1/+1 EOT.
///   - Casting a creature spell does NOT pump.
///   - End-of-turn cleanup expires the pump.
/// </summary>
public class MonasterySwiftspearFactoryTests
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
    public void MonasterySwiftspear_Identity_HumanMonk_1_2_AtCostR()
    {
        var s = MonasterySwiftspearFactory.Create(_alice);

        s.Name.Should().Be("Monastery Swiftspear");
        s.ManaCost.Should().Be("{R}");
        s.HasType(CardType.Creature).Should().BeTrue();
        s.HasSubtype(CardSubtype.Human).Should().BeTrue();
        s.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        s.BasePower.Should().Be(1);
        s.BaseToughness.Should().Be(2);
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MonasterySwiftspear()
    {
        var card = NamedCardFactory.Create("Monastery Swiftspear", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Monastery Swiftspear");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Monk).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability set — keyword markers + trigger wiring
    // -----------------------------------------------------------------------

    [Fact]
    public void MonasterySwiftspear_HasHasteKeywordMarker()
    {
        var s = MonasterySwiftspearFactory.Create(_alice);

        s.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Haste",
                "Monastery Swiftspear has Haste (CR 702.10)");
    }

    [Fact]
    public void MonasterySwiftspear_HasProwessKeywordMarker()
    {
        var s = MonasterySwiftspearFactory.Create(_alice);

        s.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Prowess",
                "Monastery Swiftspear has Prowess (CR 702.108)");
    }

    [Fact]
    public void MonasterySwiftspear_ShapeOnly_NoTriggeredAbility()
    {
        // Single-arg path — Prowess trigger is NOT wired without an
        // effects service (matches Monastery Mentor's shape-only stance).
        var s = MonasterySwiftspearFactory.Create(_alice);
        s.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void MonasterySwiftspear_FullyWired_HasProwessTriggeredAbility()
    {
        var effects = new ContinuousEffectsService();
        var s = MonasterySwiftspearFactory.Create(_alice, eventBus: null, triggers: null, effects: effects);

        s.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the prowess trigger is the only triggered ability");
    }

    // -----------------------------------------------------------------------
    // Prowess pump on noncreature spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_PumpsSwiftspearPlusOnePlusOneEndOfTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var s = MonasterySwiftspearFactory.Create(_alice, bus, triggers, effects);
        s.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Prowess: Swiftspear is 2/3 until end of turn (CR 702.108 / Layer 7c).
        s.Power.Should().Be(2);
        s.Toughness.Should().Be(3);

        // End-of-turn cleanup expires the pump (CR 514.2).
        effects.ExpireEndOfTurn();
        s.Power.Should().Be(1);
        s.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // No pump on creature spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotPumpSwiftspear()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var s = MonasterySwiftspearFactory.Create(_alice, bus, triggers, effects);
        s.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        s.Power.Should().Be(1);
        s.Toughness.Should().Be(2);
    }
}

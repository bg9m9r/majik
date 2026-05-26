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
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Stormchaser Mage (Oath of the Gatewatch, {U}{R}).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch returns the correct shape.
///   - Flying + Haste KeywordAbility markers attach.
///   - Prowess wiring: with effects service supplied, casting a noncreature
///     spell registers a layer-7c +1/+1 ContinuousEffect on Stormchaser
///     (CR 702.108).
///   - Creature-spell cast does NOT trigger prowess (predicate excludes
///     creature spells).
///   - Opponent's cast does NOT trigger Stormchaser's prowess.
/// </summary>
public class StormchaserMageTests
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

    [Fact]
    public void StormchaserMage_Identity_HumanWizard_1_2_AtCostUR()
    {
        var sm = StormchaserMageFactory.Create(_alice);

        sm.Name.Should().Be("Stormchaser Mage");
        sm.ManaCost.Should().Be("{U}{R}");
        sm.HasType(CardType.Creature).Should().BeTrue();
        sm.HasSubtype(CardSubtype.Human).Should().BeTrue();
        sm.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        sm.BasePower.Should().Be(1);
        sm.BaseToughness.Should().Be(2);
        sm.Owner.Should().BeSameAs(_alice);
        sm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StormchaserMage_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Stormchaser Mage", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Stormchaser Mage");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void StormchaserMage_HasFlyingAndHaste()
    {
        var sm = StormchaserMageFactory.Create(_alice);

        var keywords = sm.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Haste");
    }

    [Fact]
    public void StormchaserMage_ShapeOnlyOverload_HasNoProwessTrigger()
    {
        // Without an effects service supplied, prowess does NOT wire — only
        // Flying + Haste markers attach. Same posture as Soul-Scar Mage's
        // shape-only Create(Player) overload.
        var sm = StormchaserMageFactory.Create(_alice);
        sm.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void StormchaserMage_WiredOverload_RegistersProwessTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var sm = StormchaserMageFactory.Create(_alice, effects, bus, triggers);

        sm.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "ProwessFactory.Build attaches exactly one trigger");
    }

    [Fact]
    public void CastingInstant_PumpsStormchaserPlus1Plus1_UntilEndOfTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var sm = StormchaserMageFactory.Create(_alice, effects, bus, triggers);
        _alice.Zones.Battlefield.AddCard(sm);
        sm.SetZone(ZoneType.Battlefield);

        // Cast an instant — prowess should fire and pump Stormchaser +1/+1.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1, "prowess triggers on a noncreature spell");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Apply layer-7c continuous effects and read computed P/T.
        var chars = effects.Compute(sm);
        chars.Power.Should().Be(2, "1 base + 1 prowess pump");
        chars.Toughness.Should().Be(3, "2 base + 1 prowess pump");
    }

    [Fact]
    public void CastingCreatureSpell_DoesNotTriggerProwess()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var sm = StormchaserMageFactory.Create(_alice, effects, bus, triggers);
        _alice.Zones.Battlefield.AddCard(sm);
        sm.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));
        triggers.PendingCount.Should().Be(0, "prowess gates on noncreature spells only");
    }

    [Fact]
    public void OpponentCastingNoncreature_DoesNotTriggerStormchaserProwess()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var sm = StormchaserMageFactory.Create(_alice, effects, bus, triggers);
        _alice.Zones.Battlefield.AddCard(sm);
        sm.SetZone(ZoneType.Battlefield);

        // Bob casts an instant — Stormchaser's prowess should NOT fire
        // ("Whenever YOU cast..." — controller-gated, CR 702.108).
        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));
        triggers.PendingCount.Should().Be(0);
    }
}

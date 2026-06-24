using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="StormcatchMentorFactory"/> (Secrets of Strixhaven
/// Commander, {U}{R}, Creature — Otter Wizard 1/1).
///
/// Covers only Stormcatch Mentor's unique body (CardFactoryContractTests
/// already asserts dispatch + well-formedness for every implemented card):
///   - Identity: {U}{R}, Otter + Wizard, 1/1.
///   - Haste keyword (CR 702.10).
///   - Prowess (CR 702.108): noncreature cast → +1/+1 EOT; creature cast →
///     no pump; opponent's cast → no pump.
///   - Spell-cost reduction rider (CR 117.7): instant/sorcery you cast cost
///     {1} less; creature spell untouched.
/// </summary>
[Trait("Color", "M")]
public class StormcatchMentorFactoryTests
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

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void StormcatchMentor_Identity_OtterWizard_1_1_AtCostUR()
    {
        var c = StormcatchMentorFactory.Create(_alice);

        c.Name.Should().Be("Stormcatch Mentor");
        c.ManaCost.Should().Be("{U}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Otter).Should().BeTrue("Otter is a printed subtype");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Wizard is a printed subtype");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Haste (CR 702.10)
    // -----------------------------------------------------------------------

    [Fact]
    public void StormcatchMentor_HasHasteKeyword()
    {
        var c = StormcatchMentorFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Haste").Should().BeTrue("Stormcatch Mentor has Haste");
    }

    // -----------------------------------------------------------------------
    // Prowess (CR 702.108)
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_PumpsProwess()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var mentor = StormcatchMentorFactory.Create(_alice, effects, triggers);
        mentor.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        // Prowess is the only trigger.
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 702.108 / Layer 7c — 1/1 base becomes 2/2 until end of turn.
        mentor.Power.Should().Be(2);
        mentor.Toughness.Should().Be(2);
    }

    [Fact]
    public void CastingCreatureSpell_NoProwessPump()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var mentor = StormcatchMentorFactory.Create(_alice, effects, triggers);
        mentor.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        // Creature spell — prowess does not fire (CR 702.108 "noncreature").
        triggers.PendingCount.Should().Be(0);
        mentor.Power.Should().Be(1);
        mentor.Toughness.Should().Be(1);
    }

    [Fact]
    public void OpponentCastingNoncreatureSpell_NoProwessPump()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var mentor = StormcatchMentorFactory.Create(_alice, effects, triggers);
        mentor.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        // "Whenever YOU cast" — opponent casts don't trigger.
        triggers.PendingCount.Should().Be(0);
        mentor.Power.Should().Be(1);
        mentor.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Spell-cost reduction rider (CR 117.7)
    // -----------------------------------------------------------------------

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void InstantCast_GenericReducedByOne()
    {
        var mentor = StormcatchMentorFactory.Create(_alice);
        PutOnBattlefield(_alice, mentor);

        var cancel = new Instant("Cancel", "{1}{U}{U}");
        cancel.SetOwner(_alice);
        cancel.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(cancel, _alice);

        effective.Generic.Should().Be(0, "{1} generic reduced by 1 → {0}");
        effective.Blue.Should().Be(2, "coloured pips untouched (CR 117.7c)");
    }

    [Fact]
    public void SorceryCast_GenericReducedByOne()
    {
        var mentor = StormcatchMentorFactory.Create(_alice);
        PutOnBattlefield(_alice, mentor);

        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);
        sorcery.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(sorcery, _alice);

        effective.Generic.Should().Be(1, "{2} generic reduced by 1 → {1}");
        effective.Black.Should().Be(1, "coloured pips untouched (CR 117.7c)");
    }

    [Fact]
    public void CreatureCast_NoReduction()
    {
        var mentor = StormcatchMentorFactory.Create(_alice);
        PutOnBattlefield(_alice, mentor);

        var creature = new Creature("Test Beast", "{2}{G}", power: 3, toughness: 3);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(creature, _alice);

        effective.Generic.Should().Be(2, "creature spell — no discount (instant/sorcery only)");
        effective.Green.Should().Be(1);
    }
}

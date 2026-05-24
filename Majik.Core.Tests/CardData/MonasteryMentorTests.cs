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
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Monastery Mentor (Fate Reforged, {2}{W}, Creature — Human Monk 2/2).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Casting a noncreature spell → Prowess (+1/+1 EOT) fires AND 1/1 Monk
///     token is created.
///   - Casting a creature spell → no Prowess pump, no token.
///   - Opponent casting a noncreature spell → no pump, no token for Alice.
/// </summary>
public class MonasteryMentorTests
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
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MonasteryMentor_Identity_HumanMonk_2_2_AtCost2W()
    {
        var mm = MonasteryMentorFactory.Create(_alice);

        mm.Name.Should().Be("Monastery Mentor");
        mm.ManaCost.Should().Be("{2}{W}");
        mm.HasType(CardType.Creature).Should().BeTrue();
        mm.HasSubtype(CardSubtype.Human).Should().BeTrue();
        mm.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        mm.BasePower.Should().Be(2);
        mm.BaseToughness.Should().Be(2);
        mm.Owner.Should().BeSameAs(_alice);
        mm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MonasteryMentor()
    {
        var card = NamedCardFactory.Create("Monastery Mentor", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Monastery Mentor");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Monk).Should().BeTrue();
    }

    [Fact]
    public void MonasteryMentor_ShapeOnly_HasOneTriggeredAbility()
    {
        // Single-arg path — only the token trigger is attached (no prowess
        // wiring without a ContinuousEffectsService).
        var mm = MonasteryMentorFactory.Create(_alice);
        mm.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void MonasteryMentor_FullyWired_HasTwoTriggeredAbilities()
    {
        // Fully-wired path — prowess trigger AND token trigger both attached.
        var effects = new ContinuousEffectsService();
        var mm = MonasteryMentorFactory.Create(_alice, eventBus: null, triggers: null, effects: effects);
        mm.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Noncreature spell → Prowess pump + Monk token
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_PumpsProwessAndCreatesMonkToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var mm = MonasteryMentorFactory.Create(_alice, bus, triggers, effects);
        mm.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        // Two triggers pending: prowess + token.
        triggers.PendingCount.Should().Be(2);

        // Resolve both triggers.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        stack.Pop()!.Resolve();

        // Prowess: Mentor is 3/3 until end of turn (CR 702.108 / Layer 7c).
        mm.Power.Should().Be(3);
        mm.Toughness.Should().Be(3);

        // Token trigger: one 1/1 Monk token on the battlefield.
        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(battlefieldBefore + 1);
        var token = battlefield.OfType<Creature>().Last();
        token.IsToken.Should().BeTrue();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Creature spell → no pump, no token
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_NoPumpNoToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var mm = MonasteryMentorFactory.Create(_alice, bus, triggers, effects);
        mm.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);

        // P/T is unmodified.
        mm.Power.Should().Be(2);
        mm.Toughness.Should().Be(2);

        // No token produced.
        _alice.Zones.Battlefield.GetCards().Should().HaveCount(battlefieldBefore);
    }

    // -----------------------------------------------------------------------
    // Opponent's cast does not trigger either ability
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastingNoncreatureSpell_NoPumpNoToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var mm = MonasteryMentorFactory.Create(_alice, bus, triggers, effects);
        mm.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
        mm.Power.Should().Be(2);
        mm.Toughness.Should().Be(2);
    }
}

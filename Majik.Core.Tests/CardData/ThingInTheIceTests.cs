using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Thing in the Ice // Awoken Horror — transform-DFC front face
/// (Shadows over Innistrad, {1}{U}, Creature — Horror 0/4).
///
/// Oracle (verified against Scryfall):
///   Thing in the Ice — "Defender
///    This creature enters with four ice counters on it.
///    Whenever you cast an instant or sorcery spell, remove an ice counter
///    from this creature. Then if it has no ice counters on it, transform it."
///   Awoken Horror (7/8) — "When this creature transforms into Awoken Horror,
///    return all non-Horror creatures to their owners' hands."
///
/// Covers:
///   - Card identity (name, type, subtype Horror, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Defender keyword marker present (CR 702.3).
///   - Enters with four ice counters (CR 122.1).
///   - MdfcState attached with correct front / back face names (CR 711).
///   - One triggered ability present on the card.
///   - Casting an instant removes one ice counter (no transform yet).
///   - Casting a creature spell does not trigger.
///   - Opponent casting an instant does not trigger for the controller.
///   - Four instant/sorcery casts drain the counters and transform.
///   - Transform into Awoken Horror bounces all non-Horror creatures, but
///     leaves Horror creatures (and Awoken Horror itself) on the battlefield.
/// </summary>
public class ThingInTheIceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private List<Player> AllPlayers => new() { _alice, _bob };

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Lava")
    {
        var sorcery = new Sorcery(name, "1R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
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
    public void ThingInTheIce_Identity_Horror_0_4_AtCost1U()
    {
        var thing = ThingInTheIceFactory.Create(_alice);

        thing.Name.Should().Be("Thing in the Ice");
        thing.ManaCost.Should().Be("{1}{U}");
        thing.HasType(CardType.Creature).Should().BeTrue();
        thing.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        thing.BasePower.Should().Be(0);
        thing.BaseToughness.Should().Be(4);
        thing.Owner.Should().BeSameAs(_alice);
        thing.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ThingInTheIce()
    {
        var card = NamedCardFactory.Create("Thing in the Ice // Awoken Horror", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Thing in the Ice");
        card.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ThingInTheIce_HasDefenderKeyword()
    {
        var thing = ThingInTheIceFactory.Create(_alice);

        // CR 702.3 — Defender keyword marker; surfaced for block legality.
        CombatAbilities.HasDefender(thing).Should().BeTrue();
    }

    [Fact]
    public void ThingInTheIce_EntersWithFourIceCounters()
    {
        var thing = ThingInTheIceFactory.Create(_alice);

        // CR 122.1 — "This creature enters with four ice counters on it."
        thing.Counters.Count(ThingInTheIceFactory.IceCounter).Should().Be(4);
    }

    [Fact]
    public void ThingInTheIce_HasMdfcStateOnFrontFace()
    {
        var thing = ThingInTheIceFactory.Create(_alice);

        thing.MdfcState.Should().NotBeNull("transform DFC must carry an MdfcState (CR 711)");
        thing.MdfcState!.FrontFaceName.Should().Be("Thing in the Ice");
        thing.MdfcState.BackFaceName.Should().Be("Awoken Horror");
        thing.MdfcState.IsBackFace.Should().BeFalse("starts on the front face");
        thing.MdfcState.ActiveFaceName.Should().Be("Thing in the Ice");
    }

    [Fact]
    public void ThingInTheIce_HasOneTriggeredAbility()
    {
        var thing = ThingInTheIceFactory.Create(_alice);
        thing.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Cast trigger — counter removal
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_RemovesOneIceCounter_NoTransformYet()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var thing = ThingInTheIceFactory.Create(_alice, triggers, () => AllPlayers);
        _alice.Zones.Battlefield.AddCard(thing);
        thing.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 122.3 — one ice counter removed; three remain, no transform.
        thing.Counters.Count(ThingInTheIceFactory.IceCounter).Should().Be(3);
        thing.MdfcState!.IsBackFace.Should().BeFalse(
            "three ice counters remain, so it does not transform yet");
    }

    [Fact]
    public void CastingCreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var thing = ThingInTheIceFactory.Create(_alice, triggers, () => AllPlayers);
        _alice.Zones.Battlefield.AddCard(thing);
        thing.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0,
            "a creature spell is neither an instant nor a sorcery");
        thing.Counters.Count(ThingInTheIceFactory.IceCounter).Should().Be(4);
    }

    [Fact]
    public void OpponentCastingInstant_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var thing = ThingInTheIceFactory.Create(_alice, triggers, () => AllPlayers);
        _alice.Zones.Battlefield.AddCard(thing);
        thing.SetZone(ZoneType.Battlefield);

        // Bob casts the instant — "you" is Alice, so no trigger.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Opt")));

        triggers.PendingCount.Should().Be(0,
            "the trigger only fires on the controller's own instant/sorcery casts");
        thing.Counters.Count(ThingInTheIceFactory.IceCounter).Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // Transform + Awoken Horror mass-bounce
    // -----------------------------------------------------------------------

    [Fact]
    public void FourInstantSorceryCasts_DrainCounters_AndTransform()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var thing = ThingInTheIceFactory.Create(_alice, triggers, () => AllPlayers);
        _alice.Zones.Battlefield.AddCard(thing);
        thing.SetZone(ZoneType.Battlefield);

        for (var i = 0; i < 4; i++)
        {
            bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, $"Bolt{i}")));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        // CR 701.28 — fourth removal empties the counters and transforms.
        thing.Counters.Count(ThingInTheIceFactory.IceCounter).Should().Be(0);
        thing.MdfcState!.IsBackFace.Should().BeTrue(
            "removing the last ice counter transforms into Awoken Horror");
        thing.MdfcState.ActiveFaceName.Should().Be("Awoken Horror");
    }

    [Fact]
    public void TransformIntoAwokenHorror_BouncesAllNonHorrorCreatures()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var thing = ThingInTheIceFactory.Create(_alice, triggers, () => AllPlayers);
        _alice.Zones.Battlefield.AddCard(thing);
        thing.SetZone(ZoneType.Battlefield);

        // A non-Horror creature on Alice's side and on Bob's side — both bounce.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear }) { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        bear.SetController(_alice);

        var goblin = new Creature("Goblin Guide", "R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin }) { Owner = _bob };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);
        goblin.SetController(_bob);

        // A Horror creature — NOT bounced.
        var spellskite = new Creature("Spellskite", "2", 0, 4,
            subtypes: new[] { CardSubtype.Horror }) { Owner = _bob };
        _bob.Zones.Battlefield.AddCard(spellskite);
        spellskite.SetZone(ZoneType.Battlefield);
        spellskite.SetController(_bob);

        // Drain the four counters to force the transform.
        for (var i = 0; i < 4; i++)
        {
            bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, $"Lava{i}")));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        thing.MdfcState!.IsBackFace.Should().BeTrue("transformed into Awoken Horror");

        // CR 701.10 — all non-Horror creatures returned to owners' hands.
        bear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        goblin.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(goblin);

        // Horror creatures stay — Spellskite and Thing/Awoken Horror itself.
        spellskite.Zone.Should().Be(ZoneType.Battlefield);
        thing.Zone.Should().Be(ZoneType.Battlefield,
            "Awoken Horror is a Horror, so its own trigger never bounces it");
    }
}

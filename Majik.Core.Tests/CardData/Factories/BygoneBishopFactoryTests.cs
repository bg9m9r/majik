using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BygoneBishopFactory"/>.
///
/// Covers:
/// - Identity (Creature — Spirit Cleric 2/3 at {2}{W}, owner /
///   controller wired).
/// - Flying keyword marker.
/// - NamedCardFactory dispatch.
/// - Single cast-trigger attached.
/// - Trigger fires on creature spells with mv ≤ 3 cast by the
///   controller (Investigate → Clue token).
/// - Trigger does NOT fire on creature spells with mv ≥ 4.
/// - Trigger does NOT fire on noncreature spells (instants /
///   sorceries / pure enchantments / lands).
/// - Trigger does NOT fire on opponent's casts.
/// </summary>
public class BygoneBishopFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BygoneBishop_Identity()
    {
        var c = BygoneBishopFactory.Create(_alice);

        c.Name.Should().Be("Bygone Bishop");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BygoneBishop_HasFlying()
    {
        var c = BygoneBishopFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying");
    }

    [Fact]
    public void BygoneBishop_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Bygone Bishop", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Bygone Bishop");
        ((Creature)c).BasePower.Should().Be(2);
        ((Creature)c).BaseToughness.Should().Be(3);
    }

    [Fact]
    public void BygoneBishop_AttachesSingleCastTrigger()
    {
        var c = BygoneBishopFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Investigate cast-trigger only");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "cast trigger only active while Bishop is on the battlefield (CR 603.6a)");
    }

    [Fact]
    public void BygoneBishop_Trigger_Fires_OnMv3CreatureCastByController()
    {
        var bishop = BygoneBishopFactory.Create(_alice);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        // Alice casts a mv-3 creature spell.
        var mv3Creature = new Creature("Knight of the White Orchid", "{W}{W}{W}", 2, 2);
        mv3Creature.SetOwner(_alice);
        mv3Creature.SetController(_alice);

        var spell = new Majik.Core.Spells.Spell(mv3Creature, _alice);

        var trigger = bishop.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition.Should().NotBeNull();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeTrue("mv-3 creature spell cast by controller fires the Investigate trigger");
    }

    [Fact]
    public void BygoneBishop_Trigger_Fires_OnMv1CreatureCastByController()
    {
        var bishop = BygoneBishopFactory.Create(_alice);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        var mv1Creature = new Creature("Champion of the Parish", "{W}", 1, 1);
        mv1Creature.SetOwner(_alice);
        mv1Creature.SetController(_alice);

        var spell = new Majik.Core.Spells.Spell(mv1Creature, _alice);

        var trigger = bishop.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeTrue();
    }

    [Fact]
    public void BygoneBishop_Trigger_DoesNotFire_OnMv4Creature()
    {
        var bishop = BygoneBishopFactory.Create(_alice);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        var mv4Creature = new Creature("Siege Rhino", "{1}{W}{B}{G}", 4, 5);
        mv4Creature.SetOwner(_alice);
        mv4Creature.SetController(_alice);

        var spell = new Majik.Core.Spells.Spell(mv4Creature, _alice);

        var trigger = bishop.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeFalse("mv-4 creature exceeds the printed mv ≤ 3 cap");
    }

    [Fact]
    public void BygoneBishop_Trigger_DoesNotFire_OnInstant()
    {
        var bishop = BygoneBishopFactory.Create(_alice);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        var instant = new Instant("Lightning Bolt", "{R}");
        instant.SetOwner(_alice);
        instant.SetController(_alice);

        var spell = new Majik.Core.Spells.Spell(instant, _alice);

        var trigger = bishop.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeFalse("noncreature spell — printed 'creature spell' filter");
    }

    [Fact]
    public void BygoneBishop_Trigger_DoesNotFire_OnOpponentCast()
    {
        var bishop = BygoneBishopFactory.Create(_alice);
        bishop.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bishop);

        var bobCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);

        var spell = new Majik.Core.Spells.Spell(bobCreature, _bob);

        var trigger = bishop.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeFalse("'whenever YOU cast' — opponent's casts don't fire");
    }
}

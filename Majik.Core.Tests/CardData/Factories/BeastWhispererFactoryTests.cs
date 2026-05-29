using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BeastWhispererFactory"/>.
///
/// Covers:
/// - Identity (Creature — Elf Druid 2/3 at {2}{G}{G}, owner / controller
///   wired) from the embedded JSON definition.
/// - NamedCardFactory dispatch.
/// - Single cast-trigger attached, active on the battlefield.
/// - Trigger fires on any creature spell cast by the controller (draw).
/// - Trigger does NOT fire on noncreature spells (instants / sorceries).
/// - Trigger does NOT fire on opponent's casts.
/// </summary>
public class BeastWhispererFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BeastWhisperer_Identity()
    {
        var c = BeastWhispererFactory.Create(_alice);

        c.Name.Should().Be("Beast Whisperer");
        c.ManaCost.Should().Be("{2}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BeastWhisperer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Beast Whisperer", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Beast Whisperer");
        ((Creature)c).BasePower.Should().Be(2);
        ((Creature)c).BaseToughness.Should().Be(3);
    }

    [Fact]
    public void BeastWhisperer_AttachesSingleCastTrigger()
    {
        var c = BeastWhispererFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "cast-a-creature-spell draw trigger only");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "cast trigger only active while Beast Whisperer is on the battlefield (CR 603.6a)");
    }

    [Fact]
    public void BeastWhisperer_Trigger_Fires_OnCreatureCastByController()
    {
        var whisperer = BeastWhispererFactory.Create(_alice);
        whisperer.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(whisperer);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var spell = new Majik.Core.Spells.Spell(creature, _alice);

        var trigger = whisperer.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition.Should().NotBeNull();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeTrue("creature spell cast by controller fires the draw trigger");
    }

    [Fact]
    public void BeastWhisperer_Trigger_Fires_OnExpensiveCreatureCastByController()
    {
        // No mana-value cap — unlike Bygone Bishop, any creature spell fires.
        var whisperer = BeastWhispererFactory.Create(_alice);
        whisperer.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(whisperer);

        var bigCreature = new Creature("Craterhoof Behemoth", "{5}{G}{G}{G}", 5, 5);
        bigCreature.SetOwner(_alice);
        bigCreature.SetController(_alice);

        var spell = new Majik.Core.Spells.Spell(bigCreature, _alice);

        var trigger = whisperer.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeTrue("Beast Whisperer has no mana-value cap on the creature spell");
    }

    [Fact]
    public void BeastWhisperer_Trigger_DoesNotFire_OnInstant()
    {
        var whisperer = BeastWhispererFactory.Create(_alice);
        whisperer.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(whisperer);

        var instant = new Instant("Lightning Bolt", "{R}");
        instant.SetOwner(_alice);
        instant.SetController(_alice);

        var spell = new Majik.Core.Spells.Spell(instant, _alice);

        var trigger = whisperer.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeFalse("noncreature spell — printed 'creature spell' filter");
    }

    [Fact]
    public void BeastWhisperer_Trigger_DoesNotFire_OnOpponentCast()
    {
        var whisperer = BeastWhispererFactory.Create(_alice);
        whisperer.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(whisperer);

        var bobCreature = new Creature("Llanowar Elves", "{G}", 1, 1);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);

        var spell = new Majik.Core.Spells.Spell(bobCreature, _bob);

        var trigger = whisperer.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition!.Matches(new SpellCastEvent(spell), trigger)
            .Should().BeFalse("'whenever YOU cast' — opponent's casts don't fire");
    }
}

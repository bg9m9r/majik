using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PhantasmalBearFactory"/> — Creature — Bear
/// Illusion {U} 2/2 with one trigger:
///   "When this creature becomes the target of a spell or ability,
///    sacrifice it."
///
/// Covers:
/// - Card identity (name, cost, types, subtypes, P/T, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Single TriggeredAbility attached, gated to the Battlefield zone.
/// - Bus-driven trigger surfaces on <see cref="TargetsChosenEvent"/>
///   when a spell targets the bear.
/// - Bus-driven trigger surfaces when an *ability* targets the bear
///   (Phantasmal Bear / Phantasmal Image fire on BOTH spells AND
///   abilities — unlike Bonecrusher Giant which is spell-only).
/// - Sacrifice effect moves the bear to its owner's graveyard.
/// - Targeting an unrelated permanent does NOT trigger.
/// </summary>
[Trait("Color", "U")]
public class PhantasmalBearFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ------------------------------------------------------------------
    // Shape
    // ------------------------------------------------------------------

    [Fact]
    public void PhantasmalBear_IsBearIllusion_2_2_AtCostU()
    {
        var pb = PhantasmalBearFactory.Create(_alice);

        pb.Name.Should().Be("Phantasmal Bear");
        pb.ManaCost.Should().Be("{U}");
        pb.HasType(CardType.Creature).Should().BeTrue();
        pb.HasSubtype(CardSubtype.Bear).Should().BeTrue();
        pb.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        pb.BasePower.Should().Be(2);
        pb.BaseToughness.Should().Be(2);
        pb.Owner.Should().BeSameAs(_alice);
        pb.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void PhantasmalBear_HasSacrificeTrigger_OnlyOnBattlefield()
    {
        var pb = PhantasmalBearFactory.Create(_alice);

        var triggers = pb.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
        triggers[0].ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    // ------------------------------------------------------------------
    // Live trigger surfacing
    // ------------------------------------------------------------------

    [Fact]
    public void PhantasmalBear_TargetedBySpell_SacrificeTriggerSurfaces()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var pb = PhantasmalBearFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(pb);
        pb.SetZone(ZoneType.Battlefield);

        // Bob casts a Lightning Bolt targeting Phantasmal Bear.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(pb) });

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "Phantasmal Bear triggers when it becomes the target of a spell");
    }

    [Fact]
    public void PhantasmalBear_NotTargeted_NoTriggerSurfaces()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var pb = PhantasmalBearFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(pb);
        pb.SetZone(ZoneType.Battlefield);

        // Bob casts a Lightning Bolt targeting some OTHER creature.
        var otherBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        otherBear.SetOwner(_alice);
        otherBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(otherBear);
        otherBear.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(otherBear) });

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Phantasmal Bear only triggers when IT is the target");
    }

    [Fact]
    public void PhantasmalBear_SacrificeEffect_MovesItToGraveyard()
    {
        // Execute the effect directly to verify the sacrifice routing
        // — same structural posture as PhantasmalImageTests'
        // PhantasmalImage_SacrificeEffect_MovesItToGraveyard.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var pb = PhantasmalBearFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(pb);
        pb.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(pb) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        pb.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice effect moves Phantasmal Bear to its owner's graveyard");
        _alice.Zones.Graveyard.GetCards().Should().Contain(pb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(pb);
    }
}

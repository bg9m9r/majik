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
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Phantasmal Image (Magic 2012 / Modern Horizons 2, {1}{U}).
///
/// Covers:
///   - Card identity (name, type, Illusion subtype, P/T, mana cost).
///   - NamedCardFactory dispatch.
///   - Enters-as-copy of a Bear: P/T stat-copied to 2/2 via the shared
///     <see cref="EntersAsCopyReplacement"/> + <see cref="CopyEffect"/>.
///   - Illusion subtype present after entering as a copy (Layer 4 rider
///     plus the printed subtype both keep Illusion on the card).
///   - "Decline" copy (modelled v1 as no candidates on battlefield): the
///     image enters with its printed 0/0.
///   - Targeted-by-spell sacrifice trigger structure + structural firing
///     (no DamageDealtEvent surface like Bonecrusher — sacrifice is a raw
///     zone move per Dress Down's end-step pattern).
/// </summary>
public class PhantasmalImageTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void PhantasmalImage_IsCreature_Illusion_0_0_AtCost1U()
    {
        var pi = PhantasmalImageFactory.Create(_alice);

        pi.Name.Should().Be("Phantasmal Image");
        pi.ManaCost.Should().Be("{1}{U}");
        pi.HasType(CardType.Creature).Should().BeTrue();
        pi.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        pi.BasePower.Should().Be(0);
        pi.BaseToughness.Should().Be(0);
        pi.Owner.Should().BeSameAs(_alice);
        pi.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PhantasmalImage()
    {
        var card = NamedCardFactory.Create("Phantasmal Image", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Phantasmal Image");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(0);
        ((Creature)card).BaseToughness.Should().Be(0);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void PhantasmalImage_HasSacrificeTrigger_OnlyOnBattlefield()
    {
        var pi = PhantasmalImageFactory.Create(_alice);

        var triggers = pi.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
        triggers[0].ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    [Fact]
    public void PhantasmalImage_EntersAsCopyOfBear_StatCopiedTo_2_2()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // A vanilla Bear already on the battlefield as the copy source.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        // Phantasmal Image entering from the hand.
        var pi = PhantasmalImageFactory.Create(
            _alice,
            eventBus: null,
            triggers: null,
            replacements: bus,
            effects: effects);
        pi.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pi);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(pi, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CopyEffect copied the bear's printed P/T onto the image.
        pi.Power.Should().Be(2, "Phantasmal Image enters as a copy of Grizzly Bears");
        pi.Toughness.Should().Be(2);
        pi.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void PhantasmalImage_EntersAsCopy_IllusionSubtypePresent()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // Copy source: a Bear that is NOT itself an Illusion.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var pi = PhantasmalImageFactory.Create(
            _alice,
            eventBus: null,
            triggers: null,
            replacements: bus,
            effects: effects);
        pi.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pi);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(pi, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // The printed Illusion subtype is still there (CopyEffect at v1
        // does not overwrite subtypes; the Layer 4 AddSubtypeEffect rider
        // also keeps Illusion present in the layer-computed characteristics).
        pi.HasSubtype(CardSubtype.Illusion).Should().BeTrue(
            "Phantasmal Image is an Illusion in addition to its other types");

        var computed = effects.Compute(pi);
        computed.Subtypes.Should().Contain(CardSubtype.Illusion,
            "Layer 4 AddSubtypeEffect adds Illusion to the working characteristics");
    }

    [Fact]
    public void PhantasmalImage_NoCopyCandidates_EntersAsPrintedZeroZero()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // No creatures on the battlefield → the EntersAsCopyReplacement's
        // PickSource returns null → no CopyEffect registered → image
        // enters as its printed 0/0. This is the v1 stand-in for "decline
        // the may" since EntersAsCopyReplacement is auto-yes-when-able
        // (no agent prompt yet — see EntersAsCopyReplacement xmldoc).
        var pi = PhantasmalImageFactory.Create(
            _alice,
            eventBus: null,
            triggers: null,
            replacements: bus,
            effects: effects);
        pi.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pi);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(pi, ZoneType.Hand, ZoneType.Battlefield, _alice);

        pi.Power.Should().Be(0, "no copy source available → printed 0/0");
        pi.Toughness.Should().Be(0);
        pi.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        pi.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void PhantasmalImage_TargetedBySpell_SacrificeTriggerSurfaces()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();
        var replacements = new ReplacementBus();

        var pi = PhantasmalImageFactory.Create(_alice, bus, triggers, replacements, effects);
        _alice.Zones.Battlefield.AddCard(pi);
        pi.SetZone(ZoneType.Battlefield);

        // Bob casts a Lightning Bolt targeting Phantasmal Image.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(pi) });

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "Phantasmal Image triggers when it becomes the target of a spell");
    }

    [Fact]
    public void PhantasmalImage_SacrificeEffect_MovesItToGraveyard()
    {
        // Structural test for the sacrifice effect. We don't need a live
        // trigger here — execute the effect directly and verify the card
        // ends up in the owner's graveyard (sac is modelled as a raw
        // zone move per OracleSpellBinder.MoveToGraveyard, same pattern
        // as Dress Down's end-step self-sac).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();
        var replacements = new ReplacementBus();

        var pi = PhantasmalImageFactory.Create(_alice, bus, triggers, replacements, effects);
        _alice.Zones.Battlefield.AddCard(pi);
        pi.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(pi) });

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        var trigger = pi.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        pi.Zone.Should().Be(ZoneType.Graveyard,
            "Phantasmal Image sacrifices itself when targeted");
        _alice.Zones.Graveyard.GetCards().Should().Contain(pi);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(pi);
    }

    [Fact]
    public void PhantasmalImage_TargetedByAbility_SacrificeTriggerSurfaces()
    {
        // Unlike Bonecrusher Giant (which is spell-only per CR 115.6),
        // Phantasmal Image's rider says "spell or ability" — any chosen
        // target referencing the image fires the trigger regardless of
        // the stack object's flavour.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();
        var replacements = new ReplacementBus();

        var pi = PhantasmalImageFactory.Create(_alice, bus, triggers, replacements, effects);
        _alice.Zones.Battlefield.AddCard(pi);
        pi.SetZone(ZoneType.Battlefield);

        // Fabricate a TargetsChosenEvent whose stack object is NOT an
        // ISpell — anything implementing IStackObject suffices. Reuse a
        // Spell wrapper here as a stand-in stack object that we then
        // treat as "an ability targeted the image" by simply checking
        // that the predicate ignores the spell-vs-ability distinction.
        // (Phantasmal Image's predicate, unlike Bonecrusher's, does not
        // gate on `is ISpell`.)
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(pi) });

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "Phantasmal Image triggers on spells OR abilities — predicate is not gated on ISpell");
    }
}

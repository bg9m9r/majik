using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HammerOfNazahnFactory"/>.
///
/// Covers:
/// - Identity (Legendary Artifact Equipment, {4}).
/// - NamedCardFactory dispatch.
/// - Equip {3} activated ability shape.
/// - Static +2/+0 boost via <see cref="AttachedBoostEffect"/>.
/// - Indestructible grant on equipped creature (live via
///   <see cref="ContinuousEffectsService"/>; printed marker on the
///   hammer itself in the shape-only path).
/// - Equipment-ETB trigger present + auto-attaches another Equipment to
///   the first controller creature.
/// </summary>
public class HammerOfNazahnTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HammerOfNazahn_Identity()
    {
        var h = HammerOfNazahnFactory.Create(_alice);

        h.Name.Should().Be("Hammer of Nazahn");
        h.ManaCost.Should().Be("{4}");
        h.HasType(CardType.Artifact).Should().BeTrue();
        h.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        h.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        h.Owner.Should().BeSameAs(_alice);
        h.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HammerOfNazahn_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Hammer of Nazahn", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Hammer of Nazahn");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip {3}
    // -----------------------------------------------------------------------

    [Fact]
    public void HammerOfNazahn_EquipAbility_HasGenericThreeCost()
    {
        var h = HammerOfNazahnFactory.Create(_alice);

        var equip = h.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.EquipCost.Generic.Should().Be(3, "printed Equip {3}");
    }

    // -----------------------------------------------------------------------
    // Static boost + Indestructible grant
    // -----------------------------------------------------------------------

    [Fact]
    public void HammerOfNazahn_Equipped_Bear_Becomes_4_2_AndIndestructible()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var hammer = HammerOfNazahnFactory.Create(_alice, svc, triggers: null);
        hammer.Zone = ZoneType.Battlefield;

        hammer.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+0 boost");
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasIndestructible(bear).Should().BeTrue(
            "Hammer of Nazahn grants Indestructible to the equipped creature");
    }

    [Fact]
    public void HammerOfNazahn_Detach_RestoresPT_AndIndestructibleLapses()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var hammer = HammerOfNazahnFactory.Create(_alice, svc, triggers: null);
        hammer.Zone = ZoneType.Battlefield;
        hammer.AttachTo(bear);

        // Sanity
        bear.GetPower().Should().Be(4);
        CombatAbilities.HasIndestructible(bear).Should().BeTrue();

        hammer.Unattach();

        bear.GetPower().Should().Be(2, "boost lapses on detach");
        CombatAbilities.HasIndestructible(bear).Should().BeFalse(
            "Indestructible grant is revoked when no longer attached");
    }

    [Fact]
    public void HammerOfNazahn_ShapeOnly_HammerCarriesIndestructibleMarker()
    {
        var hammer = HammerOfNazahnFactory.Create(_alice);

        // Shape-only path: Indestructible marker lives on the hammer card
        // itself so factory-shape tests can observe the keyword. With a
        // ContinuousEffectsService wired the grant projects onto the
        // equipped creature instead (see the Equipped_Bear test).
        hammer.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => string.Equals(
                k.Keyword, "Indestructible", System.StringComparison.OrdinalIgnoreCase),
                "shape-only path attaches the Indestructible marker to the hammer");
    }

    // -----------------------------------------------------------------------
    // ETB-equipment trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void HammerOfNazahn_HasEtbEquipmentTrigger()
    {
        var hammer = HammerOfNazahnFactory.Create(_alice);

        hammer.Abilities.OfType<TriggeredAbility>().Should().NotBeEmpty(
            "Hammer of Nazahn has a 'whenever an Equipment enters' trigger");
    }

    [Fact]
    public void HammerOfNazahn_EtbTrigger_AutoAttachesEnteringEquipmentToBear()
    {
        // Direct-effect smoke test: locate the trigger, prime the closure
        // by firing the predicate once with a CardMovedEvent for a fresh
        // Bonesplitter, then resolve the effect. Bypasses TriggerManager
        // wiring — same posture as the shape-only equipment tests.
        var svc = new ContinuousEffectsService();
        var hammer = HammerOfNazahnFactory.Create(_alice, svc, triggers: null);
        hammer.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(hammer);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var bonesplitter = new Artifact("Bonesplitter", "{1}",
            subtypes: new[] { CardSubtype.Equipment });
        bonesplitter.SetOwner(_alice);
        bonesplitter.SetController(_alice);
        bonesplitter.Zone = ZoneType.Battlefield;

        var trigger = hammer.Abilities.OfType<TriggeredAbility>().Single();

        // Fire the condition predicate with a synthetic event so the
        // closure captures the entering Equipment, then resolve.
        var moved = new Majik.Core.Events.CardMovedEvent(
            bonesplitter, ZoneType.Hand, ZoneType.Battlefield);
        var matched = trigger.Condition.Matches(moved, trigger);
        matched.Should().BeTrue();

        foreach (var eff in trigger.Effects) eff.Execute();

        bonesplitter.AttachedTo.Should().BeSameAs(bear,
            "the ETB trigger auto-attaches the entering Equipment to the first controller creature");
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SwiftfootBootsFactory"/>.
///
/// Swiftfoot Boots (Magic 2012 et al., {2}) — Artifact — Equipment.
/// Oracle text (Scryfall, verified 2026-06-02):
///   "Equipped creature has hexproof and haste. (It can't be the target of
///    spells or abilities your opponents control. It can attack and {T} no
///    matter when it came under your control.)"
///   "Equip {1}"
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {1} activated-ability shape.
/// - Granted Hexproof marker (CR 702.11) on the equipped creature.
/// - Granted Haste (CR 702.10) read through the layer system.
/// - Detach: granted keywords are revoked.
/// </summary>
public class SwiftfootBootsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SwiftfootBoots_Identity()
    {
        var c = SwiftfootBootsFactory.Create(_alice);

        c.Name.Should().Be("Swiftfoot Boots");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Swiftfoot Boots is an Equipment");
        c.ManaCost.Should().Be("{2}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwiftfootBoots_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Swiftfoot Boots", _alice);

        c.Should().BeOfType<Artifact>("Swiftfoot Boots is an Artifact");
        c.Name.Should().Be("Swiftfoot Boots");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void SwiftfootBoots_EquipAbility_HasGenericOneCost()
    {
        var c = SwiftfootBootsFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1, "Equip {1} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effects — hexproof + haste
    // -----------------------------------------------------------------------

    [Fact]
    public void SwiftfootBoots_GrantsHaste_ToEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        CombatAbilities.HasHaste(bear).Should().BeFalse(
            "the bear has no printed haste");

        var boots = SwiftfootBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;
        boots.AttachTo(bear);

        CombatAbilities.HasHaste(bear).Should().BeTrue(
            "Swiftfoot Boots grants haste to the equipped creature (CR 702.10)");
    }

    [Fact]
    public void SwiftfootBoots_GrantsHexproof_ToEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        svc.Compute(bear).Keywords.Should().NotContain("Hexproof",
            "the bear has no printed hexproof");

        var boots = SwiftfootBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;
        boots.AttachTo(bear);

        // The granted "Hexproof" marker is projected onto the bearer's
        // computed keyword set (CR 702.11). TargetLegality consults exactly
        // this set, so the equipped creature can't be targeted by opponents.
        svc.Compute(bear).Keywords.Should().Contain("Hexproof",
            "Swiftfoot Boots grants hexproof to the equipped creature (CR 702.11)");
    }

    [Fact]
    public void SwiftfootBoots_Detach_RevokesKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var boots = SwiftfootBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;
        boots.AttachTo(bear);

        // While attached: hexproof + haste.
        CombatAbilities.HasHaste(bear).Should().BeTrue();
        svc.Compute(bear).Keywords.Should().Contain("Hexproof");

        boots.Unattach();

        // Both grants gate on AttachedTo — revoked on detach.
        CombatAbilities.HasHaste(bear).Should().BeFalse("granted haste is revoked");
        svc.Compute(bear).Keywords.Should().NotContain("Hexproof",
            "granted hexproof is revoked once the boots are no longer attached");
    }

    [Fact]
    public void SwiftfootBoots_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var boots = SwiftfootBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        CombatAbilities.HasHaste(bear).Should().BeFalse(
            "unequipped Boots gate on AttachedTo");
        svc.Compute(bear).Keywords.Should().NotContain("Hexproof");
    }
}

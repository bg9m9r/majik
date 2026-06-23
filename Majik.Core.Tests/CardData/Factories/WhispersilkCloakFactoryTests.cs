using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WhispersilkCloakFactory"/>.
///
/// Whispersilk Cloak (Mirrodin et al., {3}) — Artifact — Equipment.
/// Oracle text (verified against Scryfall 2026-06-23):
///   "Equipped creature can't be blocked and has shroud. (It can't be the
///    target of spells or abilities.)"
///   "Equip {2}"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (mana cost {3}, Equipment subtype).
/// - Equip {2} activated-ability shape (CR 702.6).
/// - Granted Shroud (CR 702.18) — keyword on the equipped creature, enforced by
///   <see cref="TargetLegality"/> against both players.
/// - "Can't be blocked" (CR 509.1c) combat restriction on the equipped creature.
/// - Detach: both grants are revoked.
/// </summary>
[Trait("Color", "C")]
public class WhispersilkCloakFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature MakeBear(ContinuousEffectsService svc) => new("Bear", "1G", 2, 2)
    {
        Owner = _alice,
        Controller = _alice,
        Zone = ZoneType.Battlefield,
        ActiveEffects = svc,
    };

    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats: {3} mana cost, Equipment subtype)
    // -----------------------------------------------------------------------

    [Fact]
    public void WhispersilkCloak_Identity()
    {
        var c = WhispersilkCloakFactory.Create(_alice);

        c.Name.Should().Be("Whispersilk Cloak");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Whispersilk Cloak is an Equipment");
        c.ManaCost.Should().Be("{3}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Equip cost — {2}
    // -----------------------------------------------------------------------

    [Fact]
    public void WhispersilkCloak_EquipAbility_HasGenericTwoCost()
    {
        var c = WhispersilkCloakFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2, "Equip {2} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Granted Shroud (CR 702.18)
    // -----------------------------------------------------------------------

    [Fact]
    public void WhispersilkCloak_GrantsShroud_ToEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeBear(svc);

        var cloak = WhispersilkCloakFactory.Create(_alice, svc);
        cloak.Zone = ZoneType.Battlefield;
        cloak.AttachTo(bear);

        // CR 613 — the Layer-6 ability grant materialises onto the bearer during
        // a layer pass; the returned keyword set settles on the FOLLOWING
        // Compute. Prime one pass, then assert.
        svc.Compute(bear);
        svc.Compute(bear).Keywords.Should().Contain("Shroud",
            "Whispersilk Cloak grants shroud to the equipped creature (CR 702.18)");
    }

    [Fact]
    public void WhispersilkCloak_Shroud_MakesEquippedCreatureUntargetable()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeBear(svc);

        var spec = new TargetSpec("target creature").Creatures();

        // Before equipping the bear is a legal target for either player.
        TargetLegality.IsLegal(spec, bear, _alice).Should().BeTrue(
            "an un-shrouded creature can be targeted");

        var cloak = WhispersilkCloakFactory.Create(_alice, svc);
        cloak.Zone = ZoneType.Battlefield;
        cloak.AttachTo(bear);
        svc.Compute(bear); // settle the Layer-6 grant (CR 613).

        // CR 702.18 — Shroud: the creature can't be the target of ANY spell or
        // ability, including its own controller's.
        TargetLegality.IsLegal(spec, bear, _alice).Should().BeFalse(
            "shroud stops even the controller from targeting (CR 702.18)");
        TargetLegality.IsLegal(spec, bear, _bob).Should().BeFalse(
            "shroud stops opponents from targeting (CR 702.18)");
    }

    // -----------------------------------------------------------------------
    // "Can't be blocked" (CR 509.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void WhispersilkCloak_GrantsCantBeBlocked_ToEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeBear(svc);

        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "the bear has no printed evasion");

        var cloak = WhispersilkCloakFactory.Create(_alice, svc);
        cloak.Zone = ZoneType.Battlefield;
        cloak.AttachTo(bear);

        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "Whispersilk Cloak makes the equipped creature unblockable (CR 509.1c)");
    }

    // -----------------------------------------------------------------------
    // Detach revokes both grants
    // -----------------------------------------------------------------------

    [Fact]
    public void WhispersilkCloak_Detach_RevokesBothGrants()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeBear(svc);

        var cloak = WhispersilkCloakFactory.Create(_alice, svc);
        cloak.Zone = ZoneType.Battlefield;
        cloak.AttachTo(bear);

        // While attached: shroud + can't-be-blocked.
        svc.Compute(bear);
        svc.Compute(bear).Keywords.Should().Contain("Shroud");
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeTrue();

        cloak.Unattach();

        // Both grants gate on AttachedTo — revoked on detach. Prime once more so
        // the keyword revoke settles before asserting.
        svc.Compute(bear);
        svc.Compute(bear).Keywords.Should().NotContain("Shroud",
            "granted shroud is revoked once the cloak is no longer attached");
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "the can't-be-blocked restriction gates on the cloak staying attached");
    }

    [Fact]
    public void WhispersilkCloak_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeBear(svc);

        var cloak = WhispersilkCloakFactory.Create(_alice, svc);
        cloak.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        svc.Compute(bear).Keywords.Should().NotContain("Shroud");
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "unequipped Cloak grants nothing");
    }
}

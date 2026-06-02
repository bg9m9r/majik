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
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LightningGreavesFactory"/>.
///
/// Lightning Greaves (Mirrodin, {2}) — Artifact — Equipment.
/// Oracle text (Scryfall, verified 2026-06-02):
///   "Equipped creature has haste and shroud. (It can't be the target of
///    spells or abilities.)"
///   "Equip {0}"
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {0} activated-ability shape (CR 702.6).
/// - Granted Haste (CR 702.10) read through the layer system.
/// - Granted Shroud (CR 702.18) — keyword on the equipped creature, enforced
///   by <see cref="TargetLegality"/>.
/// - Detach: granted keywords are revoked.
/// </summary>
public class LightningGreavesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningGreaves_Identity()
    {
        var c = LightningGreavesFactory.Create(_alice);

        c.Name.Should().Be("Lightning Greaves");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Lightning Greaves is an Equipment");
        c.ManaCost.Should().Be("{2}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LightningGreaves_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Lightning Greaves", _alice);

        c.Should().BeOfType<Artifact>("Lightning Greaves is an Artifact");
        c.Name.Should().Be("Lightning Greaves");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip cost — {0}
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningGreaves_EquipAbility_HasZeroCost()
    {
        var c = LightningGreavesFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(0, "Equip {0} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Granted keywords — haste + shroud
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningGreaves_GrantsHaste_ToEquippedCreature()
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

        var greaves = LightningGreavesFactory.Create(_alice, svc);
        greaves.Zone = ZoneType.Battlefield;
        greaves.AttachTo(bear);

        // CR 613 — a Layer-6 ability grant materialises onto the bearer during
        // a layer pass (SyncAbilityGrants), and the returned keyword set
        // stabilises on the FOLLOWING Compute. Prime one pass so the assertion
        // reads the settled state, exactly as repeated layer recomputation does
        // during a real game.
        svc.Compute(bear);

        CombatAbilities.HasHaste(bear).Should().BeTrue(
            "Lightning Greaves grants haste to the equipped creature (CR 702.10)");
    }

    [Fact]
    public void LightningGreaves_GrantsShroud_ToEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var greaves = LightningGreavesFactory.Create(_alice, svc);
        greaves.Zone = ZoneType.Battlefield;
        greaves.AttachTo(bear);

        // A layer pass (CR 613) materialises the Layer-6 ability grants onto
        // the bearer's Abilities list; the returned keyword set settles on the
        // following Compute. Prime one pass, then assert.
        svc.Compute(bear);
        svc.Compute(bear).Keywords.Should().Contain("Shroud",
            "Lightning Greaves grants shroud to the equipped creature (CR 702.18)");
    }

    [Fact]
    public void LightningGreaves_Shroud_MakesEquippedCreatureUntargetable()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var spec = new TargetSpec("target creature").Creatures();

        // Before equipping the bear is a legal target for either player.
        TargetLegality.IsLegal(spec, bear, _alice).Should().BeTrue(
            "an un-shrouded creature can be targeted");

        var greaves = LightningGreavesFactory.Create(_alice, svc);
        greaves.Zone = ZoneType.Battlefield;
        greaves.AttachTo(bear);
        svc.Compute(bear); // settle the Layer-6 grant (CR 613).

        // CR 702.18 — Shroud: the creature can't be the target of ANY spell or
        // ability, including its own controller's.
        TargetLegality.IsLegal(spec, bear, _alice).Should().BeFalse(
            "shroud stops even the controller from targeting (CR 702.18)");
        TargetLegality.IsLegal(spec, bear, _bob).Should().BeFalse(
            "shroud stops opponents from targeting (CR 702.18)");
    }

    [Fact]
    public void LightningGreaves_Detach_RevokesGrantedKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var greaves = LightningGreavesFactory.Create(_alice, svc);
        greaves.Zone = ZoneType.Battlefield;
        greaves.AttachTo(bear);

        // While attached: haste + shroud. Prime a layer pass so the grants
        // settle (see GrantsHaste test for the rationale).
        svc.Compute(bear);
        CombatAbilities.HasHaste(bear).Should().BeTrue();
        svc.Compute(bear).Keywords.Should().Contain("Shroud");

        greaves.Unattach();

        // Both grants gate on AttachedTo — revoked on detach. Prime once more
        // so the revoke settles before asserting.
        svc.Compute(bear);
        CombatAbilities.HasHaste(bear).Should().BeFalse("granted haste is revoked");
        svc.Compute(bear).Keywords.Should().NotContain("Shroud",
            "granted shroud is revoked once the greaves are no longer attached");
    }

    [Fact]
    public void LightningGreaves_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var greaves = LightningGreavesFactory.Create(_alice, svc);
        greaves.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        CombatAbilities.HasHaste(bear).Should().BeFalse(
            "unequipped Greaves grant nothing");
        svc.Compute(bear).Keywords.Should().NotContain("Shroud");
    }
}

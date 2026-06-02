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
/// Unit tests for <see cref="LavaspurBootsFactory"/>.
///
/// Lavaspur Boots (Outlaws of Thunder Junction, {1}) — Artifact — Equipment.
/// Oracle text (Scryfall, verified 2026-06-01):
///   "Equipped creature gets +1/+0 and has haste and ward {1}.
///    (Whenever it becomes the target of a spell or ability an opponent
///     controls, counter it unless that player pays {1}.)"
///   "Equip {1}"
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {1} activated-ability shape.
/// - Static +1/+0 boost (Layer 7c) on the equipped creature.
/// - Granted Haste (CR 702.10) read through the layer system.
/// - Granted Ward {1} marker (CR 702.21) on the equipped creature.
/// - Detach: boost lapses and granted keywords are revoked.
/// </summary>
public class LavaspurBootsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LavaspurBoots_Identity()
    {
        var c = LavaspurBootsFactory.Create(_alice);

        c.Name.Should().Be("Lavaspur Boots");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Lavaspur Boots is an Equipment");
        c.ManaCost.Should().Be("{1}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LavaspurBoots_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Lavaspur Boots", _alice);

        c.Should().BeOfType<Artifact>("Lavaspur Boots is an Artifact");
        c.Name.Should().Be("Lavaspur Boots");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void LavaspurBoots_EquipAbility_HasGenericOneCost()
    {
        var c = LavaspurBootsFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1, "Equip {1} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effects — +1/+0, haste, ward {1}
    // -----------------------------------------------------------------------

    [Fact]
    public void LavaspurBoots_Equipped_Bear_Gets_Plus1Power()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var boots = LavaspurBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;

        boots.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1/+0 boost from Lavaspur Boots");
        bear.GetToughness().Should().Be(2, "Boots add +0 toughness");
    }

    [Fact]
    public void LavaspurBoots_GrantsHaste_ToEquippedCreature()
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

        var boots = LavaspurBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;
        boots.AttachTo(bear);

        // CR 613 — a Layer-6 ability grant materialises onto the bearer
        // during a layer pass (SyncAbilityGrants), and the returned keyword
        // set stabilises on the FOLLOWING Compute (the grant-attach side
        // effect invalidates the in-pass cache by design). Prime one pass so
        // the assertion reads the settled state, exactly as repeated SBA /
        // layer recomputation does during a real game.
        svc.Compute(bear);

        CombatAbilities.HasHaste(bear).Should().BeTrue(
            "Lavaspur Boots grants haste to the equipped creature (CR 702.10)");
    }

    [Fact]
    public void LavaspurBoots_GrantsWard1_ToEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var boots = LavaspurBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;
        boots.AttachTo(bear);

        // A layer pass (CR 613) materialises the Layer-6 ability grants onto
        // the bearer's Abilities list (SyncAbilityGrants); the returned
        // keyword set settles on the following Compute. Prime one pass, then
        // assert — mirroring repeated layer recomputation during a game.
        svc.Compute(bear);
        svc.Compute(bear).Keywords.Should().Contain("Ward",
            "Lavaspur Boots grants ward to the equipped creature (CR 702.21)");

        var ward = bear.Abilities
            .OfType<KeywordAbility>()
            .Single(k => k.Keyword == "Ward");
        ward.Arg.Should().Be(1, "the printed ward cost is {1} (CR 702.21)");
    }

    [Fact]
    public void LavaspurBoots_Detach_RevokesBoostAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var boots = LavaspurBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;
        boots.AttachTo(bear);

        // While attached: 3/2, haste, ward {1}. Prime a layer pass so the
        // Layer-6 grants settle (see GrantsHaste test for the rationale).
        svc.Compute(bear);
        bear.GetPower().Should().Be(3);
        CombatAbilities.HasHaste(bear).Should().BeTrue();
        svc.Compute(bear).Keywords.Should().Contain("Ward");

        boots.Unattach();

        // All grants gate on AttachedTo — revoked on detach. Prime once more
        // so the revoke settles before asserting.
        svc.Compute(bear);
        bear.GetPower().Should().Be(2, "boost lapses on detach");
        CombatAbilities.HasHaste(bear).Should().BeFalse("granted haste is revoked");
        svc.Compute(bear).Keywords.Should().NotContain("Ward",
            "granted ward is revoked once the boots are no longer attached");
    }

    [Fact]
    public void LavaspurBoots_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var boots = LavaspurBootsFactory.Create(_alice, svc);
        boots.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2, "unequipped Boots gate on AttachedTo");
        CombatAbilities.HasHaste(bear).Should().BeFalse();
    }
}

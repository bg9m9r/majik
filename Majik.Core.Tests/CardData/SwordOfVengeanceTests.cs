using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="SwordOfVengeanceFactory"/>.
///
/// Sword of Vengeance (Magic 2013, {3}) — Artifact — Equipment.
/// Oracle text (Scryfall, verified 2026-06-23):
///   "Equipped creature gets +2/+0 and has first strike, vigilance,
///    trample, and haste."
///   "Equip {3}"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, type, Equipment subtype, mana cost) — single assert.
/// - Equip {3} activated-ability shape (the printed activation cost).
/// - Static +2/+0 boost (Layer 7c) on the equipped creature.
/// - The four granted evergreen combat keywords read through the layer
///   system (first strike / vigilance / trample / haste).
/// - Detach: the boost lapses and granted keywords are revoked.
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so no *_DispatchesViaNamedCardFactory test here.)
/// </summary>
[Trait("Color", "C")] // colourless artifact
public class SwordOfVengeanceTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfVengeance_Identity()
    {
        var c = SwordOfVengeanceFactory.Create(_alice);

        c.Name.Should().Be("Sword of Vengeance");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Sword of Vengeance is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfVengeance_EquipAbility_HasGenericThreeCost()
    {
        var c = SwordOfVengeanceFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(3, "Equip {3} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effect — +2/+0
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfVengeance_Equipped_Bear_Gets_Plus2Power()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfVengeanceFactory.Create(_alice, svc);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+0 boost from Sword of Vengeance");
        bear.GetToughness().Should().Be(2, "Sword adds +0 toughness");
    }

    // -----------------------------------------------------------------------
    // Granted evergreen combat keywords
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfVengeance_GrantsFirstStrikeVigilanceTrampleHaste()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        // The bear has none of the keywords printed.
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse();
        CombatAbilities.HasVigilance(bear).Should().BeFalse();
        CombatAbilities.HasTrample(bear).Should().BeFalse();
        CombatAbilities.HasHaste(bear).Should().BeFalse();

        var sword = SwordOfVengeanceFactory.Create(_alice, svc);
        sword.Zone = ZoneType.Battlefield;
        sword.AttachTo(bear);

        // CR 613 — Layer-6 ability grants materialise onto the bearer during a
        // layer pass (SyncAbilityGrants); the returned keyword set settles on
        // the FOLLOWING Compute. Prime one pass so the assertion reads the
        // settled state, as repeated SBA / layer recomputation does in a game.
        svc.Compute(bear);

        CombatAbilities.HasFirstStrike(bear).Should().BeTrue(
            "Sword of Vengeance grants first strike (CR 702.7)");
        CombatAbilities.HasVigilance(bear).Should().BeTrue(
            "Sword of Vengeance grants vigilance (CR 702.20)");
        CombatAbilities.HasTrample(bear).Should().BeTrue(
            "Sword of Vengeance grants trample (CR 702.19)");
        CombatAbilities.HasHaste(bear).Should().BeTrue(
            "Sword of Vengeance grants haste (CR 702.10)");
    }

    // -----------------------------------------------------------------------
    // Detach revokes the boost and the granted keywords
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfVengeance_Detach_RevokesBoostAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfVengeanceFactory.Create(_alice, svc);
        sword.Zone = ZoneType.Battlefield;
        sword.AttachTo(bear);

        // While attached: 4/2 with all four keywords. Prime a layer pass so
        // the Layer-6 grants settle before asserting.
        svc.Compute(bear);
        bear.GetPower().Should().Be(4);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();
        CombatAbilities.HasVigilance(bear).Should().BeTrue();
        CombatAbilities.HasTrample(bear).Should().BeTrue();
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        sword.Unattach();

        // All grants gate on AttachedTo — revoked on detach. Prime once more
        // so the revoke settles before asserting.
        svc.Compute(bear);
        bear.GetPower().Should().Be(2, "boost lapses on detach");
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse("granted first strike is revoked");
        CombatAbilities.HasVigilance(bear).Should().BeFalse("granted vigilance is revoked");
        CombatAbilities.HasTrample(bear).Should().BeFalse("granted trample is revoked");
        CombatAbilities.HasHaste(bear).Should().BeFalse("granted haste is revoked");
    }
}

using System.Linq;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ONaginataFactory"/>.
///
/// Card: O-Naginata — Artifact — Equipment {1} (Champions of Kamigawa).
///   "This Equipment can be attached only to a creature with power 3 or
///    greater."
///   "Equipped creature gets +3/+0 and has trample."
///   "Equip {2}"
///
/// Same paired-grant equip shape as <see cref="ShadowspearFactory"/> (+P/+0
/// boost + Trample keyword grant), with the CR 702.6e equip restriction
/// (power 3 or greater) as the unique behaviour.
/// </summary>
[Trait("Color", "C")]
public class ONaginataFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ONaginata_Identity()
    {
        var c = ONaginataFactory.Create(_alice);

        c.Name.Should().Be("O-Naginata");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "O-Naginata is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ONaginata_EquipAbility_HasGenericTwoCost()
    {
        var c = ONaginataFactory.Create(_alice);

        var ability = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void ONaginata_Equipped_Gives_Plus3_0_And_Trample()
    {
        var svc = new ContinuousEffectsService();
        var ogre = new Creature("Ogre", "3R", 3, 3)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var naginata = ONaginataFactory.Create(_alice, svc);
        naginata.Zone = ZoneType.Battlefield;

        naginata.AttachTo(ogre);

        ogre.GetPower().Should().Be(6, "+3/+0 boost from O-Naginata");
        ogre.GetToughness().Should().Be(3, "O-Naginata adds +0 toughness");
        CombatAbilities.HasTrample(ogre).Should().BeTrue(
            "O-Naginata grants trample to the equipped creature");
    }

    [Fact]
    public void ONaginata_Detach_RestoresPT_And_RemovesTrample()
    {
        var svc = new ContinuousEffectsService();
        var ogre = new Creature("Ogre", "3R", 3, 3)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var naginata = ONaginataFactory.Create(_alice, svc);
        naginata.Zone = ZoneType.Battlefield;
        naginata.AttachTo(ogre);

        ogre.GetPower().Should().Be(6);
        CombatAbilities.HasTrample(ogre).Should().BeTrue();

        naginata.Unattach();

        ogre.GetPower().Should().Be(3,
            "boost lapses on detach — AttachedTo gate flips IsActive to false");
        CombatAbilities.HasTrample(ogre).Should().BeFalse(
            "the granted trample lapses with the attachment");
    }

    // CR 702.6e — "can be attached only to a creature with power 3 or
    // greater." The equip restriction narrows the legal attach candidates.

    [Fact]
    public void ONaginata_AttachRestriction_AllowsPower3OrGreater()
    {
        ONaginataFactory.MeetsAttachRestriction(
            new Creature("Bear", "1G", 3, 3)).Should().BeTrue(
            "power exactly 3 satisfies 'power 3 or greater'");
        ONaginataFactory.MeetsAttachRestriction(
            new Creature("Giant", "4G", 5, 5)).Should().BeTrue(
            "power above 3 satisfies the restriction");
    }

    [Fact]
    public void ONaginata_AttachRestriction_RejectsPowerUnder3()
    {
        ONaginataFactory.MeetsAttachRestriction(
            new Creature("Bear", "1G", 2, 2)).Should().BeFalse(
            "a power-2 creature cannot be equipped with O-Naginata");
    }

    [Fact]
    public void ONaginata_EquipCandidates_OnlyPower3Plus()
    {
        var svc = new ContinuousEffectsService();
        var weakling = new Creature("Weakling", "G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var bruiser = new Creature("Bruiser", "3G", 4, 4)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(weakling);
        _alice.Zones.Battlefield.AddCard(bruiser);

        var naginata = ONaginataFactory.Create(_alice, svc);
        naginata.Zone = ZoneType.Battlefield;
        var equip = naginata.Abilities.OfType<EquipActivatedAbility>().Single();

        // The equip candidate gatherer reads the source's controller directly
        // and ignores its GameContext argument, so null! is safe here.
        var candidates = equip.TargetCreature.CandidateGatherer!(null!)
            .OfType<Creature>()
            .ToList();

        candidates.Should().Contain(bruiser, "power 4 satisfies the restriction");
        candidates.Should().NotContain(weakling,
            "power 2 is filtered out by the CR 702.6e equip restriction");
    }

    [Fact]
    public void ONaginata_EquipResolve_AttachesOnlyToPower3Plus()
    {
        var svc = new ContinuousEffectsService();
        var weakling = new Creature("Weakling", "G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var bruiser = new Creature("Bruiser", "3G", 4, 4)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(weakling);
        _alice.Zones.Battlefield.AddCard(bruiser);

        var naginata = ONaginataFactory.Create(_alice, svc);
        naginata.Zone = ZoneType.Battlefield;
        var equip = naginata.Abilities.OfType<EquipActivatedAbility>().Single();

        // No agent target chosen → deterministic fallback picker must skip
        // the power-2 Weakling and attach to the power-4 Bruiser.
        equip.Resolve();

        naginata.AttachedTo.Should().BeSameAs(bruiser,
            "the fallback picker honours the CR 702.6e equip restriction");
    }
}

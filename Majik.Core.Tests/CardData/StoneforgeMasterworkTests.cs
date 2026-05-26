using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="StoneforgeMasterworkFactory"/>.
///
/// Covers:
/// - Identity (name, Artifact, Equipment subtype, mana cost {1}).
/// - NamedCardFactory dispatch.
/// - Equip {2} activated ability shape.
/// - Dynamic +N/+N boost where N = count of other creatures the
///   controller controls that share a creature subtype with the
///   equipped creature.
/// - Boost falls back to 0 when unequipped.
/// - Boost ignores non-creature subtypes (Equipment / Aura / land
///   subtypes) so a Human Soldier equipped Bear doesn't pick up
///   bonuses from siblings that share Equipment etc.
/// - Pure helper end-to-end (`CountSharedSubtypeCreatures`).
/// </summary>
public class StoneforgeMasterworkTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMasterwork_Identity()
    {
        var c = StoneforgeMasterworkFactory.Create(_alice);

        c.Name.Should().Be("Stoneforge Masterwork");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StoneforgeMasterwork_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Stoneforge Masterwork", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Stoneforge Masterwork");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip {2}
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMasterwork_EquipAbility_HasGenericTwoCost_AndSorcerySpeed()
    {
        var c = StoneforgeMasterworkFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();

        equip.EquipCost.Generic.Should().Be(2, "printed Equip {2}");
        equip.IsSorcerySpeed.Should().BeTrue(
            "Equip is a sorcery-speed activation per CR 702.6d");
    }

    // -----------------------------------------------------------------------
    // Dynamic +N/+N boost
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMasterwork_Equipped_BoostScalesByOtherSharedTypeCreatures()
    {
        var svc = new ContinuousEffectsService();

        // Equipped creature: Human Soldier.
        var champion = new Creature(
            "Test Soldier",
            "W",
            1, 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(champion);

        var masterwork = StoneforgeMasterworkFactory.Create(_alice, svc);
        masterwork.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(masterwork);

        masterwork.AttachTo(champion);

        // No other creatures yet → boost = 0.
        champion.GetPower().Should().Be(1, "no other creatures share a type with the Soldier");
        champion.GetToughness().Should().Be(1);

        // Add another Human (no Soldier overlap) — shares Human → +1/+1.
        var thalia = new Creature(
            "Human Pal", "W", 2, 1,
            subtypes: new[] { CardSubtype.Human })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(thalia);

        champion.GetPower().Should().Be(2, "+1/+1 from one shared-type Human");
        champion.GetToughness().Should().Be(2);

        // Add a Soldier (not Human) — also shares a type → +2/+2.
        var soldier = new Creature(
            "Lone Soldier", "W", 1, 1,
            subtypes: new[] { CardSubtype.Soldier })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(soldier);

        champion.GetPower().Should().Be(3, "+2/+2 from two shared-type creatures");
        champion.GetToughness().Should().Be(3);

        // Add an unrelated creature (Elf) — no shared subtype → still +2/+2.
        var elf = new Creature(
            "Random Elf", "G", 1, 1,
            subtypes: new[] { CardSubtype.Elf })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(elf);

        champion.GetPower().Should().Be(3, "Elf shares nothing with Human Soldier");
        champion.GetToughness().Should().Be(3);
    }

    [Fact]
    public void StoneforgeMasterwork_Unattached_BoostIsZero()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var masterwork = StoneforgeMasterworkFactory.Create(_alice, svc);
        masterwork.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(masterwork);
        // intentionally not attached

        bear.GetPower().Should().Be(2, "the boost gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void StoneforgeMasterwork_Boost_IgnoresEquipmentArtifactSubtypes()
    {
        // Regression: a sibling Equipment must NOT contribute to the
        // count even though both share "Equipment" subtype with the
        // bearer's attached masterwork — Equipment is a non-creature
        // subtype.
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Plain Bear", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var sibling = new Artifact(
            "Random Equipment", "2",
            subtypes: new[] { CardSubtype.Equipment });
        sibling.SetOwner(_alice);
        sibling.SetController(_alice);
        sibling.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(sibling);

        var masterwork = StoneforgeMasterworkFactory.Create(_alice, svc);
        masterwork.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(masterwork);

        masterwork.AttachTo(bear);

        // The sibling artifact is not a creature; the bear has the Bear
        // creature subtype and there are no other creatures sharing it.
        bear.GetPower().Should().Be(2, "no other creature shares a creature type with the Bear");
    }

    // -----------------------------------------------------------------------
    // Pure helper
    // -----------------------------------------------------------------------

    [Fact]
    public void CountSharedSubtypeCreatures_ZeroWhenUnattached()
    {
        var masterwork = StoneforgeMasterworkFactory.Create(_alice);
        masterwork.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(masterwork);

        StoneforgeMasterworkFactory.CountSharedSubtypeCreatures(masterwork)
            .Should().Be(0);
    }

    [Fact]
    public void CountSharedSubtypeCreatures_CountsOtherSharedTypeOnly()
    {
        var bear = new Creature("Bear A", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bear);

        var bear2 = new Creature("Bear B", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear2.SetOwner(_alice);
        bear2.SetController(_alice);
        bear2.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bear2);

        var elf = new Creature("Elf", "G", 1, 1,
            subtypes: new[] { CardSubtype.Elf });
        elf.SetOwner(_alice);
        elf.SetController(_alice);
        elf.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(elf);

        var masterwork = StoneforgeMasterworkFactory.Create(_alice);
        masterwork.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(masterwork);
        masterwork.AttachTo(bear);

        // Other Bear matches; Elf does not; the equipped Bear is excluded.
        StoneforgeMasterworkFactory.CountSharedSubtypeCreatures(masterwork)
            .Should().Be(1);
    }
}

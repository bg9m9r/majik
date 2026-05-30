using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CranialPlatingFactory"/>.
///
/// Covers:
/// - Identity (name, Artifact, Equipment subtype, mana cost {1}).
/// - NamedCardFactory dispatch.
/// - Equip {1} activated ability shape.
/// - {B}{B} attach-activation ability shape: ManaCost.Black == 2, no
///   sorcery-speed gate, target-creature-you-control candidate gatherer.
/// - Dynamic +N/+0 boost where N = controller's live artifact count.
///   Adding a fresh artifact while the bear is equipped grows the boost.
/// - Boost falls back to 0 when unequipped.
/// </summary>
public class CranialPlatingTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CranialPlating_Identity()
    {
        var c = CranialPlatingFactory.Create(_alice);

        c.Name.Should().Be("Cranial Plating");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CranialPlating_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Cranial Plating", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Cranial Plating");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip {1}
    // -----------------------------------------------------------------------

    [Fact]
    public void CranialPlating_EquipAbility_HasGenericOneCost_AndSorcerySpeed()
    {
        var c = CranialPlatingFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();

        equip.EquipCost.Generic.Should().Be(1, "printed Equip {1}");
        equip.IsSorcerySpeed.Should().BeTrue(
            "Equip is a sorcery-speed activation per CR 702.6d");
    }

    // -----------------------------------------------------------------------
    // {B}{B}: Attach to target creature you control
    // -----------------------------------------------------------------------

    [Fact]
    public void CranialPlating_BlackAttachAbility_HasTwoBlackCost_AndIsInstantSpeed()
    {
        var c = CranialPlatingFactory.Create(_alice);

        // The non-Equip activated ability is the {B}{B}: attach activation.
        var attach = c.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not EquipActivatedAbility)
            .Single();

        var mana = attach.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Black.Should().Be(2, "printed {B}{B}");
        mana.Cost.Generic.Should().Be(0);
        attach.IsSorcerySpeed.Should().BeFalse(
            "the printed {B}{B} attach activation has no sorcery-speed gate");
    }

    [Fact]
    public void CranialPlating_BlackAttach_AttachesToFirstControllerCreature()
    {
        var plating = CranialPlatingFactory.Create(_alice);
        plating.Zone = ZoneType.Battlefield;

        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);

        var attach = plating.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not EquipActivatedAbility)
            .Single();

        // No agent target supplied — falls back to first controller creature.
        foreach (var eff in attach.Effects) eff.Execute();

        plating.AttachedTo.Should().BeSameAs(bear);
    }

    // -----------------------------------------------------------------------
    // Dynamic +N/+0 boost
    // -----------------------------------------------------------------------

    [Fact]
    public void CranialPlating_Equipped_GrowsBoost_AsArtifactCountRises()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var plating = CranialPlatingFactory.Create(_alice, svc);
        plating.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(plating);

        plating.AttachTo(bear);

        // Only the plating itself counts as an artifact at this point → +1/+0.
        bear.GetPower().Should().Be(2 + 1, "+1/+0 from one artifact (the plating itself)");
        bear.GetToughness().Should().Be(2, "Cranial Plating adds only +N/+0");

        // Add a second artifact under Alice's control. Wire its ActiveEffects
        // (as production does for battlefield permanents) so its zone entry
        // invalidates the layer-system cache.
        var bauble = new Artifact("Bauble", "0");
        bauble.SetOwner(_alice);
        bauble.SetController(_alice);
        bauble.ActiveEffects = svc;
        bauble.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bauble);

        bear.GetPower().Should().Be(2 + 2, "+2/+0 from two artifacts");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void CranialPlating_Unattached_BoostIsZero()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var plating = CranialPlatingFactory.Create(_alice, svc);
        plating.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(plating);
        // intentionally not attached

        bear.GetPower().Should().Be(2, "the boost gates on AttachedTo");
    }

    [Fact]
    public void CranialPlating_CountArtifacts_ReadsControllerBattlefield()
    {
        var plating = CranialPlatingFactory.Create(_alice);
        plating.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(plating);

        CranialPlatingFactory.CountArtifacts(plating).Should().Be(1,
            "only the plating itself is on the battlefield");

        var bauble = new Artifact("Bauble", "0");
        bauble.SetOwner(_alice);
        bauble.SetController(_alice);
        bauble.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bauble);

        CranialPlatingFactory.CountArtifacts(plating).Should().Be(2);
    }
}

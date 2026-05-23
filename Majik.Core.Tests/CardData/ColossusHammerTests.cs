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
/// Unit tests for <see cref="ColossusHammerFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Equipment subtype, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip activated ability shape: {8} mana cost.
/// - Static effect: equipped 2/2 Bear becomes 12/2.
/// - Static effect: equipped flyer loses Flying.
/// - Detach: P/T returns to base and flying restored.
/// </summary>
public class ColossusHammerTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ColossusHammer_Identity()
    {
        var c = ColossusHammerFactory.Create(_alice);

        c.Name.Should().Be("Colossus Hammer");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Colossus Hammer is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ColossusHammer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Colossus Hammer", _alice);

        c.Should().BeOfType<Artifact>("Colossus Hammer is an Artifact");
        c.Name.Should().Be("Colossus Hammer");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void ColossusHammer_EquipAbility_HasGenericEightCost()
    {
        var c = ColossusHammerFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(8,
            "Equip {8} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effects — P/T and lose flying
    // -----------------------------------------------------------------------

    [Fact]
    public void ColossusHammer_Equipped_Bear_Becomes_12_2()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var hammer = ColossusHammerFactory.Create(_alice, svc);
        hammer.Zone = ZoneType.Battlefield;

        hammer.AttachTo(bear);

        bear.GetPower().Should().Be(12, "+10/+0 boost from Colossus Hammer");
        bear.GetToughness().Should().Be(2, "Hammer adds +0 toughness");
    }

    [Fact]
    public void ColossusHammer_StripsFlying_FromEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var drake = new Creature("Drake", "2U", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        drake.AddAbility(new KeywordAbility("Flying", drake, _alice));

        // Sanity: drake starts with Flying.
        CombatAbilities.HasFlying(drake).Should().BeTrue(
            "drake's printed keyword set includes Flying");

        var hammer = ColossusHammerFactory.Create(_alice, svc);
        hammer.Zone = ZoneType.Battlefield;
        hammer.AttachTo(drake);

        CombatAbilities.HasFlying(drake).Should().BeFalse(
            "Colossus Hammer strips Flying from the equipped creature");
        drake.GetPower().Should().Be(12, "drake also gets +10/+0");
    }

    [Fact]
    public void ColossusHammer_Detach_RestoresPT_AndFlying()
    {
        var svc = new ContinuousEffectsService();
        var drake = new Creature("Drake", "2U", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        drake.AddAbility(new KeywordAbility("Flying", drake, _alice));

        var hammer = ColossusHammerFactory.Create(_alice, svc);
        hammer.Zone = ZoneType.Battlefield;
        hammer.AttachTo(drake);

        // While attached: 12/2 + no flying.
        drake.GetPower().Should().Be(12);
        CombatAbilities.HasFlying(drake).Should().BeFalse();

        hammer.Unattach();

        // Both effects gate on AttachedTo != null — IsActive() now false,
        // so the working set falls back to printed values.
        drake.GetPower().Should().Be(2, "boost lapses on detach");
        drake.GetToughness().Should().Be(2);
        CombatAbilities.HasFlying(drake).Should().BeTrue(
            "Flying returns once the equipment is no longer attached");
    }

    [Fact]
    public void ColossusHammer_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var hammer = ColossusHammerFactory.Create(_alice, svc);
        hammer.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Hammer's effects gate on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }
}

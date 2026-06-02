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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ParadiseMantleFactory"/>.
///
/// Paradise Mantle (Fifth Dawn / Modern Horizons, {0}) — Artifact — Equipment.
/// Oracle text (Scryfall, verified 2026-06-02):
///   "Equipped creature has \"{T}: Add one mana of any color.\""
///   "Equip {1}"
///
/// Covers:
/// - Identity (name, Artifact type, Equipment subtype, {0} cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {1} activated-ability shape.
/// - Granted "{T}: Add one mana of any color" — five WUBRG ManaAbility slots
///   appear on the equipped creature once attached.
/// - Activating a granted slot taps the CREATURE and produces that colour.
/// - Detach revokes all five granted slots (CR 613.6e).
/// - Unattached mantle grants nothing.
/// </summary>
[Trait("Color", "C")]
public class ParadiseMantleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Creature ReadyBear(ContinuousEffectsService svc)
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);
        bear.ClearSummoningSickness();
        return bear;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ParadiseMantle_Identity()
    {
        var c = ParadiseMantleFactory.Create(_alice);

        c.Name.Should().Be("Paradise Mantle");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Paradise Mantle is an Equipment");
        c.ManaCost.Should().Be("{0}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ParadiseMantle_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Paradise Mantle", _alice);

        c.Should().BeOfType<Artifact>("Paradise Mantle is an Artifact");
        c.Name.Should().Be("Paradise Mantle");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void ParadiseMantle_EquipAbility_HasGenericOneCost()
    {
        var c = ParadiseMantleFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1,
            "Equip {1} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Granted "{T}: Add one mana of any color"
    // -----------------------------------------------------------------------

    [Fact]
    public void ParadiseMantle_Equipped_GrantsFiveColorManaAbilities()
    {
        var svc = new ContinuousEffectsService();
        var bear = ReadyBear(svc);
        var mantle = ParadiseMantleFactory.Create(_alice, svc);
        mantle.Zone = ZoneType.Battlefield;

        mantle.AttachTo(bear);
        // GrantAbilityEffect materialises the granted abilities onto the
        // bearer's Abilities list during Compute (same priming as Lavaspur).
        svc.Compute(bear);

        bear.Abilities.OfType<ParadiseMantleManaAbility>()
            .Should().HaveCount(5,
                "\"{T}: Add one mana of any color\" is modeled as one slot per WUBRG");
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void ParadiseMantle_GrantsOneSlotPerColor(string colorPip)
    {
        var svc = new ContinuousEffectsService();
        var bear = ReadyBear(svc);
        var mantle = ParadiseMantleFactory.Create(_alice, svc);
        mantle.Zone = ZoneType.Battlefield;

        mantle.AttachTo(bear);
        svc.Compute(bear);

        bear.Abilities.OfType<ParadiseMantleManaAbility>()
            .Should().ContainSingle(a => a.ColorPip == colorPip);
    }

    [Fact]
    public void ParadiseMantle_TapForBlue_TapsCreature_ProducesU()
    {
        var svc = new ContinuousEffectsService();
        var bear = ReadyBear(svc);
        var mantle = ParadiseMantleFactory.Create(_alice, svc);
        mantle.Zone = ZoneType.Battlefield;

        mantle.AttachTo(bear);
        svc.Compute(bear);

        var blue = bear.Abilities.OfType<ParadiseMantleManaAbility>()
            .Single(a => a.ColorPip == "U");

        blue.CanActivate().Should().BeTrue(
            "the bear is untapped and not summoning-sick");
        var mana = blue.Activate();

        mana.Blue.Should().Be(1, "{T}: Add one mana of any color — here U");
        mana.Generic.Should().Be(0);
        bear.IsTapped.Should().BeTrue(
            "the equipped creature HAS the ability, so its {T} taps the creature");
        mantle.IsTapped.Should().BeFalse(
            "the mantle itself is not the source of the granted {T}");
    }

    [Fact]
    public void ParadiseMantle_Detach_RevokesGrantedAbilities()
    {
        var svc = new ContinuousEffectsService();
        var bear = ReadyBear(svc);
        var mantle = ParadiseMantleFactory.Create(_alice, svc);
        mantle.Zone = ZoneType.Battlefield;

        mantle.AttachTo(bear);
        svc.Compute(bear);
        bear.Abilities.OfType<ParadiseMantleManaAbility>().Should().HaveCount(5);

        mantle.Unattach();
        svc.Compute(bear);

        bear.Abilities.OfType<ParadiseMantleManaAbility>().Should().BeEmpty(
            "detach revokes the granted mana abilities (CR 613.6e)");
    }

    [Fact]
    public void ParadiseMantle_Unattached_GrantsNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = ReadyBear(svc);
        var mantle = ParadiseMantleFactory.Create(_alice, svc);
        mantle.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        svc.Compute(bear);

        bear.Abilities.OfType<ParadiseMantleManaAbility>().Should().BeEmpty(
            "an unequipped mantle's grant selector returns null");
    }
}

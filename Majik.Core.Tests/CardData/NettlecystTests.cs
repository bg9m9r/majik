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
/// Unit tests for <see cref="NettlecystFactory"/> (New Phyrexia, {3}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-05-29):
///   "Living weapon (When this Equipment enters, create a 0/0 black
///    Phyrexian Germ creature token, then attach this to it.)"
///   "Equipped creature gets +1/+1 for each artifact and/or enchantment
///    you control."
///   "Equip {2}"
///
/// Combines the Batterskull living-weapon ETB (0/0 black Germ + auto
/// attach, CR 702.91) with the Cranial Plating dynamic boost — except
/// Nettlecyst is +1/+1 (both stats) per artifact AND/OR enchantment you
/// control. Covers:
/// - Identity (Artifact Equipment, {3}).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Equip {2} activated ability shape.
/// - Living-weapon ETB trigger spawns a 0/0 black Phyrexian Germ and
///   attaches Nettlecyst to it.
/// - Dynamic +N/+N boost where N = controller's live artifact + enchantment
///   count; growing the count grows both power and toughness.
/// - Boost gates on AttachedTo (zero when unequipped).
/// </summary>
public class NettlecystTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Nettlecyst_Identity()
    {
        var c = NettlecystFactory.Create(_alice);

        c.Name.Should().Be("Nettlecyst");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Nettlecyst_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Nettlecyst", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Nettlecyst");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the living-weapon ETB trigger is attached");
        c.Abilities.OfType<EquipActivatedAbility>().Should().HaveCount(1,
            "Equip {2} is wired");
    }

    // -----------------------------------------------------------------------
    // Equip {2}
    // -----------------------------------------------------------------------

    [Fact]
    public void Nettlecyst_EquipAbility_HasGenericTwoCost_AndSorcerySpeed()
    {
        var c = NettlecystFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();

        equip.EquipCost.Generic.Should().Be(2, "printed Equip {2}");
        equip.IsSorcerySpeed.Should().BeTrue(
            "Equip is a sorcery-speed activation per CR 702.6d");
    }

    // -----------------------------------------------------------------------
    // Living weapon — ETB spawns 0/0 black Phyrexian Germ + auto-attaches
    // -----------------------------------------------------------------------

    [Fact]
    public void Nettlecyst_LivingWeapon_SpawnsPhyrexianGermAndAttaches()
    {
        var cyst = NettlecystFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cyst);
        cyst.SetZone(ZoneType.Battlefield);

        var etb = cyst.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var germ = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Germ");

        germ.Should().NotBeNull("living weapon spawns a Germ token");
        germ!.IsToken.Should().BeTrue();
        germ.BasePower.Should().Be(0, "Germ enters as 0/0 (CR 702.91)");
        germ.BaseToughness.Should().Be(0);
        germ.HasSubtype(CardSubtype.Germ).Should().BeTrue();
        germ.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue(
            "Nettlecyst's token is a Phyrexian Germ");
        Majik.Core.Cards.CardColors.GetColors(germ).Should()
            .ContainSingle(c => c == Majik.Core.ValueObjects.ManaColor.Black,
                "Germ is a black creature token");

        cyst.AttachedTo.Should().BeSameAs(germ,
            "Nettlecyst attaches itself to the freshly-spawned Germ");
    }

    // -----------------------------------------------------------------------
    // Dynamic +N/+N boost (artifacts AND/OR enchantments you control)
    // -----------------------------------------------------------------------

    [Fact]
    public void Nettlecyst_Equipped_GrowsBoost_AsArtifactAndEnchantmentCountRises()
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

        var cyst = NettlecystFactory.Create(_alice, svc);
        cyst.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(cyst);

        cyst.AttachTo(bear);

        // Only Nettlecyst itself counts (an artifact) at this point → +1/+1.
        bear.GetPower().Should().Be(2 + 1, "+1/+1 from one artifact (Nettlecyst itself)");
        bear.GetToughness().Should().Be(2 + 1, "Nettlecyst adds +1/+1 (both stats)");

        // Add an enchantment under Alice's control → counts toward N.
        var enchant = new Enchantment("Aura", "1W");
        enchant.SetOwner(_alice);
        enchant.SetController(_alice);
        enchant.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(enchant);

        bear.GetPower().Should().Be(2 + 2, "+2/+2 from one artifact + one enchantment");
        bear.GetToughness().Should().Be(2 + 2);

        // Add a second artifact → counts toward N.
        var bauble = new Artifact("Bauble", "0");
        bauble.SetOwner(_alice);
        bauble.SetController(_alice);
        bauble.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bauble);

        bear.GetPower().Should().Be(2 + 3, "+3/+3 from two artifacts + one enchantment");
        bear.GetToughness().Should().Be(2 + 3);
    }

    [Fact]
    public void Nettlecyst_Unattached_BoostIsZero()
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

        var cyst = NettlecystFactory.Create(_alice, svc);
        cyst.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(cyst);
        // intentionally not attached

        bear.GetPower().Should().Be(2, "the boost gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Nettlecyst_CountArtifactsAndEnchantments_ReadsControllerBattlefield()
    {
        var cyst = NettlecystFactory.Create(_alice);
        cyst.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(cyst);

        NettlecystFactory.CountArtifactsAndEnchantments(cyst).Should().Be(1,
            "only Nettlecyst itself (an artifact) is on the battlefield");

        var bauble = new Artifact("Bauble", "0");
        bauble.SetOwner(_alice);
        bauble.SetController(_alice);
        bauble.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bauble);

        var enchant = new Enchantment("Aura", "1W");
        enchant.SetOwner(_alice);
        enchant.SetController(_alice);
        enchant.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(enchant);

        NettlecystFactory.CountArtifactsAndEnchantments(cyst).Should().Be(3,
            "two artifacts + one enchantment");
    }
}

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
/// Unit tests for <see cref="BasiliskCollarFactory"/>.
///
/// Card: Basilisk Collar — Artifact — Equipment {1} (Worldwake).
///   "Equipped creature has deathtouch and lifelink."
///   "Equip {2}"
///
/// Same grant shape as <see cref="LoxodonWarhammerFactory"/>'s Trample /
/// Lifelink Layer-6 line, minus the P/T boost: Basilisk Collar grants keywords
/// only (Deathtouch + Lifelink), equip cost {2}.
/// </summary>
[Trait("Color", "C")]
public class BasiliskCollarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BasiliskCollar_Identity()
    {
        var c = BasiliskCollarFactory.Create(_alice);

        c.Name.Should().Be("Basilisk Collar");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Basilisk Collar is an Equipment");
        c.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Basilisk Collar is not legendary");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BasiliskCollar_EquipAbility_HasGenericTwoCost()
    {
        var c = BasiliskCollarFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2, "Equip {2} is the printed cost");
    }

    [Fact]
    public void BasiliskCollar_Equipped_Bear_GetsDeathtouchAndLifelink_NoBoost()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var collar = BasiliskCollarFactory.Create(_alice, svc);
        collar.Zone = ZoneType.Battlefield;

        collar.AttachTo(bear);

        bear.GetPower().Should().Be(2, "Basilisk Collar grants no P/T boost");
        bear.GetToughness().Should().Be(2, "Basilisk Collar grants no P/T boost");
        CombatAbilities.HasDeathtouch(bear).Should().BeTrue(
            "Basilisk Collar grants Deathtouch at Layer 6");
        CombatAbilities.HasLifelink(bear).Should().BeTrue(
            "Basilisk Collar grants Lifelink at Layer 6");
    }

    [Fact]
    public void BasiliskCollar_Detach_RevokesKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var collar = BasiliskCollarFactory.Create(_alice, svc);
        collar.Zone = ZoneType.Battlefield;
        collar.AttachTo(bear);

        collar.Unattach();

        CombatAbilities.HasDeathtouch(bear).Should().BeFalse(
            "Deathtouch lapses on detach");
        CombatAbilities.HasLifelink(bear).Should().BeFalse(
            "Lifelink lapses on detach");
    }

    [Fact]
    public void BasiliskCollar_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var collar = BasiliskCollarFactory.Create(_alice, svc);
        collar.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        CombatAbilities.HasDeathtouch(bear).Should().BeFalse(
            "unequipped Collar's grant gates on AttachedTo");
        CombatAbilities.HasLifelink(bear).Should().BeFalse(
            "unequipped Collar's grant gates on AttachedTo");
    }
}

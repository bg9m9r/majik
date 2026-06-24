using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Attacker = Majik.Core.Combat.Attacker;
using CombatAbilities = Majik.Core.Combat.CombatAbilities;
using Creature = Majik.Core.Cards.Creature;
using MtgCombat = Majik.Core.Combat.Combat;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DiamondPickAxeFactory"/> (Outlaws of Thunder
/// Junction, {R}).
///
/// Covers ONLY the card's unique behaviour (the contract test already asserts
/// dispatch + well-formedness):
/// - Identity ({R} mana cost, Artifact + Equipment subtype).
/// - Indestructible keyword marker (CR 702.12).
/// - Equip {2} activated ability shape.
/// - Static +1/+1 on the equipped creature (CR 613 Layer 7c).
/// - Attack trigger: gates on the equipped creature being declared as an
///   attacker (CR 508.1), and on resolution mints one Treasure token
///   (CR 111.10).
/// </summary>
[Trait("Color", "R")]
public class DiamondPickAxeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DiamondPickAxe_Identity()
    {
        var c = DiamondPickAxeFactory.Create(_alice);

        c.Name.Should().Be("Diamond Pick-Axe");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Diamond Pick-Axe is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Indestructible marker — CR 702.12
    // -----------------------------------------------------------------------

    [Fact]
    public void DiamondPickAxe_HasIndestructibleKeyword()
    {
        var c = DiamondPickAxeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Indestructible",
                System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the equipment is printed Indestructible (CR 702.12)");

        CombatAbilities.HasIndestructible(c).Should().BeTrue(
            "the marker is visible to the indestructible SBA helper (704.5g)");
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void DiamondPickAxe_EquipAbility_HasGenericTwoCost()
    {
        var c = DiamondPickAxeFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2, "Equip {2} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effect — +1/+1
    // -----------------------------------------------------------------------

    [Fact]
    public void DiamondPickAxe_Equipped_Bear_Becomes_3_3()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var pick = DiamondPickAxeFactory.Create(
            _alice, svc, triggers: null, zones: null);
        pick.Zone = ZoneType.Battlefield;

        pick.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1 power from Diamond Pick-Axe");
        bear.GetToughness().Should().Be(3, "+1 toughness from Diamond Pick-Axe");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — condition gating
    // -----------------------------------------------------------------------

    [Fact]
    public void DiamondPickAxe_AttackTrigger_GatesOnEquippedCreatureAttacking()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var other = new Creature("Other", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var pick = DiamondPickAxeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pick);
        pick.SetZone(ZoneType.Battlefield);
        pick.AttachTo(bear);

        var trigger = pick.Abilities.OfType<TriggeredAbility>().Single();

        // Equipped Bear is declared as an attacker → matches.
        var combatEquipped = new MtgCombat(_alice, _bob);
        combatEquipped.AddAttacker(new Attacker(bear, _bob));
        trigger.IsTriggered(new AttackersDeclaredEvent(combatEquipped))
            .Should().BeTrue("the equipped creature attacked (CR 508.1)");

        // Only a different (unequipped) creature attacks → does not match.
        var combatOther = new MtgCombat(_alice, _bob);
        combatOther.AddAttacker(new Attacker(other, _bob));
        trigger.IsTriggered(new AttackersDeclaredEvent(combatOther))
            .Should().BeFalse("trigger fires only when the equipped creature attacks");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — resolution mints a Treasure
    // -----------------------------------------------------------------------

    [Fact]
    public void DiamondPickAxe_AttackTrigger_CreatesTreasureToken()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var pick = DiamondPickAxeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pick);
        pick.SetZone(ZoneType.Battlefield);
        pick.AttachTo(bear);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Treasure").Should().Be(0,
                "no Treasure exists before the trigger resolves");

        var trigger = pick.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Treasure").Should().Be(1,
                "the attack trigger mints exactly one Treasure (CR 111.10)");
    }
}

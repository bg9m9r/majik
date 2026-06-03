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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 509.1c / 509.1g / 613.1f — the aura/equipment "all creatures able to
/// block ~ do so" keyword-grant rail. Lure (Aura, {1}{G}{G}) and Nemesis Mask
/// (Equipment, {3}) both GRANT the
/// <see cref="Majik.Core.Combat.CombatAbilities.MustBeBlockedByAllAble"/>
/// marker keyword to their enchanted / equipped host through the same
/// continuous-effects keyword-grant path used by Pacifism-class auras and the
/// Lightning Greaves equipment cycle: the grant registers on attach and is
/// revoked on detach / host-leaves, so <c>Compute(...).Keywords</c> picks it
/// up exactly like the printed marker on Breaker of Armies.
/// </summary>
public class AuraEquipmentMustBlockGrantTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private const string MustBlockKeyword = "MustBeBlockedByAllAble";

    private Creature BearOn(ContinuousEffectsService svc) => new("Bear", "1G", 2, 2)
    {
        Owner = _alice,
        Controller = _alice,
        Zone = ZoneType.Battlefield,
        ActiveEffects = svc,
    };

    // -----------------------------------------------------------------------
    // Lure — Enchantment — Aura {1}{G}{G}
    // -----------------------------------------------------------------------

    [Fact]
    public void Lure_Identity()
    {
        var c = LureFactory.Create(_alice);

        c.Name.Should().Be("Lure");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue("Lure is an Aura");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Lure_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Lure", _alice);

        card.Should().BeOfType<Enchantment>("Lure is an Enchantment — Aura");
        card.Name.Should().Be("Lure");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void Lure_GrantsMustBlockMarker_ToEnchantedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = BearOn(svc);

        CombatAbilities.MustBeBlockedByAllAble(bear).Should().BeFalse(
            "the bear has no printed must-block marker");

        var lure = LureFactory.Create(_alice, svc);
        lure.Zone = ZoneType.Battlefield;
        lure.AttachTo(bear);

        // CR 613 — a continuous keyword grant settles on the following Compute;
        // prime one pass so the assertion reads the settled state.
        svc.Compute(bear);

        svc.Compute(bear).Keywords.Should().Contain(MustBlockKeyword,
            "Lure grants 'all creatures able to block enchanted creature do so' (CR 509.1c)");
        CombatAbilities.MustBeBlockedByAllAble(bear).Should().BeTrue();
    }

    [Fact]
    public void Lure_Detach_RevokesMustBlockMarker()
    {
        var svc = new ContinuousEffectsService();
        var bear = BearOn(svc);

        var lure = LureFactory.Create(_alice, svc);
        lure.Zone = ZoneType.Battlefield;
        lure.AttachTo(bear);
        svc.Compute(bear);
        CombatAbilities.MustBeBlockedByAllAble(bear).Should().BeTrue();

        lure.Unattach();
        svc.Compute(bear);

        CombatAbilities.MustBeBlockedByAllAble(bear).Should().BeFalse(
            "the granted marker is revoked once the aura is no longer attached (CR 613.6e)");
    }

    [Fact]
    public void Lure_Unattached_GrantsNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = BearOn(svc);

        var lure = LureFactory.Create(_alice, svc);
        lure.Zone = ZoneType.Battlefield;
        // intentionally not attached

        svc.Compute(bear).Keywords.Should().NotContain(MustBlockKeyword,
            "an unattached Lure grants nothing");
    }

    // -----------------------------------------------------------------------
    // Nemesis Mask — Artifact — Equipment {3}, Equip {3}
    // -----------------------------------------------------------------------

    [Fact]
    public void NemesisMask_Identity()
    {
        var c = NemesisMaskFactory.Create(_alice);

        c.Name.Should().Be("Nemesis Mask");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue("Nemesis Mask is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NemesisMask_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Nemesis Mask", _alice);

        card.Should().BeOfType<Artifact>("Nemesis Mask is an Artifact — Equipment");
        card.Name.Should().Be("Nemesis Mask");
        card.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    [Fact]
    public void NemesisMask_EquipAbility_HasCostThree()
    {
        var c = NemesisMaskFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(3, "Equip {3} is the printed activation cost");
    }

    [Fact]
    public void NemesisMask_GrantsMustBlockMarker_ToEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = BearOn(svc);

        CombatAbilities.MustBeBlockedByAllAble(bear).Should().BeFalse();

        var mask = NemesisMaskFactory.Create(_alice, svc);
        mask.Zone = ZoneType.Battlefield;
        mask.AttachTo(bear);

        svc.Compute(bear);

        svc.Compute(bear).Keywords.Should().Contain(MustBlockKeyword,
            "Nemesis Mask grants 'all creatures able to block equipped creature do so' (CR 509.1c)");
        CombatAbilities.MustBeBlockedByAllAble(bear).Should().BeTrue();
    }

    [Fact]
    public void NemesisMask_Detach_RevokesMustBlockMarker()
    {
        var svc = new ContinuousEffectsService();
        var bear = BearOn(svc);

        var mask = NemesisMaskFactory.Create(_alice, svc);
        mask.Zone = ZoneType.Battlefield;
        mask.AttachTo(bear);
        svc.Compute(bear);
        CombatAbilities.MustBeBlockedByAllAble(bear).Should().BeTrue();

        mask.Unattach();
        svc.Compute(bear);

        CombatAbilities.MustBeBlockedByAllAble(bear).Should().BeFalse(
            "the granted marker is revoked once the equipment is no longer attached (CR 613.6e)");
    }

    [Fact]
    public void NemesisMask_GrantThenBlock_ForcesAbleBlocker()
    {
        // End-to-end: a Nemesis Mask'd attacker is treated as a must-block
        // attacker by the combat validator (the rail feeds the same
        // MustBeBlockedByAllAble enforcement Breaker of Armies uses).
        var svc = new ContinuousEffectsService();
        var attacker = new Creature("Goblin", "1R", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var mask = NemesisMaskFactory.Create(_alice, svc);
        mask.Zone = ZoneType.Battlefield;
        mask.AttachTo(attacker);
        svc.Compute(attacker);

        var atk = new Attacker(attacker, _bob);
        var defender = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var validator = new CombatValidator();

        // Not assigning the able blocker is illegal (CR 509.1c).
        validator.IsValidBlockDeclaration(
                new List<(Creature, Attacker)>(), _bob, new[] { atk }, new[] { defender })
            .Should().BeFalse("the masked attacker must be blocked by every able creature");

        // Assigning it satisfies the requirement.
        validator.IsValidBlockDeclaration(
                new List<(Creature, Attacker)> { (defender, atk) }, _bob, new[] { atk }, new[] { defender })
            .Should().BeTrue();
    }
}

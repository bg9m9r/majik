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
/// Unit tests for <see cref="ShadowspearFactory"/>.
///
/// Card: Shadowspear — Legendary Artifact — Equipment {1} (Theros Beyond
/// Death).
///   "{1}, {T}: Target creature loses indestructible and hexproof until
///    end of turn."
///   "Equipped creature gets +1/+1 and has trample and lifelink."
///   "Equip {1}."
/// </summary>
[Trait("Color", "C")]
public class ShadowspearFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Shadowspear_Identity()
    {
        var c = ShadowspearFactory.Create(_alice);

        c.Name.Should().Be("Shadowspear");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Shadowspear is Legendary");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Shadowspear_EquipAbility_HasGenericOneCost()
    {
        var c = ShadowspearFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1, "Equip {1} is the printed cost");
    }

    [Fact]
    public void Shadowspear_Equipped_Bear_GetsPlusOnePlusOneAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var spear = ShadowspearFactory.Create(_alice, svc);
        spear.Zone = ZoneType.Battlefield;

        spear.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1/+1 boost from Shadowspear");
        bear.GetToughness().Should().Be(3);
        CombatAbilities.HasTrample(bear).Should().BeTrue(
            "Shadowspear grants Trample at Layer 6");
        CombatAbilities.HasLifelink(bear).Should().BeTrue(
            "Shadowspear grants Lifelink at Layer 6");
    }

    [Fact]
    public void Shadowspear_Detach_RevokesBoostAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var spear = ShadowspearFactory.Create(_alice, svc);
        spear.Zone = ZoneType.Battlefield;
        spear.AttachTo(bear);

        spear.Unattach();

        bear.GetPower().Should().Be(2, "boost lapses on detach");
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasTrample(bear).Should().BeFalse();
        CombatAbilities.HasLifelink(bear).Should().BeFalse();
    }

    [Fact]
    public void Shadowspear_KeywordStrip_RemovesIndestructibleAndHexproof_UntilEOT()
    {
        var svc = new ContinuousEffectsService();
        // Bob's creature with both Indestructible + Hexproof printed as
        // KeywordAbility markers, AND surfaced through the layer system via
        // ActiveEffects (CombatAbilities reads from the working set when
        // ActiveEffects is wired).
        var target = new Creature("Holy Critter", "2WW", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        target.AddAbility(new KeywordAbility("Indestructible", target, _bob));
        target.AddAbility(new KeywordAbility("Hexproof", target, _bob));

        var spear = ShadowspearFactory.Create(_alice, svc);
        spear.Zone = ZoneType.Battlefield;

        // Sanity — the printed markers do surface via the layer system's
        // printed-keyword pre-pass.
        CombatAbilities.HasIndestructible(target).Should().BeTrue();

        // Find the keyword-strip activated ability: 1..1 "target creature"
        // request + {1} mana cost (not the Equip ability, which is a
        // strongly-typed EquipActivatedAbility).
        var stripAbility = spear.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not EquipActivatedAbility)
            .Single(a =>
                a.TargetRequests.Count == 1 &&
                a.TargetRequests[0].Description == "target creature");

        stripAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in stripAbility.Effects) e.Execute();

        // After the activation, both keywords are stripped EOT.
        var keywords = svc.Compute(target).Keywords;
        keywords.Contains("Indestructible").Should().BeFalse(
            "Shadowspear's activated ability strips Indestructible EOT");
        keywords.Contains("Hexproof").Should().BeFalse(
            "Shadowspear's activated ability strips Hexproof EOT");

        // EOT expiry returns the keywords (CR 514.2 cleanup).
        svc.ExpireEndOfTurn();
        var afterEot = svc.Compute(target).Keywords;
        afterEot.Contains("Indestructible").Should().BeTrue(
            "Indestructible returns after end of turn");
        afterEot.Contains("Hexproof").Should().BeTrue(
            "Hexproof returns after end of turn");
    }

    [Fact]
    public void Shadowspear_KeywordStrip_HasManaAndTapCosts()
    {
        var spear = ShadowspearFactory.Create(_alice);
        var stripAbility = spear.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not EquipActivatedAbility)
            .Single(a =>
                a.TargetRequests.Count == 1 &&
                a.TargetRequests[0].Description == "target creature");

        var mana = stripAbility.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Generic.Should().Be(1, "{1} is the mana cost of the strip");

        stripAbility.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            "Tap is the only additional cost");
    }
}

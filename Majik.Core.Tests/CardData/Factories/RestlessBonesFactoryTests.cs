using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="RestlessBonesFactory"/>.
///
/// Card: Restless Bones — Creature — Skeleton {2}{B} 1/1 (verified against
/// Scryfall 2026-06-02).
///   "{3}{B}, {T}: Target creature gains swampwalk until end of turn.
///    (It can't be blocked as long as defending player controls a Swamp.)"
///   "{1}{B}: Regenerate this creature."
/// </summary>
[Trait("Color", "B")]
public class RestlessBonesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RestlessBones_Identity()
    {
        var c = RestlessBonesFactory.Create(_alice);

        c.Name.Should().Be("Restless Bones");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Skeleton).Should().BeTrue();
        c.GetPower().Should().Be(1);
        c.GetToughness().Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RestlessBones_Swampwalk_AbilityHasManaAndTapCosts()
    {
        var c = RestlessBonesFactory.Create(_alice);

        var swampwalk = c.Abilities.OfType<ActivatedAbility>()
            .Single(a =>
                a.TargetRequests.Count == 1 &&
                a.TargetRequests[0].Description == "target creature");

        var mana = swampwalk.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Generic.Should().Be(3, "{3} is the generic part of {3}{B}");

        swampwalk.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            "Tap is the only additional cost ({T})");
    }

    [Fact]
    public void RestlessBones_Regenerate_AbilityHasManaCostOneBlack()
    {
        var c = RestlessBonesFactory.Create(_alice);

        // Regenerate ability — no target, no tap, single {1}{B} mana cost.
        var regen = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        var mana = regen.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Generic.Should().Be(1, "{1} is the generic part of {1}{B}");
        regen.Costs.OfType<AdditionalCost>().Should().BeEmpty(
            "regenerate has no tap / additional cost");
    }

    [Fact]
    public void RestlessBones_Regenerate_AddsRegenerationShield()
    {
        var c = RestlessBonesFactory.Create(_alice);
        c.Zone = ZoneType.Battlefield;

        c.HasRegenerationShield.Should().BeFalse("no shield before activation");

        var regen = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);
        foreach (var e in regen.Effects) e.Execute();

        c.HasRegenerationShield.Should().BeTrue(
            "{1}{B}: Regenerate this creature adds a regeneration shield (CR 701.15)");
        c.RegenerationShieldCount.Should().Be(1);
    }

    [Fact]
    public void RestlessBones_Swampwalk_GrantsSwampwalkToTarget_UntilEOT()
    {
        var svc = new ContinuousEffectsService();
        var bones = RestlessBonesFactory.Create(_alice, svc);
        bones.Zone = ZoneType.Battlefield;

        var target = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var swampwalk = bones.Abilities.OfType<ActivatedAbility>()
            .Single(a =>
                a.TargetRequests.Count == 1 &&
                a.TargetRequests[0].Description == "target creature");

        swampwalk.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in swampwalk.Effects) e.Execute();

        var keywords = svc.Compute(target).Keywords;
        keywords.Contains("Swampwalk").Should().BeTrue(
            "Restless Bones grants Swampwalk to the target until EOT");

        // EOT expiry removes the granted keyword (CR 514.2 cleanup).
        svc.ExpireEndOfTurn();
        svc.Compute(target).Keywords.Contains("Swampwalk").Should().BeFalse(
            "Swampwalk lapses after end of turn");
    }
}

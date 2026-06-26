using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Regression tests for <see cref="SacrificeAnotherCreatureCost"/> covering
/// the animated-land sacrifice bug (issue #3505): a non-Creature permanent that
/// is effectively a creature (e.g. a Land animated by Badgermole Cub's earthbend
/// ability or a manland) must appear in
/// <see cref="SacrificeAnotherCreatureCost.EligibleSacrifices"/> and be
/// payable as the sacrifice cost.
/// </summary>
public class SacrificeAnotherCreatureCostTests
{
    /// <summary>
    /// A permanent that has both Land and Creature card types, simulating a land
    /// animated into a creature (e.g. via Badgermole Cub's earthbend ability).
    /// Using a raw <see cref="Permanent"/> with explicit card types avoids a full
    /// ContinuousEffectsService setup: when ActiveEffects is null,
    /// <see cref="Permanent.IsEffectivelyCreature"/> falls back to the printed
    /// CardTypes, which include CardType.Creature here.
    /// </summary>
    private static Permanent AnimatedLand(string name, Player owner)
    {
        var p = new Permanent(name, "", new[] { CardType.Land, CardType.Creature })
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(p);
        return p;
    }

    private static Creature ACreature(string name, Player owner)
    {
        var c = new Creature(name, "G", 1, 1)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Land ALand(string name, Player owner)
    {
        var l = new Land(name)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(l);
        return l;
    }

    // -------------------------------------------------------------------------
    // EligibleSacrifices
    // -------------------------------------------------------------------------

    [Fact]
    public void EligibleSacrifices_IncludesAnimatedLand()
    {
        var alice = new Player("Alice", 20);
        var yawgmoth = ACreature("Yawgmoth, Thran Physician", alice);
        var animatedLand = AnimatedLand("Animated Forest", alice);

        var cost = new SacrificeAnotherCreatureCost(yawgmoth);

        cost.EligibleSacrifices(alice).Should().Contain(animatedLand,
            "a land animated into a creature is an eligible sacrifice target (CR 701.16)");
    }

    [Fact]
    public void EligibleSacrifices_ExcludesPlainLand()
    {
        var alice = new Player("Alice", 20);
        var yawgmoth = ACreature("Yawgmoth, Thran Physician", alice);
        _ = ALand("Forest", alice);

        var cost = new SacrificeAnotherCreatureCost(yawgmoth);

        cost.EligibleSacrifices(alice).Should().BeEmpty(
            "a plain land that is not currently a creature cannot be sacrificed");
    }

    [Fact]
    public void EligibleSacrifices_IncludesRegularCreature()
    {
        var alice = new Player("Alice", 20);
        var yawgmoth = ACreature("Yawgmoth, Thran Physician", alice);
        var bear = ACreature("Bear", alice);

        var cost = new SacrificeAnotherCreatureCost(yawgmoth);

        cost.EligibleSacrifices(alice).Should().Contain((Permanent)bear);
    }

    [Fact]
    public void EligibleSacrifices_ExcludesSource()
    {
        var alice = new Player("Alice", 20);
        var yawgmoth = ACreature("Yawgmoth, Thran Physician", alice);
        _ = ACreature("Bear", alice);

        var cost = new SacrificeAnotherCreatureCost(yawgmoth);

        cost.EligibleSacrifices(alice).Should().NotContain((Permanent)yawgmoth,
            "Yawgmoth cannot sacrifice itself");
    }

    // -------------------------------------------------------------------------
    // CanPay
    // -------------------------------------------------------------------------

    [Fact]
    public void CanPay_ReturnsTrueWhenOnlyAnimatedLandAvailable()
    {
        var alice = new Player("Alice", 20);
        var yawgmoth = ACreature("Yawgmoth, Thran Physician", alice);
        _ = AnimatedLand("Animated Forest", alice);

        var cost = new SacrificeAnotherCreatureCost(yawgmoth);

        cost.CanPay(alice).Should().BeTrue(
            "an animated land counts as a creature for paying sacrifice costs");
    }

    [Fact]
    public void CanPay_ReturnsFalseWhenOnlyPlainLandAvailable()
    {
        var alice = new Player("Alice", 20);
        var yawgmoth = ACreature("Yawgmoth, Thran Physician", alice);
        _ = ALand("Forest", alice);

        var cost = new SacrificeAnotherCreatureCost(yawgmoth);

        cost.CanPay(alice).Should().BeFalse(
            "a plain land is not a creature; no sacrifice cost can be paid");
    }

    // -------------------------------------------------------------------------
    // Pay / Sacrificed
    // -------------------------------------------------------------------------

    [Fact]
    public void Pay_SacrificesAnimatedLand_WhenStampedAsTarget()
    {
        var alice = new Player("Alice", 20);
        var yawgmoth = ACreature("Yawgmoth, Thran Physician", alice);
        var animatedLand = AnimatedLand("Animated Forest", alice);

        var cost = new SacrificeAnotherCreatureCost(yawgmoth);
        cost.ChooseSacrifice(animatedLand);
        cost.Pay(alice);

        animatedLand.Zone.Should().Be(ZoneType.Graveyard,
            "paying the cost moves the animated land to the graveyard");
        cost.Sacrificed.Should().BeSameAs(animatedLand);
    }

    [Fact]
    public void Pay_FallbackPicksAnimatedLand_WhenNoCreaturePresent()
    {
        var alice = new Player("Alice", 20);
        var yawgmoth = ACreature("Yawgmoth, Thran Physician", alice);
        var animatedLand = AnimatedLand("Animated Forest", alice);

        var cost = new SacrificeAnotherCreatureCost(yawgmoth);
        // No ChooseSacrifice call — exercises the deterministic fallback path
        cost.Pay(alice);

        animatedLand.Zone.Should().Be(ZoneType.Graveyard,
            "the fallback auto-picker must also consider animated lands");
        cost.Sacrificed.Should().BeSameAs(animatedLand);
    }
}

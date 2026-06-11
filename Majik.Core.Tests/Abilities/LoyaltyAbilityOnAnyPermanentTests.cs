using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Sub-slice 4A — loyalty abilities decoupled from the concrete
/// <see cref="Planeswalker"/> subclass. A non-planeswalker permanent carrying
/// a transient loyalty body (CR 711, e.g. a creature-front DFC flipped to its
/// planeswalker back) can host + pay loyalty abilities through the same
/// <see cref="Permanent"/>-level surface a real planeswalker uses.
/// </summary>
public class LoyaltyAbilityOnAnyPermanentTests
{
    private static Creature MakeCreatureWithLoyalty(int loyalty)
    {
        var c = new Creature("Flipwalker", "{3}", power: 2, toughness: 2);
        c.SetTransientLoyalty(loyalty);
        return c;
    }

    [Fact]
    public void Creature_WithTransientLoyalty_PlusAbility_RaisesEffectiveLoyalty()
    {
        var c = MakeCreatureWithLoyalty(4);
        var ran = false;
        var plus1 = new LoyaltyAbility(c, +1, () => ran = true);

        c.GetEffectiveLoyalty().Should().Be(4);
        plus1.CanActivate().Should().BeTrue();

        plus1.PayLoyaltyCost();

        c.GetEffectiveLoyalty().Should().Be(5);
        c.LoyaltyAbilityActivatedThisTurn.Should().BeTrue();
        ran.Should().BeFalse("PayLoyaltyCost only pays the cost; effects resolve off the stack");
    }

    [Fact]
    public void Creature_WithTransientLoyalty_MinusAbility_LowersEffectiveLoyalty()
    {
        var c = MakeCreatureWithLoyalty(4);
        var minus3 = new LoyaltyAbility(c, -3, () => { });

        minus3.CanActivate().Should().BeTrue();
        minus3.PayLoyaltyCost();

        c.GetEffectiveLoyalty().Should().Be(1);
    }

    [Fact]
    public void Creature_WithTransientLoyalty_MinusBelowZero_IsIllegal()
    {
        var c = MakeCreatureWithLoyalty(2);
        var minus3 = new LoyaltyAbility(c, -3, () => { });

        minus3.CanActivate().Should().BeFalse();
        var act = () => minus3.PayLoyaltyCost();
        act.Should().Throw<InvalidOperationException>();
        c.GetEffectiveLoyalty().Should().Be(2, "an illegal activation must not mutate loyalty");
    }

    [Fact]
    public void Creature_WithTransientLoyalty_OncePerTurn_BlocksSecondActivation()
    {
        var c = MakeCreatureWithLoyalty(4);
        var plus1 = new LoyaltyAbility(c, +1, () => { });
        var minus1 = new LoyaltyAbility(c, -1, () => { });

        plus1.PayLoyaltyCost();

        minus1.CanActivate().Should().BeFalse("CR 606.5 — one loyalty ability per permanent per turn");
    }

    [Fact]
    public void Permanent_AddTransientLoyalty_RaisesBody()
    {
        var c = MakeCreatureWithLoyalty(3);
        c.AddTransientLoyalty(2);
        c.GetEffectiveLoyalty().Should().Be(5);
    }

    [Fact]
    public void Permanent_AddTransientLoyalty_NoBody_IsNoOp()
    {
        var c = new Creature("Plain", "{1}", power: 1, toughness: 1);
        c.AddTransientLoyalty(2);
        c.GetEffectiveLoyalty().Should().BeNull();
        c.IsEffectivePlaneswalker().Should().BeFalse();
    }

    // --- real planeswalker must behave identically (one source of truth) ---

    [Fact]
    public void RealPlaneswalker_LoyaltyAbility_StillMutatesAuthoritativeLoyalty()
    {
        var pw = new Planeswalker("Real PW", "{3}", startingLoyalty: 4);
        var plus1 = new LoyaltyAbility(pw, +1, () => { });
        var minus3 = new LoyaltyAbility(pw, -3, () => { });

        pw.GetEffectiveLoyalty().Should().Be(4);
        plus1.PayLoyaltyCost();
        pw.Loyalty.Should().Be(5, "the authoritative field stays the source of truth");
        pw.GetEffectiveLoyalty().Should().Be(5);

        // fresh turn for a clean second activation
        pw.LoyaltyAbilityActivatedThisTurn = false;
        minus3.PayLoyaltyCost();
        pw.Loyalty.Should().Be(2);
        pw.GetEffectiveLoyalty().Should().Be(2);
    }

    [Fact]
    public void RealPlaneswalker_MinusBelowZero_IsIllegal()
    {
        var pw = new Planeswalker("Real PW", "{3}", startingLoyalty: 2);
        var minus3 = new LoyaltyAbility(pw, -3, () => { });

        minus3.CanActivate().Should().BeFalse();
        pw.Loyalty.Should().Be(2);
    }
}

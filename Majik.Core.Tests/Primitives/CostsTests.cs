using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Xunit;
// The Majik.Core.Costs namespace (imported above for AdditionalCost,
// ManaCostCost, …) shadows the Majik.Core.Primitives.Costs *class* under
// test, so reach it via an alias. Same collision the Fx class doc warns
// about; the alias sidesteps it here.
using CostFx = Majik.Core.Primitives.Costs;

namespace Majik.Core.Tests.Primitives;

/// <summary>
/// Unit tests for the <see cref="Costs"/> cost-primitive facade (PLAN 03
/// S1). Each helper must produce the same cost object the JSON
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> used
/// to inline — same concrete type, same <see cref="ICost.Description"/>,
/// same null-arg guards.
/// </summary>
public class CostsTests
{
    private static Creature MakeCreature(string name = "Walking Ballista") =>
        new(name, "{X}{X}", 0, 0);

    // ------------------------------------------------------------------
    // TapSelf — wraps AdditionalCost.Tap.
    // ------------------------------------------------------------------

    [Fact]
    public void TapSelf_ProducesTapAdditionalCost()
    {
        var perm = MakeCreature();
        var cost = CostFx.TapSelf(perm);

        cost.Should().BeOfType<AdditionalCost>();
        ((AdditionalCost)cost).CostType.Should().Be(AdditionalCostType.Tap);
        cost.Description.Should().Be($"Tap {perm.Name}");
    }

    [Fact]
    public void TapSelf_ParityWithInlinedAdditionalCostTap()
    {
        var perm = MakeCreature();
        var viaHelper = CostFx.TapSelf(perm);
        var inlined = AdditionalCost.Tap(perm);

        viaHelper.Should().BeOfType<AdditionalCost>();
        ((AdditionalCost)viaHelper).CostType.Should().Be(((AdditionalCost)inlined).CostType);
        viaHelper.Description.Should().Be(inlined.Description);
    }

    [Fact]
    public void TapSelf_NullPermanent_Throws()
    {
        Action act = () => CostFx.TapSelf(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // SacrificeSelf — wraps AdditionalCost.Sacrifice.
    // ------------------------------------------------------------------

    [Fact]
    public void SacrificeSelf_ProducesSacrificeAdditionalCost()
    {
        var perm = MakeCreature("Gingerbrute");
        var cost = CostFx.SacrificeSelf(perm);

        cost.Should().BeOfType<AdditionalCost>();
        ((AdditionalCost)cost).CostType.Should().Be(AdditionalCostType.Sacrifice);
        cost.Description.Should().Be($"Sacrifice {perm.Name}");
    }

    [Fact]
    public void SacrificeSelf_NullPermanent_Throws()
    {
        Action act = () => CostFx.SacrificeSelf(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // Mana — wraps ManaCostCost.
    // ------------------------------------------------------------------

    [Fact]
    public void Mana_FromString_ProducesManaCostCost()
    {
        var cost = CostFx.Mana("{1}{R}");

        cost.Should().BeOfType<ManaCostCost>();
        cost.Description.Should().Be(new ManaCostCost("{1}{R}").Description);
    }

    [Fact]
    public void Mana_EmptyString_IsZeroCost()
    {
        var cost = CostFx.Mana("");
        cost.Should().BeOfType<ManaCostCost>();
        ((ManaCostCost)cost).Cost.Should().Be(Majik.Core.ValueObjects.ManaCost.Zero);
    }

    [Fact]
    public void Mana_FromValueObject_ProducesManaCostCost()
    {
        var mc = Majik.Core.ValueObjects.ManaCost.Parse("{2}{G}");
        var cost = CostFx.Mana(mc);

        cost.Should().BeOfType<ManaCostCost>();
        ((ManaCostCost)cost).Cost.Should().Be(mc);
    }

    // ------------------------------------------------------------------
    // RemovePlusOnePlusOneCounter — wraps RemovePlusOnePlusOneCounterCost.
    // ------------------------------------------------------------------

    [Fact]
    public void RemovePlusOnePlusOneCounter_ProducesCorrectCost()
    {
        var perm = MakeCreature();
        var cost = CostFx.RemovePlusOnePlusOneCounter(perm);

        cost.Should().BeOfType<RemovePlusOnePlusOneCounterCost>();
        ((RemovePlusOnePlusOneCounterCost)cost).Amount.Should().Be(1);
        cost.Description.Should().Be($"Remove a +1/+1 counter from {perm.Name}");
    }

    [Fact]
    public void RemovePlusOnePlusOneCounter_WithAmount_MatchesInlined()
    {
        var perm = MakeCreature();
        var viaHelper = CostFx.RemovePlusOnePlusOneCounter(perm, 2);
        var inlined = new RemovePlusOnePlusOneCounterCost(perm, 2);

        viaHelper.Should().BeOfType<RemovePlusOnePlusOneCounterCost>();
        ((RemovePlusOnePlusOneCounterCost)viaHelper).Amount.Should().Be(2);
        viaHelper.Description.Should().Be(inlined.Description);
    }

    [Fact]
    public void RemovePlusOnePlusOneCounter_NullSource_Throws()
    {
        Action act = () => CostFx.RemovePlusOnePlusOneCounter(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // DiscardSelf — wraps DiscardSelfCost.
    // ------------------------------------------------------------------

    [Fact]
    public void DiscardSelf_ProducesDiscardSelfCost()
    {
        ICard card = MakeCreature("Boseiju, Who Endures");
        var cost = CostFx.DiscardSelf(card);

        cost.Should().BeOfType<DiscardSelfCost>();
        cost.Description.Should().Be(new DiscardSelfCost(card).Description);
    }

    [Fact]
    public void DiscardSelf_NullCard_Throws()
    {
        Action act = () => CostFx.DiscardSelf(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

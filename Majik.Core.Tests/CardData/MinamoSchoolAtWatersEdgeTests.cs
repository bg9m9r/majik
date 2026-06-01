using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MinamoSchoolAtWatersEdgeFactory"/>.
///
/// Minamo, School at Water's Edge (Champions of Kamigawa) — Legendary Land.
/// Oracle text:
///   "{T}: Add {U}.
///    {U}, {T}: Untap target legendary permanent."
///
/// Covers:
/// - Card identity (name, Legendary supertype, Land type)
/// - {T}: Add {U} mana ability (presence + blue output)
/// - {U}, {T} activated ability cost composition (ManaCostCost({U}) + Tap)
/// - The untap-target effect resolves without throwing when no target is
///   chosen (CR 608.2b fizzle). Full chosen-target untap behaviour is covered
///   end-to-end in JsonTargetingEffectsTests (PLAN 01 Slice F).
/// </summary>
public class MinamoSchoolAtWatersEdgeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Minamo_IsLegendary()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);

        minamo.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Minamo_IsLand()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);

        minamo.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void Minamo_OwnerAndControllerAreSet()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);

        minamo.Owner.Should().BeSameAs(_alice);
        minamo.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {U} mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Minamo_HasExactlyOneManaAbility()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);

        minamo.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Minamo_ManaAbility_ProducesBlue()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);
        var mana = minamo.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.Blue.Should().Be(1, "Minamo taps for exactly one {U}");
        mana.ManaGenerated.Generic.Should().Be(0, "no colorless component");
    }

    // -----------------------------------------------------------------------
    // {U}, {T}: Untap target legendary permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void Minamo_HasExactlyOneActivatedAbility()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);

        minamo.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "only the untap ability; the mana ability is a ManaAbility, not ActivatedAbility");
    }

    [Fact]
    public void Minamo_UntapAbility_HasManaCostCost()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);
        var ability = minamo.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Minamo_UntapAbility_ManaCostIsU()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);
        var ability = minamo.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        manaCost.Blue.Should().Be(1, "the {U} component");
        manaCost.Generic.Should().Be(0, "no generic component");
    }

    [Fact]
    public void Minamo_UntapAbility_HasTapSelfCost()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);
        var ability = minamo.Abilities.OfType<ActivatedAbility>().Single();

        // The {T} symbol is built as an AdditionalCost.Tap on the source.
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle("the {T} symbol composes a tap-self additional cost");
    }

    [Fact]
    public void Minamo_UntapAbility_HasExactlyTwoCosts()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);
        var ability = minamo.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "ManaCostCost({U}) + tap-self");
    }

    // -----------------------------------------------------------------------
    // Untap-target effect (PLAN 01 Slice F — real targeting).
    // -----------------------------------------------------------------------

    [Fact]
    public void Minamo_UntapAbility_DeclaresOneTargetRequest()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);
        var ability = minamo.Abilities.OfType<ActivatedAbility>().Single();

        ability.TargetRequests.Should().ContainSingle(
            "untap target legendary permanent declares one 1..1 target");
    }

    [Fact]
    public void Minamo_UntapAbility_NoTargetChosen_ResolvesWithoutThrowing()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);
        var ability = minamo.Abilities.OfType<ActivatedAbility>().Single();

        // No ChosenTargets set → CR 608.2b fizzle (clean no-op, no throw).
        var act = () => ability.Resolve();

        act.Should().NotThrow("an unfilled target fizzles cleanly");
    }
}

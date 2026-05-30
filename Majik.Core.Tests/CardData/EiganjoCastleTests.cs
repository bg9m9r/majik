using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EiganjoCastleFactory"/>.
///
/// Eiganjo Castle (Champions of Kamigawa) — Legendary Land.
/// Oracle text:
///   "{T}: Add {W}.
///    {W}, {T}: Prevent the next 2 damage that would be dealt to target
///     legendary creature this turn."
///
/// Structural twin of Minamo, School at Water's Edge — Legendary Land with a
/// mana ability plus a "{cost}, {T}: do-thing-to-target-legendary" activated
/// ability. The damage-prevention effect resolves as a no-op stub because the
/// targeting/prompt system isn't wired yet (mirrors Minamo's untap_target_stub
/// and Boseiju's destroy_target_stub).
///
/// Covers:
/// - Card identity (name, Legendary supertype, Land type)
/// - {T}: Add {W} mana ability (presence + white output)
/// - {W}, {T} activated ability cost composition (ManaCostCost({W}) + Tap)
/// - The prevent-damage effect resolves without throwing (stub).
/// </summary>
public class EiganjoCastleTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_IsLegendary()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);

        eiganjo.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Eiganjo_IsLand()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);

        eiganjo.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void Eiganjo_OwnerAndControllerAreSet()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);

        eiganjo.Owner.Should().BeSameAs(_alice);
        eiganjo.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {W} mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_HasExactlyOneManaAbility()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);

        eiganjo.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Eiganjo_ManaAbility_ProducesWhite()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);
        var mana = eiganjo.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.White.Should().Be(1, "Eiganjo Castle taps for exactly one {W}");
        mana.ManaGenerated.Generic.Should().Be(0, "no colorless component");
    }

    // -----------------------------------------------------------------------
    // {W}, {T}: Prevent the next 2 damage to target legendary creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_HasExactlyOneActivatedAbility()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);

        eiganjo.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "only the prevent ability; the mana ability is a ManaAbility, not ActivatedAbility");
    }

    [Fact]
    public void Eiganjo_PreventAbility_HasManaCostCost()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Eiganjo_PreventAbility_ManaCostIsW()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        manaCost.White.Should().Be(1, "the {W} component");
        manaCost.Generic.Should().Be(0, "no generic component");
    }

    [Fact]
    public void Eiganjo_PreventAbility_HasTapSelfCost()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        // The {T} symbol is built as an AdditionalCost.Tap on the source.
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle("the {T} symbol composes a tap-self additional cost");
    }

    [Fact]
    public void Eiganjo_PreventAbility_HasExactlyTwoCosts()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "ManaCostCost({W}) + tap-self");
    }

    // -----------------------------------------------------------------------
    // Prevent-damage effect resolve (stub — targeting not wired yet)
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_PreventAbility_ResolvesWithoutThrowing()
    {
        var eiganjo = EiganjoCastleFactory.Create(_alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => ability.Resolve();

        act.Should().NotThrow("v1 prevent-damage-target effect is a no-op stub");
    }
}

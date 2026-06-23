using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PristineTalismanFactory"/>.
///
/// Pristine Talisman — Artifact, {3}. Oracle text:
///   "{T}: Add {C}. You gain 1 life."
///
/// Covers the card's UNIQUE behaviour:
/// - A single {C} mana ability (CR 605.1 — mana abilities don't use the
///   stack; {C} folds into the generic bucket).
/// - The "you gain 1 life" rider: activating the mana ability adds 1 to the
///   controller's life total (CR 605.1b / CR 119.3) and taps the source.
/// - No life-floor gate — gaining life is always legal, so the ability is
///   activatable at any life total.
/// Plus a single identity assert for the printed mana cost ({3}).
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests — not re-tested here.)
/// </summary>
[Trait("Color", "C")]
public class PristineTalismanFactoryTests
{
    [Fact]
    public void PristineTalisman_Identity_IsArtifactWithCorrectCost()
    {
        var alice = new Player("Alice", 20);

        var talisman = PristineTalismanFactory.Create(alice);

        talisman.Should().BeOfType<Artifact>();
        talisman.HasType(CardType.Artifact).Should().BeTrue();
        talisman.Name.Should().Be("Pristine Talisman");
        talisman.ManaCost.Should().Be("{3}");
        talisman.Owner.Should().BeSameAs(alice);
        talisman.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void PristineTalisman_HasSingleColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var talisman = PristineTalismanFactory.Create(alice);

        var mana = talisman.Abilities.OfType<ManaAbility>().Should().ContainSingle()
            .Subject;
        // {C} parses to one generic mana — no WUBRG colour.
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Black.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
        mana.ManaGenerated.Generic.Should().Be(1);
    }

    [Fact]
    public void PristineTalisman_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var talisman = PristineTalismanFactory.Create(alice);

        talisman.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only non-mana effect (gain 1 life) rides on the mana ability");
        talisman.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void PristineTalisman_Activation_GainsOneLife_AndTaps()
    {
        var alice = new Player("Alice", 20);
        var talisman = PristineTalismanFactory.Create(alice);
        var mana = talisman.Abilities.OfType<ManaAbility>().Single();

        var produced = mana.Activate();

        produced.Generic.Should().Be(1, "{T}: Add {C}");
        alice.LifeTotal.Should().Be(21, "You gain 1 life");
        talisman.IsTapped.Should().BeTrue("the {T} cost taps the source");
    }

    [Fact]
    public void PristineTalisman_CannotActivateOnceTapped()
    {
        var alice = new Player("Alice", 20);
        var talisman = PristineTalismanFactory.Create(alice);
        var mana = talisman.Abilities.OfType<ManaAbility>().Single();

        mana.Activate();

        mana.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    [Fact]
    public void PristineTalisman_GainLife_HasNoLifeFloorGate()
    {
        // Distinct from Horizon Canopy "Pay 1 life": gaining life has no
        // payment prerequisite (CR 119.3), so the ability is activatable
        // even at 1 life.
        var alice = new Player("Alice", 1);
        var talisman = PristineTalismanFactory.Create(alice);
        var mana = talisman.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();

        mana.Activate();
        alice.LifeTotal.Should().Be(2, "gaining life is always legal");
    }

    [Fact]
    public void PristineTalisman_Create_ThrowsOnNullOwner()
    {
        var act = () => PristineTalismanFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

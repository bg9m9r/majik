using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AetherHubFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary).
/// - ETB trigger granting controller 1 energy + stamping an
///   Energy counter on the land (CR 603.6a / CR 106.13).
/// - {T}: Add {C} mana ability shape.
/// - {T}, Pay {E}: Add one mana of any color — five WUBRG
///   ManaAbility instances, each gated on EnergyCounters &gt;= 1
///   (CR 119.4); activation spends energy + taps the land; cannot
///   activate when controller has zero energy or land is tapped.
/// - NamedCardFactory dispatcher resolves "Aether Hub" to the
///   expected Land shape with all 6 mana abilities + the ETB trigger.
/// </summary>
public class AetherHubTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherHub_IsLand()
    {
        var land = AetherHubFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void AetherHub_NameIsCorrect()
    {
        var land = AetherHubFactory.Create(_alice);

        land.Name.Should().Be("Aether Hub");
    }

    [Fact]
    public void AetherHub_OwnerAndControllerAreSet()
    {
        var land = AetherHubFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AetherHub_IsNotLegendary()
    {
        var land = AetherHubFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ETB trigger — "enters with an energy counter on it"
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherHub_HasExactlyOneEtbTriggeredAbility()
    {
        var land = AetherHubFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"enters with an energy counter\" trigger");
    }

    [Fact]
    public void AetherHub_EtbEffect_GrantsControllerOneEnergy()
    {
        var alice = new Player("Alice", 20);
        var land = AetherHubFactory.Create(alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        alice.EnergyCounters.Should().Be(0, "controller starts with no energy");

        foreach (var effect in etb.Effects) effect.Execute();

        alice.EnergyCounters.Should().Be(1,
            "ETB grants the controller one energy (CR 106.13)");
    }

    [Fact]
    public void AetherHub_EtbEffect_StampsEnergyCounterOnLand()
    {
        var alice = new Player("Alice", 20);
        var land = AetherHubFactory.Create(alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        land.Counters.Count(CounterType.Energy).Should().Be(1,
            "the printed \"enters with an energy counter on it\" wording is " +
            "also surfaced as an on-card marker for shape inspection");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherHub_HasExactlySixManaAbilities()
    {
        var land = AetherHubFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "one {T}: Add {C} + five {T}, Pay {E}: Add one mana of any color (WUBRG)");
    }

    [Fact]
    public void AetherHub_HasColorlessManaAbility()
    {
        var land = AetherHubFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().Contain(m => m.ManaGenerated.Generic == 1
                                 && m.ManaGenerated.White == 0
                                 && m.ManaGenerated.Blue == 0
                                 && m.ManaGenerated.Black == 0
                                 && m.ManaGenerated.Red == 0
                                 && m.ManaGenerated.Green == 0,
                "{T}: Add {C} — {C} folds into the generic bucket per ManaCost.Parse");
    }

    [Fact]
    public void AetherHub_ColorlessManaAbility_DoesNotRequireEnergy()
    {
        var alice = new Player("Alice", 20);
        var land = AetherHubFactory.Create(alice);
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.White == 0
                       && m.ManaGenerated.Blue == 0 && m.ManaGenerated.Black == 0
                       && m.ManaGenerated.Red == 0 && m.ManaGenerated.Green == 0);

        alice.EnergyCounters.Should().Be(0);
        colorless.CanActivate().Should().BeTrue(
            "{T}: Add {C} has no energy cost — pays no {E}");
    }

    [Fact]
    public void AetherHub_ColorlessManaActivation_TapsTheLand()
    {
        var alice = new Player("Alice", 20);
        var land = AetherHubFactory.Create(alice);
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.White == 0
                       && m.ManaGenerated.Blue == 0 && m.ManaGenerated.Black == 0
                       && m.ManaGenerated.Red == 0 && m.ManaGenerated.Green == 0);

        colorless.Activate();

        land.IsTapped.Should().BeTrue();
        alice.EnergyCounters.Should().Be(0, "no energy spent on the colorless tap");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}, Pay {E}: Add one mana of any color (WUBRG)
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherHub_HasFiveColoredManaAbilities_OnePerWUBRG()
    {
        var land = AetherHubFactory.Create(_alice);
        var coloreds = land.Abilities.OfType<ManaAbility>()
            .Where(m => m.ManaGenerated.White + m.ManaGenerated.Blue
                      + m.ManaGenerated.Black + m.ManaGenerated.Red
                      + m.ManaGenerated.Green == 1).ToList();

        coloreds.Should().HaveCount(5, "WUBRG — one ManaAbility per colour");
        coloreds.Should().ContainSingle(m => m.ManaGenerated.White == 1);
        coloreds.Should().ContainSingle(m => m.ManaGenerated.Blue == 1);
        coloreds.Should().ContainSingle(m => m.ManaGenerated.Black == 1);
        coloreds.Should().ContainSingle(m => m.ManaGenerated.Red == 1);
        coloreds.Should().ContainSingle(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void AetherHub_ColoredManaAbility_CannotActivateAtZeroEnergy()
    {
        // CR 119.4 — players can't pay a resource they don't have.
        var alice = new Player("Alice", 20);
        var land = AetherHubFactory.Create(alice);
        var blue = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        alice.EnergyCounters.Should().Be(0);

        blue.CanActivate().Should().BeFalse(
            "{T}, Pay {E}: Add one mana of any color requires ≥1 energy");
    }

    [Fact]
    public void AetherHub_ColoredManaAbility_CanActivateWithOneEnergy()
    {
        var alice = new Player("Alice", 20);
        alice.GainEnergy(1);
        var land = AetherHubFactory.Create(alice);
        var red = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue(
            "1 energy is enough to pay the printed {E} cost");
    }

    [Fact]
    public void AetherHub_ColoredManaActivation_SpendsOneEnergyAndTapsLand()
    {
        var alice = new Player("Alice", 20);
        alice.GainEnergy(2);
        var land = AetherHubFactory.Create(alice);
        var green = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1);

        green.Activate();

        alice.EnergyCounters.Should().Be(1, "one energy spent on the {E} cost");
        land.IsTapped.Should().BeTrue("{T} cost tapped the land");
    }

    [Fact]
    public void AetherHub_ColoredManaAbility_CannotActivateWhenTapped()
    {
        var alice = new Player("Alice", 20);
        alice.GainEnergy(5);
        var land = AetherHubFactory.Create(alice);
        var blue = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        blue.Activate();
        land.IsTapped.Should().BeTrue();

        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "a tapped permanent can't pay {T} — printed {T} cost is missing");
    }

    // -----------------------------------------------------------------------
    // Dispatcher integration
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherHub_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Aether Hub", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Aether Hub");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "dispatcher path attaches the full mana-ability suite");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "dispatcher path attaches the ETB trigger");
    }
}

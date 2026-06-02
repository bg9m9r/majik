using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FieryHellhoundFactory"/>.
///
/// Covers:
/// - Card identity ({1}{R}{R} 2/2 Creature — Elemental Dog, red, mana value 3).
/// - No Defender keyword (Fiery Hellhound is NOT a Defender creature).
/// - Activated ability shape: exactly one <see cref="ActivatedAbility"/>
///   with a single <see cref="ManaCostCost"/> of {R} and no targets
///   (self-pump, no TargetRequests).
/// - Activation resolution: registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, 0) on Fiery Hellhound's
///   <see cref="Creature.ActiveEffects"/> — Power increases by 1, Toughness
///   unchanged; effect expires at end of turn via ExpireEndOfTurn.
/// - Shape-only no-op: ActiveEffects null — activation does NOT throw.
/// - Repeatable: activating twice registers two +1/+0 EOT effects.
/// - NamedCardFactory dispatcher resolves "Fiery Hellhound" to expected shape.
/// </summary>
[Trait("Color", "R")]
public class FieryHellhoundFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryHellhound_IsCreature()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void FieryHellhound_NameIsCorrect()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.Name.Should().Be("Fiery Hellhound");
    }

    [Fact]
    public void FieryHellhound_HasCorrectPrintedManaCost()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.ManaCost.Should().Be("{1}{R}{R}");
    }

    [Fact]
    public void FieryHellhound_HasCorrectPrintedPowerAndToughness()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
    }

    [Fact]
    public void FieryHellhound_HasElementalSubtype()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.Subtypes.Should().Contain(CardSubtype.Elemental,
            "Fiery Hellhound is a Creature — Elemental Dog (CR 205.3m)");
    }

    [Fact]
    public void FieryHellhound_HasDogSubtype()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.Subtypes.Should().Contain(CardSubtype.Dog,
            "Fiery Hellhound is a Creature — Elemental Dog (CR 205.3m)");
    }

    [Fact]
    public void FieryHellhound_HasCorrectManaCostValue()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        // {1}{R}{R} = 1 generic + 2 red = mana value 3 (CR 202.3).
        card.ManaCostValue.TotalValue.Should().Be(3,
            "mana value of {1}{R}{R} is 3");
    }

    [Fact]
    public void FieryHellhound_OwnerAndControllerAreSet()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FieryHellhound_IsNotLegendary()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // No Defender (unlike Wall of Fire)
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryHellhound_DoesNotHaveDefenderKeyword()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Defender",
                "Fiery Hellhound is not a Defender creature");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryHellhound_HasExactlyOneActivatedAbility()
    {
        var card = FieryHellhoundFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {R}: +1/+0 firebreathing ability is the only ActivatedAbility");
    }

    [Fact]
    public void FieryHellhound_ActivatedAbility_HasManaCostCostOfOneRed()
    {
        var card = FieryHellhoundFactory.Create(_alice);
        var pump = card.Abilities.OfType<ActivatedAbility>().Single();

        pump.Costs.Should().HaveCount(1,
            "the only printed activation cost is {R}");
        var cost = pump.Costs.OfType<ManaCostCost>().Single();
        cost.Cost.Red.Should().Be(1, "activation cost is exactly one red mana");
        cost.Cost.Generic.Should().Be(0, "no generic component in {R}");
    }

    [Fact]
    public void FieryHellhound_ActivatedAbility_HasNoTargetRequests()
    {
        var card = FieryHellhoundFactory.Create(_alice);
        var pump = card.Abilities.OfType<ActivatedAbility>().Single();

        // Fiery Hellhound pumps itself — no targets declared.
        pump.TargetRequests.Should().BeNullOrEmpty(
            "the firebreathing pump has no targets; it affects Fiery Hellhound itself");
    }

    // -----------------------------------------------------------------------
    // Activation resolution — {R}: +1/+0 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryHellhound_ActivatingPump_IncreasePowerByOne()
    {
        var svc = new ContinuousEffectsService();
        var card = FieryHellhoundFactory.Create(_alice);
        card.ActiveEffects = svc;

        // Baseline: printed 2/2.
        card.GetPower().Should().Be(2);
        card.GetToughness().Should().Be(2);

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(3,
            "{R} firebreathing: +1/+0 until EOT — power increases by 1 (Layer 7c)");
        card.GetToughness().Should().Be(2,
            "+1/+0 does NOT modify toughness");
    }

    [Fact]
    public void FieryHellhound_PumpEffect_ExpiresAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var card = FieryHellhoundFactory.Create(_alice);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(3, "pump is active");

        // CR 514.2 — cleanup step removes EOT effects.
        svc.ExpireEndOfTurn();

        card.GetPower().Should().Be(2,
            "PumpUntilEndOfTurnEffect expires at end of turn — power returns to 2");
        card.GetToughness().Should().Be(2,
            "toughness is unchanged throughout");
    }

    [Fact]
    public void FieryHellhound_PumpEffect_IsRepeatable()
    {
        var svc = new ContinuousEffectsService();
        var card = FieryHellhoundFactory.Create(_alice);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        // Activate twice (spend {R} twice — no once-per-turn restriction printed).
        foreach (var effect in pump.Effects) effect.Execute();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(4,
            "each {R} activation stacks +1/+0: two activations = +2/+0");
        card.GetToughness().Should().Be(2);
    }

    [Fact]
    public void FieryHellhound_PumpEffect_NullActiveEffects_DoesNotThrow()
    {
        // Shape-only test path: ActiveEffects not wired.
        var card = FieryHellhoundFactory.Create(_alice);

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var effect in pump.Effects) effect.Execute(); };
        act.Should().NotThrow(
            "effect body guards on null ActiveEffects — shape-only callers safe");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatcher integration
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryHellhound_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Fiery Hellhound", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Fiery Hellhound");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().Contain(CardSubtype.Elemental);
        card.Subtypes.Should().Contain(CardSubtype.Dog);
        card.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Defender",
                "dispatcher path does not attach Defender keyword");
        card.Abilities.OfType<ActivatedAbility>()
            .Should().HaveCount(1,
                "dispatcher path attaches the {R} firebreathing activated ability");
    }
}

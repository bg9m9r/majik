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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WallOfFireFactory"/>.
///
/// Covers:
/// - Card identity ({1}{R}{R} 0/5 Creature — Wall, red, mana value 3).
/// - Defender keyword marker (CR 702.3) — surfaced via
///   <see cref="CombatAbilities.HasDefender"/>.
/// - Activated ability shape: exactly one <see cref="ActivatedAbility"/>
///   with a single <see cref="ManaCostCost"/> of {R} and no targets
///   (self-pump, no TargetRequests).
/// - Activation resolution: registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, 0) on Wall of Fire's
///   <see cref="Creature.ActiveEffects"/> — Power increases by 1, Toughness
///   unchanged; effect expires at end of turn via ExpireEndOfTurn.
/// - Shape-only no-op: ActiveEffects null — activation does NOT throw.
/// - Repeatable: activating twice registers two +1/+0 EOT effects.
/// - NamedCardFactory dispatcher resolves "Wall of Fire" to the expected
///   Wall shape with Defender + one ActivatedAbility.
/// </summary>
[Trait("Color", "R")]
public class WallOfFireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfFire_IsCreature()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void WallOfFire_NameIsCorrect()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.Name.Should().Be("Wall of Fire");
    }

    [Fact]
    public void WallOfFire_HasCorrectPrintedManaCost()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.ManaCost.Should().Be("{1}{R}{R}");
    }

    [Fact]
    public void WallOfFire_HasCorrectPrintedPowerAndToughness()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.BasePower.Should().Be(0);
        card.BaseToughness.Should().Be(5);
    }

    [Fact]
    public void WallOfFire_HasWallSubtype()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.Subtypes.Should().Contain(CardSubtype.Wall,
            "Wall of Fire is a Creature — Wall (CR 205.3m)");
    }

    [Fact]
    public void WallOfFire_HasCorrectManaCostValue()
    {
        var card = WallOfFireFactory.Create(_alice);

        // {1}{R}{R} = 1 generic + 2 red = mana value 3 (CR 202.3).
        card.ManaCostValue.TotalValue.Should().Be(3,
            "mana value of {1}{R}{R} is 3");
    }

    [Fact]
    public void WallOfFire_OwnerAndControllerAreSet()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WallOfFire_IsNotLegendary()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Defender keyword (CR 702.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfFire_HasDefenderKeyword()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "CR 702.3 — Defender is wired as a KeywordAbility marker");
        CombatAbilities.HasDefender(card).Should().BeTrue(
            "CombatAbilities.HasDefender must surface the keyword");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfFire_HasExactlyOneActivatedAbility()
    {
        var card = WallOfFireFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {R}: +1/+0 firebreathing ability is the only ActivatedAbility");
    }

    [Fact]
    public void WallOfFire_ActivatedAbility_HasManaCostCostOfOneRed()
    {
        var card = WallOfFireFactory.Create(_alice);
        var pump = card.Abilities.OfType<ActivatedAbility>().Single();

        pump.Costs.Should().HaveCount(1,
            "the only printed activation cost is {R}");
        var cost = pump.Costs.OfType<ManaCostCost>().Single();
        cost.Cost.Red.Should().Be(1, "activation cost is exactly one red mana");
        cost.Cost.Generic.Should().Be(0, "no generic component in {R}");
    }

    [Fact]
    public void WallOfFire_ActivatedAbility_HasNoTargetRequests()
    {
        var card = WallOfFireFactory.Create(_alice);
        var pump = card.Abilities.OfType<ActivatedAbility>().Single();

        // Wall of Fire pumps itself — no targets declared.
        pump.TargetRequests.Should().BeNullOrEmpty(
            "the firebreathing pump has no targets; it affects Wall of Fire itself");
    }

    // -----------------------------------------------------------------------
    // Activation resolution — {R}: +1/+0 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfFire_ActivatingPump_IncreasePowerByOne()
    {
        var svc = new ContinuousEffectsService();
        var card = WallOfFireFactory.Create(_alice);
        card.ActiveEffects = svc;

        // Baseline: printed 0/5.
        card.GetPower().Should().Be(0);
        card.GetToughness().Should().Be(5);

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(1,
            "{R} firebreathing: +1/+0 until EOT — power increases by 1 (Layer 7c)");
        card.GetToughness().Should().Be(5,
            "+1/+0 does NOT modify toughness");
    }

    [Fact]
    public void WallOfFire_PumpEffect_ExpiresAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var card = WallOfFireFactory.Create(_alice);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(1, "pump is active");

        // CR 514.2 — cleanup step removes EOT effects.
        svc.ExpireEndOfTurn();

        card.GetPower().Should().Be(0,
            "PumpUntilEndOfTurnEffect expires at end of turn — power returns to 0");
        card.GetToughness().Should().Be(5,
            "toughness is unchanged throughout");
    }

    [Fact]
    public void WallOfFire_PumpEffect_IsRepeatable()
    {
        var svc = new ContinuousEffectsService();
        var card = WallOfFireFactory.Create(_alice);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        // Activate twice (spend {R} twice — no once-per-turn restriction printed).
        foreach (var effect in pump.Effects) effect.Execute();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(2,
            "each {R} activation stacks +1/+0: two activations = +2/+0");
        card.GetToughness().Should().Be(5);
    }

    [Fact]
    public void WallOfFire_PumpEffect_NullActiveEffects_DoesNotThrow()
    {
        // Shape-only test path: ActiveEffects not wired.
        var card = WallOfFireFactory.Create(_alice);

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var effect in pump.Effects) effect.Execute(); };
        act.Should().NotThrow(
            "effect body guards on null ActiveEffects — shape-only callers safe");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatcher integration
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfFire_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Wall of Fire", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Wall of Fire");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().Contain(CardSubtype.Wall);
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "dispatcher path attaches Defender keyword");
        card.Abilities.OfType<ActivatedAbility>()
            .Should().HaveCount(1,
                "dispatcher path attaches the {R} firebreathing activated ability");
    }
}

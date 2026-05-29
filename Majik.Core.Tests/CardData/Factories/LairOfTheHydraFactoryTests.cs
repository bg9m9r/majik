using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LairOfTheHydraFactory"/> (Modern Horizons 2 green
/// creature land). Land:
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {G}.
///    {X}{G}: Until end of turn, this land becomes an X/X green Hydra
///    creature. It's still a land. X can't be 0."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {G} mana ability.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({X}{G}, instant speed) + Layer 4 / Layer 7b
///   continuous effects:
///     * Adds Creature type + Hydra subtype on Layer 4.
///     * Records X/X base P/T on Layer 7b at the sampled X.
///     * "X can't be 0" clamps the body to a minimum 1/1.
///     * Both expire at end of turn.
/// - Conditional ETB-tapped ("two or more other lands") replacement.
/// </summary>
public class LairOfTheHydraFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LairOfTheHydra_Identity()
    {
        var land = LairOfTheHydraFactory.Create(_alice);

        land.Name.Should().Be("Lair of the Hydra");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Lair of the Hydra is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LairOfTheHydra_HasManaAndAnimateAbilities()
    {
        var land = LairOfTheHydraFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {G} mana ability is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{X}{G} animate ability is wired");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LairOfTheHydra()
    {
        var card = NamedCardFactory.Create("Lair of the Hydra", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Lair of the Hydra");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void LairOfTheHydra_AnimateAbility_HasPrintedManaCostXG()
    {
        var land = LairOfTheHydraFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({X}{G})").Subject;
        manaCost.Cost.HasX.Should().BeTrue("the cost contains the variable {X}");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void LairOfTheHydra_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = LairOfTheHydraFactory.Create(
            _alice, effects, replacements: null, xValueProvider: () => 3);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Hydra,
            "Hydra subtype added");
    }

    [Fact]
    public void LairOfTheHydra_Animate_RecordsXX_AtSampledX()
    {
        var effects = new ContinuousEffectsService();
        var land = LairOfTheHydraFactory.Create(
            _alice, effects, replacements: null, xValueProvider: () => 4);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var pt = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .Single();
        pt.NewPower.Should().Be(4, "X = 4 → 4/4 body");
        pt.NewToughness.Should().Be(4, "X = 4 → 4/4 body");
        pt.Layer.Should().Be(Layer.PT_SetBase);
        pt.ExpiresAtEndOfTurn.Should().BeTrue(
            "animation lifts at cleanup (CR 514.2)");
    }

    [Fact]
    public void LairOfTheHydra_Animate_ClampsXToMinimumOne_XCantBeZero()
    {
        // CR 107.1b — "X can't be 0". With no X signal (provider returns 0),
        // the body is the minimum legal 1/1, never 0/0.
        var effects = new ContinuousEffectsService();
        var land = LairOfTheHydraFactory.Create(
            _alice, effects, replacements: null, xValueProvider: () => 0);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var pt = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .Single();
        pt.NewPower.Should().Be(LairOfTheHydraFactory.MinX,
            "\"X can't be 0\" clamps the animated body to a minimum 1/1");
        pt.NewToughness.Should().Be(LairOfTheHydraFactory.MinX);
    }

    // -----------------------------------------------------------------------
    // Conditional ETB-tapped — "two or more other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void LairOfTheHydra_RegistersConditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = LairOfTheHydraFactory.Create(
            _alice, effects: null, replacements: bus, xValueProvider: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        // Zero other lands → enters untapped.
        var afterEmpty = bus.Apply(intent);
        afterEmpty.Should().NotBeNull();
        afterEmpty!.EntersTapped.Should().BeFalse(
            "with 0 other lands, the Lair enters untapped");

        // Two other lands present (excluding the Lair) → enters tapped.
        var land1 = NamedCardFactory.Create("Plains", _alice);
        var land2 = NamedCardFactory.Create("Island", _alice);
        _alice.Zones.Battlefield.AddCard(land1);
        land1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land2);
        land2.SetZone(ZoneType.Battlefield);

        var afterTwoOthers = bus.Apply(intent);
        afterTwoOthers.Should().NotBeNull();
        afterTwoOthers!.EntersTapped.Should().BeTrue(
            "with 2 other lands, the manland's slow clause flips it tapped");
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(
        ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}

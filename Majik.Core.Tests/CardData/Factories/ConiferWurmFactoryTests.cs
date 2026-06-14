using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="ConiferWurmFactory"/> (Modern Horizons 3, {4}{G}).
///
/// Snow Creature — Wurm 4/4:
///   "Trample
///    {3}{G}: This creature gets +X/+X until end of turn, where X is the
///    number of snow permanents you control."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (Snow supertype, Wurm subtype, 4/4, {4}{G}).
/// - Trample keyword marker (CR 702.19).
/// - {3}{G} self-pump ability cost shape (no {X} in the cost; X is derived
///   from board state, not paid).
/// - +X/+X applied where X = snow permanents you control, COUNTING the Wurm
///   itself (oracle: "snow permanents you control", not "other").
/// - Pump expires at end of turn.
/// - X = 0 (no snow permanents while off the battlefield) is a clean no-op.
/// </summary>
[Trait("Color", "G")]
public class ConiferWurmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ConiferWurm_Identity()
    {
        var c = ConiferWurmFactory.Create(_alice);

        c.Name.Should().Be("Conifer Wurm");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        c.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Conifer Wurm is a Snow creature (CR 205.4d)");
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue("Conifer Wurm is a Wurm");
        c.ManaCost.Should().Be("{4}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ConiferWurm_HasTrampleKeyword()
    {
        var c = ConiferWurmFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "CR 702.19 — Conifer Wurm has Trample");
    }

    // -----------------------------------------------------------------------
    // Pump ability cost shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ConiferWurm_PumpAbility_HasPrintedManaCost3G_NoTap_InstantSpeed()
    {
        var c = ConiferWurmFactory.Create(_alice);

        var pump = c.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the {3}{G} self-pump is the only activated ability").Subject;

        var manaCost = pump.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the activation cost is one ManaCostCost ({3}{G})").Subject;
        manaCost.Cost.HasX.Should().BeFalse(
            "X is derived from board state, not paid — {3}{G} has no {X}");
        pump.Costs.OfType<AdditionalCost>().Should().BeEmpty(
            "the pump has no tap symbol in its cost");
        pump.IsSorcerySpeed.Should().BeFalse(
            "the pump is instant-speed per oracle");
    }

    // -----------------------------------------------------------------------
    // +X/+X where X = snow permanents you control (counts itself)
    // -----------------------------------------------------------------------

    [Fact]
    public void ConiferWurm_Pump_CountsItselfAndOtherSnowPermanents()
    {
        var effects = new ContinuousEffectsService();
        var wurm = ConiferWurmFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(wurm);
        wurm.SetZone(ZoneType.Battlefield);

        // Two OTHER snow permanents controlled by Alice. Together with the
        // Wurm itself that is 3 snow permanents → X = 3.
        AddSnowLand(_alice);
        AddSnowLand(_alice);

        var pump = wurm.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in pump.Effects) e.Execute();

        var (power, toughness) = effects.ComputePowerToughness(wurm);
        power.Should().Be(7, "4 base + X=3 snow permanents (2 lands + the Wurm itself)");
        toughness.Should().Be(7, "4 base + X=3 snow permanents (2 lands + the Wurm itself)");
    }

    [Fact]
    public void ConiferWurm_Pump_OnlyItselfOnBattlefield_GivesPlusOne()
    {
        // The Wurm alone is a snow permanent → X = 1 (it counts itself;
        // oracle reads "snow permanents you control", NOT "other").
        var effects = new ContinuousEffectsService();
        var wurm = ConiferWurmFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(wurm);
        wurm.SetZone(ZoneType.Battlefield);

        var pump = wurm.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in pump.Effects) e.Execute();

        var (power, toughness) = effects.ComputePowerToughness(wurm);
        power.Should().Be(5, "4 base + X=1 (the Wurm counts itself)");
        toughness.Should().Be(5, "4 base + X=1 (the Wurm counts itself)");
    }

    [Fact]
    public void ConiferWurm_Pump_RegistersEndOfTurnExpiringEffect()
    {
        var effects = new ContinuousEffectsService();
        var wurm = ConiferWurmFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(wurm);
        wurm.SetZone(ZoneType.Battlefield);

        var pump = wurm.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in pump.Effects) e.Execute();

        var registered = GetRegisteredEffects(effects)
            .OfType<PumpUntilEndOfTurnEffect>()
            .Should().ContainSingle("the activation registers one +X/+X pump").Subject;
        registered.ExpiresAtEndOfTurn.Should().BeTrue(
            "the +X/+X lifts at cleanup (CR 514.2)");
    }

    [Fact]
    public void ConiferWurm_Pump_NoSnowPermanents_IsCleanNoOp()
    {
        // Wurm NOT on the battlefield and no other snow permanents → X = 0.
        // The pump must not register any +0/+0 effect.
        var effects = new ContinuousEffectsService();
        var wurm = ConiferWurmFactory.Create(_alice, effects);
        // Deliberately leave the Wurm off the battlefield (default zone).

        var pump = wurm.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in pump.Effects) e.Execute();

        GetRegisteredEffects(effects)
            .OfType<PumpUntilEndOfTurnEffect>()
            .Should().BeEmpty("X = 0 → +0/+0 is a clean no-op, no effect registered");
    }

    // -----------------------------------------------------------------------
    // Shape-only safety
    // -----------------------------------------------------------------------

    [Fact]
    public void ConiferWurm_ShapeOnly_PumpExecuteDoesNotThrow()
    {
        var wurm = ConiferWurmFactory.Create(_alice); // no ContinuousEffectsService

        var pump = wurm.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var e in pump.Effects) e.Execute(); };

        act.Should().NotThrow(
            "without a wired ContinuousEffectsService the pump body silently no-ops");
    }

    [Fact]
    public void ConiferWurm_ThrowsOnNullOwner()
    {
        var act = () => ConiferWurmFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Add a minimal snow permanent (Snow-Covered Forest) to the controller's
    /// battlefield, mirroring the Ice-Fang Coatl test helper.
    /// </summary>
    private static void AddSnowLand(Player controller)
    {
        var land = SnowCoveredForestFactory.Create(controller);
        controller.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
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

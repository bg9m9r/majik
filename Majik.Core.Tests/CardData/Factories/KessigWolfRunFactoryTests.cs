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
/// Tests for <see cref="KessigWolfRunFactory"/> (Innistrad utility land).
/// Land:
///   "{T}: Add {C}.
///    {X}{R}{G}, {T}: Target creature gets +X/+0 and gains trample until end
///    of turn."
///
/// Covers:
/// - Identity (Land, nonbasic, name, owner/controller).
/// - JSON-backed {T}: Add {C} mana ability.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Pump ability cost shape ({X}{R}{G} + {T}) + single 1..1 target request.
/// - Resolution registers a +X/+0 <see cref="PumpUntilEndOfTurnEffect"/> and a
///   Trample <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the chosen
///   creature, X sampled from the wired provider, both expiring at end of turn.
/// - Shape-only / illegal-target paths are no-ops.
/// </summary>
[Trait("Color", "C")]
public class KessigWolfRunFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KessigWolfRun_Identity()
    {
        var land = KessigWolfRunFactory.Create(_alice);

        land.Name.Should().Be("Kessig Wolf Run");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "Kessig Wolf Run is a plain utility land, not a creature land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Kessig Wolf Run is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KessigWolfRun_DispatchesThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create(KessigWolfRunFactory.CardName, _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Kessig Wolf Run");
    }

    [Fact]
    public void KessigWolfRun_HasManaAndPumpAbilities()
    {
        var land = KessigWolfRunFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} mana ability is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {X}{R}{G}, {T} pump ability is wired");
    }

    // -----------------------------------------------------------------------
    // Pump ability — cost + target shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_HasCorrectCostShape_XRGManaAndTapAndOneTarget()
    {
        var land = KessigWolfRunFactory.Create(_alice);

        var pump = KessigWolfRunFactory.GetPumpAbility(land);

        var mana = pump.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the pump's mana cost is one ManaCostCost ({X}{R}{G})").Subject;
        mana.Cost.HasX.Should().BeTrue("the cost contains the variable {X}");
        pump.Costs.OfType<AdditionalCost>().Should().HaveCount(1, "{T} is part of the cost");

        pump.TargetRequests.Should().ContainSingle("a single target creature");
        pump.TargetRequests[0].MinTargets.Should().Be(1);
        pump.TargetRequests[0].MaxTargets.Should().Be(1);

        pump.IsSorcerySpeed.Should().BeFalse("the pump is instant-speed per oracle");
    }

    // -----------------------------------------------------------------------
    // Pump ability — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_OnResolution_RegistersPlusXPlusZeroAndTrample()
    {
        var effects = new ContinuousEffectsService();
        var land = KessigWolfRunFactory.Create(
            _alice, effects: effects, xValueProvider: () => 3);
        land.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "", 2, 2, null, null)
        {
            ActiveEffects = effects,
        };
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);

        var pump = KessigWolfRunFactory.GetPumpAbility(land);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        pump.Resolve();

        // +X/+0 (power-only) and a Trample grant, both registered on the target.
        GetRegisteredEffects(effects).OfType<PumpUntilEndOfTurnEffect>()
            .Should().ContainSingle("a +X/+0 EOT pump is registered on the target");
        GetRegisteredEffects(effects).OfType<GrantKeywordUntilEndOfTurnEffect>()
            .Should().ContainSingle("a Trample keyword grant is registered on the target");

        var chars = effects.Compute(target);
        chars.Power.Should().Be(5, "2 base + X=3 → 5 power (+X/+0)");
        chars.Toughness.Should().Be(2, "toughness is unchanged (+X/+0 is power-only)");
        chars.Keywords.Should().Contain("Trample");

        // CR 514.2 — both effects expire at cleanup, reverting the target.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(target);
        after.Power.Should().Be(2);
        after.Toughness.Should().Be(2);
        after.Keywords.Should().NotContain("Trample");
    }

    [Fact]
    public void PumpAbility_XZero_StillGrantsTrample_NoPump()
    {
        // X = 0 is a legal activation (no "X can't be 0" rider on this card).
        // +0/+0 is recorded only as the trample grant; no power change.
        var effects = new ContinuousEffectsService();
        var land = KessigWolfRunFactory.Create(
            _alice, effects: effects, xValueProvider: () => 0);
        land.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "", 2, 2, null, null)
        {
            ActiveEffects = effects,
        };
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);

        var pump = KessigWolfRunFactory.GetPumpAbility(land);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        pump.Resolve();

        var chars = effects.Compute(target);
        chars.Power.Should().Be(2, "X=0 → +0/+0, power unchanged");
        chars.Keywords.Should().Contain("Trample", "trample is granted regardless of X");
    }

    [Fact]
    public void PumpAbility_NoTarget_NoOp_DoesNotThrow()
    {
        var effects = new ContinuousEffectsService();
        var land = KessigWolfRunFactory.Create(_alice, effects: effects, xValueProvider: () => 2);
        land.SetZone(ZoneType.Battlefield);

        var pump = KessigWolfRunFactory.GetPumpAbility(land);
        // No chosen target — resolving must not throw and registers nothing.
        var resolve = () => pump.Resolve();
        resolve.Should().NotThrow();

        GetRegisteredEffects(effects).OfType<PumpUntilEndOfTurnEffect>()
            .Should().BeEmpty();
    }

    [Fact]
    public void PumpAbility_NoEffectsService_NoOp_DoesNotThrow()
    {
        // Single-arg dispatcher path — no ContinuousEffectsService wired and
        // the target carries no ActiveEffects: resolving must not throw.
        var land = KessigWolfRunFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "", 2, 2, null, null);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);

        var pump = KessigWolfRunFactory.GetPumpAbility(land);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        var resolve = () => pump.Resolve();
        resolve.Should().NotThrow();
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

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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="PendelhavenFactory"/> — Legendary Land (Legends) with
/// two abilities:
///   {T}: Add {G}.
///   {T}: Target 1/1 creature gets +1/+2 until end of turn.
///
/// The plain card surface (name, Legendary supertype, Land type, {T}: Add {G})
/// is materialised from the embedded JSON definition; the targeted pump is
/// layered on in the factory (Blinkmoth Nexus target-creature pattern + the
/// shared <see cref="PumpUntilEndOfTurnEffect"/> primitive).
/// </summary>
public class PendelhavenTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Pendelhaven_IsLegendaryLand_WithCorrectIdentity()
    {
        var land = PendelhavenFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Pendelhaven is a Legendary Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Name.Should().Be("Pendelhaven");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Pendelhaven()
    {
        var card = NamedCardFactory.Create("Pendelhaven", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Pendelhaven");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        // Green mana ability + one targeted-pump ActivatedAbility.
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void Pendelhaven_TapForGreen_TapsLandAndProducesOneGreen()
    {
        var land = PendelhavenFactory.Create(_alice);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        produced.Green.Should().Be(1);
        produced.Generic.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Target 1/1 creature gets +1/+2 until end of turn.
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_HasCorrectCostShape_TapOnlyAndOneTarget()
    {
        var land = PendelhavenFactory.Create(_alice);

        var pump = PendelhavenFactory.GetPumpAbility(land);

        // Tap is the only cost — no mana component (unlike Blinkmoth's pump).
        pump.Costs.OfType<ManaCostCost>().Should().BeEmpty("the cost is just {T}");
        pump.Costs.OfType<AdditionalCost>().Should().HaveCount(1, "{T} is the cost");
        pump.TargetRequests.Should().ContainSingle();
        pump.TargetRequests[0].MinTargets.Should().Be(1);
        pump.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void PumpAbility_OnResolution_RegistersPlusOnePlusTwoUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = PendelhavenFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        // A target 1/1 creature on the battlefield, primed with the
        // continuous-effects service so the pump can register against it.
        var target = new Creature("Llanowar Elves", "", 1, 1)
        {
            ActiveEffects = effects,
        };
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);

        var pump = PendelhavenFactory.GetPumpAbility(land);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        pump.Resolve();

        // A +1/+2 EOT pump is registered against the target's effects.
        var registered = GetRegisteredEffects(effects)
            .OfType<PumpUntilEndOfTurnEffect>()
            .SingleOrDefault();
        registered.Should().NotBeNull("the pump registers a +1/+2 EOT effect on the target");

        var chars = effects.Compute(target);
        chars.Power.Should().Be(2, "1/1 base + 1/2 from the pump → 2/3");
        chars.Toughness.Should().Be(3);

        // CR 514.2 — expires during cleanup, reverting the target to 1/1.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(target);
        after.Power.Should().Be(1);
        after.Toughness.Should().Be(1);
    }

    [Fact]
    public void PumpAbility_NoEffectsService_NoOp_DoesNotThrow()
    {
        // Single-arg dispatcher path — no ContinuousEffectsService on a target.
        var land = PendelhavenFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var pump = PendelhavenFactory.GetPumpAbility(land);
        // No target primed + no effects service — resolving must not throw.
        var resolve = () => pump.Resolve();
        resolve.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

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

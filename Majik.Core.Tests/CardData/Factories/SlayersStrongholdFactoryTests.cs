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
/// Unit tests for <see cref="SlayersStrongholdFactory"/> (Avacyn Restored, Land).
///
/// Oracle text:
///   "{T}: Add {C}.
///    {R}{W}, {T}: Target creature gets +2/+0 and gains vigilance and haste
///    until end of turn."
///
/// Covers:
/// - Card identity (Land, non-legendary, owner/controller).
/// - {T}: Add {C} — vanilla colourless mana ability from the embedded JSON.
/// - Pump ability cost shape: {R}{W} + {T} + a single 1..1 target creature.
/// - Resolution: the chosen creature gets +2/+0, vigilance, and haste until
///   end of turn (CR 613.4d / 613.1c); all expire at cleanup (CR 514.2).
/// - CR 608.2b guards: no target / no effects service → no-op, no throw.
/// - NamedCardFactory dispatcher resolves "Slayers' Stronghold".
/// </summary>
[Trait("Color", "C")]
public class SlayersStrongholdFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SlayersStronghold_IsLand()
    {
        var land = SlayersStrongholdFactory.Create(_alice);
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Land");
    }

    [Fact]
    public void SlayersStronghold_NameIsCorrect()
    {
        var land = SlayersStrongholdFactory.Create(_alice);
        land.Name.Should().Be("Slayers' Stronghold");
    }

    [Fact]
    public void SlayersStronghold_OwnerAndControllerAreSet()
    {
        var land = SlayersStrongholdFactory.Create(_alice);
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SlayersStronghold_IsNotLegendary()
    {
        var land = SlayersStrongholdFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SlayersStronghold_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Slayers' Stronghold", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Slayers' Stronghold");
        card.HasType(CardType.Land).Should().BeTrue();
        // {T}: Add {C} is the only mana ability.
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void SlayersStronghold_HasColorlessTapAbility()
    {
        var land = SlayersStrongholdFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();
        // {C} folds to one colourless mana (one generic in ManaCost today),
        // matching Blinkmoth Nexus / Reliquary Tower.
        mana.ManaGenerated.TotalValue.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // {R}{W}, {T}: Target creature gets +2/+0 and gains vigilance and haste.
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_HasCorrectCostShape_ManaAndTapAndOneTarget()
    {
        var land = SlayersStrongholdFactory.Create(_alice);

        var pump = SlayersStrongholdFactory.GetPumpAbility(land);

        var mana = pump.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Red.Should().Be(1, "the {R} pip");
        mana.Cost.White.Should().Be(1, "the {W} pip");
        pump.Costs.OfType<AdditionalCost>().Should().HaveCount(1, "{T} is part of the cost");
        pump.TargetRequests.Should().ContainSingle();
        pump.TargetRequests[0].MinTargets.Should().Be(1);
        pump.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void PumpAbility_OnResolution_GivesPlusTwoPlusZeroVigilanceAndHaste()
    {
        var effects = new ContinuousEffectsService();
        var land = SlayersStrongholdFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var target = new Creature("Bear", "", 2, 2)
        {
            ActiveEffects = effects,
        };
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);

        CombatAbilities.HasVigilance(target).Should().BeFalse("no buff yet");
        CombatAbilities.HasHaste(target).Should().BeFalse("no buff yet");

        var pump = SlayersStrongholdFactory.GetPumpAbility(land);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        pump.Resolve();

        var chars = effects.Compute(target);
        chars.Power.Should().Be(4, "2/2 base + 2/0 from the pump");
        chars.Toughness.Should().Be(2, "+2/+0 leaves toughness unchanged");

        CombatAbilities.HasVigilance(target).Should().BeTrue("the pump grants vigilance");
        CombatAbilities.HasHaste(target).Should().BeTrue("the pump grants haste");

        // CR 514.2 — everything expires during cleanup, reverting the target.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(target);
        after.Power.Should().Be(2);
        after.Toughness.Should().Be(2);
        CombatAbilities.HasVigilance(target).Should().BeFalse("vigilance expired at cleanup");
        CombatAbilities.HasHaste(target).Should().BeFalse("haste expired at cleanup");
    }

    [Fact]
    public void PumpAbility_NoTargetOrEffectsService_NoOp_DoesNotThrow()
    {
        var land = SlayersStrongholdFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var pump = SlayersStrongholdFactory.GetPumpAbility(land);
        // No target primed + no effects service — resolving must not throw.
        var resolve = () => pump.Resolve();
        resolve.Should().NotThrow();
    }
}

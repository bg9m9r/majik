using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BlinkmothWellFactory"/> (Darksteel). Land. Oracle
/// text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}: Tap target noncreature artifact."
///
/// Declarative JSON card (mana ability + a tap-target activated ability), the
/// same posture as <see cref="MasterDecoyFactory"/> but the tap target is a
/// noncreature artifact (CR 109.5) rather than a creature.
///
/// Covers:
///   - Identity (plain Land, no printed subtypes/supertypes, owner/controller).
///   - {T}: Add {C} — vanilla mana ability producing one colorless ({C}
///     bucketed as +1 generic in ManaCost today).
///   - The {2}, {T}: tap activated ability cost shape ({2} mana + self-tap)
///     and its noncreature-artifact target request.
///   - Tap resolution taps the chosen noncreature artifact (CR 701.21a).
///   - Tap resolution is a no-op on an off-battlefield target (CR 608.2b).
/// </summary>
[Trait("Color", "C")]
public class BlinkmothWellFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BlinkmothWell_Identity()
    {
        var well = BlinkmothWellFactory.Create(_alice);

        well.Name.Should().Be("Blinkmoth Well");
        well.HasType(CardType.Land).Should().BeTrue();
        well.HasType(CardType.Creature).Should().BeFalse("printed shape is a plain Land");
        well.HasType(CardType.Artifact).Should().BeFalse("printed shape is a plain Land");
        well.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Blinkmoth Well is a nonbasic land");
        well.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        well.Owner.Should().BeSameAs(_alice);
        well.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BlinkmothWell_TapForColorless_ProducesOneGeneric()
    {
        var well = BlinkmothWellFactory.Create(_alice);

        var mana = well.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeTrue();

        var produced = mana.Activate();

        // {C} is bucketed as +1 generic in ValueObjects.ManaCost today
        // (matching the Blinkmoth/Inkmoth utility lands). No coloured pips.
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        well.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void BlinkmothWell_HasTapActivatedAbility_WithTwoGenericAndSelfTapCost()
    {
        var well = BlinkmothWellFactory.Create(_alice);

        well.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "{2}, {T}: Tap target noncreature artifact");
        var tap = well.Abilities.OfType<ActivatedAbility>().Single();

        // {2} generic mana cost.
        tap.Costs.OfType<ManaCostCost>().Single().Cost.Generic
            .Should().Be(2, "the {2} cost is two generic mana");

        // {T} — self-tap.
        tap.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);

        // A single 1..1 "target noncreature artifact" request.
        var request = tap.TargetRequests.Should().ContainSingle().Subject;
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
        request.Description.Should().Contain("noncreature artifact");
    }

    [Fact]
    public void BlinkmothWell_TapAbility_TapsChosenNoncreatureArtifact()
    {
        var well = BlinkmothWellFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(well);
        well.SetZone(ZoneType.Battlefield);

        var signet = new Artifact("Sol Ring", "{1}");
        signet.SetOwner(_bob);
        signet.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(signet);
        signet.SetZone(ZoneType.Battlefield);

        var tap = well.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new[] { new object[] { signet } });

        signet.IsTapped.Should().BeFalse();
        tap.Resolve();
        signet.IsTapped.Should().BeTrue(
            "Fx.Tap delegates to Permanent.Tap (CR 701.21a)");
    }

    [Fact]
    public void BlinkmothWell_TapAbility_NoOpOnNonBattlefieldTarget()
    {
        var well = BlinkmothWellFactory.Create(_alice);

        var signet = new Artifact("Sol Ring", "{1}");
        signet.SetOwner(_bob);
        signet.SetController(_bob);
        // Deliberately NOT on the battlefield — CR 608.2b recheck rejects it.

        var tap = well.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new[] { new object[] { signet } });

        tap.Resolve();
        signet.IsTapped.Should().BeFalse(
            "CR 608.2b — target no longer on battlefield: effect fails silently");
    }
}

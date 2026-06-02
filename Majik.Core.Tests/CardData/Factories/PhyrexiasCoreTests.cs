using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PhyrexiasCoreFactory"/> — Mirrodin Besieged utility
/// land:
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice an artifact: You gain 1 life."
///
/// Colourless utility-land shape shared with <see cref="BuriedRuinFactory"/> /
/// <see cref="MirrodinsCoreFactory"/>: a {C}-producing land plus an
/// activated ability whose cost includes {1}, {T}, and "sacrifice an
/// artifact" (CR 701.16), gaining the controller 1 life (CR 119.3).
///
/// Covers:
/// - Card identity (Land, nonbasic, no subtypes) + dispatch.
/// - {T}: Add {C} mana ability (from JSON) taps the land and produces {C}.
/// - Activated ability shape: {1} + {T} + sacrifice-an-artifact, no targets.
/// - Activated ability: paying costs sacrifices an artifact + taps the land;
///   resolving gains 1 life.
/// - The sacrifice cost cannot be paid with no artifact in play.
/// - The land is never an eligible self-sacrifice (a land is not an artifact).
/// </summary>
[Trait("Color", "Colorless")]
public class PhyrexiasCoreTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexiasCore_IsNonbasicLand_NoSubtypes()
    {
        var land = PhyrexiasCoreFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Phyrexia's Core");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PhyrexiasCore()
    {
        var card = NamedCardFactory.Create("Phyrexia's Core", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Phyrexia's Core");
        card.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexiasCore_HasColorlessManaAbility_TapsAndProducesC()
    {
        var land = PhyrexiasCoreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} parses into the Generic slot today (no dedicated Colorless
        // property on ManaCost — mirrors Buried Ruin's tap-for-{C} test).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {1}, {T}, Sacrifice an artifact: You gain 1 life
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexiasCore_HasLifeGainActivatedAbility_NoTargets()
    {
        var land = PhyrexiasCoreFactory.Create(_alice);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.TargetRequests.Should().BeEmpty();
        // {1} mana cost + tap + sacrifice an artifact = three costs.
        activated.Costs.Should().HaveCount(3);
    }

    [Fact]
    public void PhyrexiasCore_LifeGain_SacrificesArtifact_TapsLand_Gains1Life()
    {
        var land = PhyrexiasCoreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // An artifact Alice controls — the sacrifice fodder.
        var relic = new Artifact("Mind Stone", "2");
        relic.SetOwner(_alice);
        relic.SetController(_alice);
        relic.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(relic);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        var lifeBefore = _alice.LifeTotal;

        // Pay {1}, then pay all costs ({1} mana, tap, sacrifice).
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(1));
        foreach (var c in activated.Costs) c.Pay(_alice);

        // The artifact was sacrificed to its owner's graveyard (CR 701.16).
        _alice.Zones.Graveyard.GetCards().Should().Contain(relic);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(relic);
        relic.Zone.Should().Be(ZoneType.Graveyard);

        // The {T} cost ran.
        land.IsTapped.Should().BeTrue();

        activated.Resolve();

        // CR 119.3 — controller gains 1 life.
        _alice.LifeTotal.Should().Be(lifeBefore + PhyrexiasCoreFactory.LifeGainAmount);
    }

    [Fact]
    public void PhyrexiasCore_SacrificeCost_CannotPay_WithNoArtifact()
    {
        var land = PhyrexiasCoreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        var sacCost = activated.Costs
            .OfType<Majik.Core.Costs.SacrificeAnArtifactCost>()
            .Single();

        // No artifact on the battlefield — the land itself is NOT an artifact,
        // so it is never an eligible sacrifice.
        sacCost.CanPay(_alice).Should().BeFalse();
    }
}

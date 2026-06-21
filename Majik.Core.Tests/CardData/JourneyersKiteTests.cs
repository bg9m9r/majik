using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="JourneyersKiteFactory"/>.
///
/// Journeyer's Kite — Artifact {2}.
///   "{3}, {T}: Search your library for a basic land card, reveal it, put it
///    into your hand, then shuffle."
///
/// Covers:
/// - Identity (Artifact, {2}) + NamedCardFactory dispatch.
/// - Single activated ability with two costs ({3}, Tap) + no target requests.
/// - Resolve: moves a BASIC land from library to hand (deterministic
///   first-match fallback when no agent is registered).
/// - Resolve: a NONBASIC land is excluded by the "basic land card" filter.
/// </summary>
public class JourneyersKiteTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_IsArtifact_WithPrintedManaCost()
    {
        var built = NamedCardFactory.Create("Journeyer's Kite", _alice);
        built.Should().BeOfType<Artifact>();
        built.Name.Should().Be("Journeyer's Kite");
        built.ManaCost.Should().Be("{2}");
    }

    [Fact]
    public void Kite_HasOneActivatedAbility_NoManaAbilities()
    {
        var kite = JourneyersKiteFactory.Create(_alice);

        kite.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        kite.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TutorAbility_Has_ThreeMana_Tap_NoSacrifice_NoTargets()
    {
        var kite = JourneyersKiteFactory.Create(_alice);
        var tutor = kite.Abilities.OfType<ActivatedAbility>().Single();

        tutor.TargetRequests.Should().BeEmpty(
            "a library search is a hidden choice (CR 115.1a), not a chosen target");

        tutor.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("3"),
                "tutor costs {3}");
        tutor.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "tutor taps the kite");
        tutor.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the kite is NOT sacrificed — it is reusable");
    }

    [Fact]
    public void Activate_Tutor_MovesBasicLandToHand_ExcludesNonbasic()
    {
        // A BASIC land (Land + Basic supertype) — the only legal target.
        var plains = new Land("Plains", supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        _alice.Zones.Library.AddCard(plains);
        plains.SetZone(ZoneType.Library);

        // A NONBASIC land (Land, no Basic supertype) — must NOT be tutored.
        var nonbasic = new Land("Stomping Ground Stub");
        nonbasic.SetOwner(_alice);
        _alice.Zones.Library.AddCard(nonbasic);
        nonbasic.SetZone(ZoneType.Library);

        var kite = JourneyersKiteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kite);
        kite.SetZone(ZoneType.Battlefield);

        var tutor = kite.Abilities.OfType<ActivatedAbility>().Single();
        tutor.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(plains,
            "the basic land was tutored to hand");
        _alice.Zones.Hand.GetCards().Should().NotContain(nonbasic,
            "a nonbasic land is excluded by the 'basic land card' filter");
        _alice.Zones.Library.GetCards().Should().Contain(nonbasic);
        _alice.Zones.Library.GetCards().Should().NotContain(plains);
        plains.Zone.Should().Be(ZoneType.Hand);

        // The kite stays on the battlefield (no sacrifice — reusable).
        kite.Zone.Should().Be(ZoneType.Battlefield);
    }
}

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
/// Unit tests for <see cref="HedronArchiveFactory"/>.
///
/// Hedron Archive — Artifact {4}.
///   "{T}: Add {C}{C}.
///    {2}, {T}, Sacrifice this artifact: Draw two cards."
///
/// Covers:
/// - Card identity (Artifact, mana cost {4}).
/// - NamedCardFactory dispatch.
/// - Mana ability shape ({T}: Add {C}{C} — two colourless).
/// - Cantrip activated ability cost shape ({2}, {T}, Sacrifice).
/// - Cantrip resolve: sacrifices Hedron Archive + draws two cards.
/// - Empty-library: no draw, still sacrifices, marks loss flag (CR 704.5b).
/// </summary>
public class HedronArchiveTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void HedronArchive_IsArtifact_FourCost()
    {
        var archive = HedronArchiveFactory.Create(_alice);

        archive.Name.Should().Be("Hedron Archive");
        archive.HasType(CardType.Artifact).Should().BeTrue();
        archive.ManaCost.Should().Be("{4}");
        archive.Owner.Should().BeSameAs(_alice);
        archive.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HedronArchive()
    {
        var card = NamedCardFactory.Create("Hedron Archive", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Hedron Archive");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{4}");
    }

    // --------------------------------------------------------------
    // Ability shape
    // --------------------------------------------------------------

    [Fact]
    public void HedronArchive_HasOneManaAbility_AndOneActivatedAbility()
    {
        var archive = HedronArchiveFactory.Create(_alice);

        archive.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        archive.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TapForColorless_ProducesTwoGeneric()
    {
        var archive = HedronArchiveFactory.Create(_alice);
        var ma = archive.Abilities.OfType<ManaAbility>().Single();

        // {C}{C} folds into the generic bucket via ManaCost.Parse (CR 107.4c).
        ma.ManaGenerated.TotalValue.Should().Be(2);
    }

    [Fact]
    public void DrawAbility_Has_TwoMana_Tap_AndSacrifice_NoTargets()
    {
        var archive = HedronArchiveFactory.Create(_alice);

        var draw = archive.Abilities.OfType<ActivatedAbility>().Single();

        draw.TargetRequests.Should().BeEmpty();

        draw.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("2"),
                "the cantrip costs {2}");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the cantrip taps Hedron Archive");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cantrip sacrifices Hedron Archive");
    }

    // --------------------------------------------------------------
    // {2}, {T}, Sacrifice: Draw two cards
    // --------------------------------------------------------------

    [Fact]
    public void Activate_Cantrip_DrawsTwoCards_AndSacrificesHedronArchive()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var second = new Card("Second of library", "");
        second.SetOwner(_alice);
        _alice.Zones.Library.AddCard(second);
        second.SetZone(ZoneType.Library);

        var archive = HedronArchiveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(archive);
        archive.SetZone(ZoneType.Battlefield);

        var draw = archive.Abilities.OfType<ActivatedAbility>().Single();

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Hand.GetCards().Should().Contain(second);
        _alice.Zones.Hand.GetCards().Should().HaveCount(2);
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        _alice.Zones.Graveyard.GetCards().Should().Contain(archive);
        archive.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Cantrip_EmptyLibrary_NoDraw_StillSacrifices()
    {
        var archive = HedronArchiveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(archive);
        archive.SetZone(ZoneType.Battlefield);

        var draw = archive.Abilities.OfType<ActivatedAbility>().Single();

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(archive);
        archive.Zone.Should().Be(ZoneType.Graveyard);
        // Fx.DrawCards marks CR 704.5b loss flag on empty library.
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }
}

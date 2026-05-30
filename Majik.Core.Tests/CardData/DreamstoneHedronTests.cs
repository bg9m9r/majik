using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DreamstoneHedronFactory"/>.
///
/// Dreamstone Hedron — Artifact {6}.
///   "{T}: Add {C}{C}{C}.
///    {3}, {T}, Sacrifice this artifact: Draw three cards."
///
/// Covers:
/// - Card identity (Artifact, mana cost {6}).
/// - NamedCardFactory dispatch.
/// - Mana ability shape ({T}: Add {C}{C}{C} — three colourless).
/// - Cantrip activated ability cost shape ({3}, {T}, Sacrifice).
/// - Cantrip resolve: sacrifices Dreamstone Hedron + draws three cards.
/// - Empty-library: no draw, still sacrifices (CR 704.5b loss flag handled
///   by the draw path).
/// </summary>
public class DreamstoneHedronTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void DreamstoneHedron_IsArtifact_SixCost()
    {
        var hedron = DreamstoneHedronFactory.Create(_alice);

        hedron.Name.Should().Be("Dreamstone Hedron");
        hedron.HasType(CardType.Artifact).Should().BeTrue();
        hedron.ManaCost.Should().Be("{6}");
        hedron.Owner.Should().BeSameAs(_alice);
        hedron.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DreamstoneHedron()
    {
        var card = NamedCardFactory.Create("Dreamstone Hedron", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Dreamstone Hedron");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{6}");
    }

    // --------------------------------------------------------------
    // Ability shape
    // --------------------------------------------------------------

    [Fact]
    public void DreamstoneHedron_HasOneManaAbility_AndOneActivatedAbility()
    {
        var hedron = DreamstoneHedronFactory.Create(_alice);

        hedron.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        hedron.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TapForColorless_ProducesThreeGeneric()
    {
        var hedron = DreamstoneHedronFactory.Create(_alice);
        var ma = hedron.Abilities.OfType<ManaAbility>().Single();

        // {C}{C}{C} folds into the generic bucket via ManaCost.Parse (CR 107.4c).
        ma.ManaGenerated.TotalValue.Should().Be(3);
    }

    [Fact]
    public void DrawAbility_Has_ThreeMana_Tap_AndSacrifice_NoTargets()
    {
        var hedron = DreamstoneHedronFactory.Create(_alice);

        var draw = hedron.Abilities.OfType<ActivatedAbility>().Single();

        draw.TargetRequests.Should().BeEmpty();

        draw.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("3"),
                "the cantrip costs {3}");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the cantrip taps Dreamstone Hedron");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cantrip sacrifices Dreamstone Hedron");
    }

    // --------------------------------------------------------------
    // {3}, {T}, Sacrifice: Draw three cards
    // --------------------------------------------------------------

    [Fact]
    public void Activate_Cantrip_DrawsThreeCards_AndSacrificesDreamstoneHedron()
    {
        var drawn = new List<Card>();
        for (var i = 0; i < 3; i++)
        {
            var c = new Card($"Library card {i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
            drawn.Add(c);
        }

        var hedron = DreamstoneHedronFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hedron);
        hedron.SetZone(ZoneType.Battlefield);

        var draw = hedron.Abilities.OfType<ActivatedAbility>().Single();

        // {3}, {T}, Sacrifice are all costs (the JSON schema path pays the
        // sacrifice via the cost, not in the resolve closure). Float {3} so
        // the mana pip is affordable, pay all costs, then resolve the draw.
        _alice.AddManaToPool(ManaCost.Parse("3"));
        foreach (var cost in draw.Costs)
        {
            cost.Pay(_alice);
        }
        draw.Resolve();

        foreach (var c in drawn)
        {
            _alice.Zones.Hand.GetCards().Should().Contain(c);
        }
        _alice.Zones.Hand.GetCards().Should().HaveCount(3);
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        _alice.Zones.Graveyard.GetCards().Should().Contain(hedron);
        hedron.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Cantrip_EmptyLibrary_NoDraw_StillSacrifices()
    {
        var hedron = DreamstoneHedronFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hedron);
        hedron.SetZone(ZoneType.Battlefield);

        var draw = hedron.Abilities.OfType<ActivatedAbility>().Single();

        _alice.AddManaToPool(ManaCost.Parse("3"));
        foreach (var cost in draw.Costs)
        {
            cost.Pay(_alice);
        }
        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(hedron);
        hedron.Zone.Should().Be(ZoneType.Graveyard);
    }
}

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
/// Unit tests for <see cref="MindStoneFactory"/>.
///
/// Mind Stone — Artifact {2}.
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice Mind Stone: Draw a card."
///
/// Covers:
/// - Card identity (Artifact, mana cost {2}).
/// - NamedCardFactory dispatch.
/// - Mana ability shape ({T}: Add {C}).
/// - Cantrip activated ability cost shape ({1}, {T}, Sacrifice).
/// - Cantrip resolve: sacrifices Mind Stone + draws one card.
/// - Empty-library: no draw, still sacrifices, marks loss flag (CR 704.5b).
/// </summary>
public class MindStoneTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void MindStone_IsArtifact_TwoCost()
    {
        var stone = MindStoneFactory.Create(_alice);

        stone.Name.Should().Be("Mind Stone");
        stone.HasType(CardType.Artifact).Should().BeTrue();
        stone.ManaCost.Should().Be("{2}");
        stone.Owner.Should().BeSameAs(_alice);
        stone.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MindStone()
    {
        var card = NamedCardFactory.Create("Mind Stone", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Mind Stone");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
    }

    // --------------------------------------------------------------
    // Ability shape
    // --------------------------------------------------------------

    [Fact]
    public void MindStone_HasOneManaAbility_AndOneActivatedAbility()
    {
        var stone = MindStoneFactory.Create(_alice);

        stone.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        stone.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TapForColorless_ProducesOneGeneric()
    {
        var stone = MindStoneFactory.Create(_alice);
        var ma = stone.Abilities.OfType<ManaAbility>().Single();

        // {C} folds into the generic bucket via ManaCost.Parse (CR 107.4c).
        ma.ManaGenerated.TotalValue.Should().Be(1);
    }

    [Fact]
    public void DrawAbility_Has_OneMana_Tap_AndSacrifice_NoTargets()
    {
        var stone = MindStoneFactory.Create(_alice);

        var draw = stone.Abilities.OfType<ActivatedAbility>().Single();

        draw.TargetRequests.Should().BeEmpty();

        draw.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "the cantrip costs {1}");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the cantrip taps Mind Stone");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cantrip sacrifices Mind Stone");
    }

    // --------------------------------------------------------------
    // {1}, {T}, Sacrifice: Draw a card
    // --------------------------------------------------------------

    [Fact]
    public void Activate_Cantrip_DrawsACard_AndSacrificesMindStone()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var stone = MindStoneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(stone);
        stone.SetZone(ZoneType.Battlefield);

        var draw = stone.Abilities.OfType<ActivatedAbility>().Single();

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(stone);
        stone.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Cantrip_EmptyLibrary_NoDraw_StillSacrifices()
    {
        var stone = MindStoneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(stone);
        stone.SetZone(ZoneType.Battlefield);

        var draw = stone.Abilities.OfType<ActivatedAbility>().Single();

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(stone);
        stone.Zone.Should().Be(ZoneType.Graveyard);
        // Fx.DrawCards marks CR 704.5b loss flag on empty library.
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }
}

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
/// Tests for <see cref="AsmoranomardicadaistinaculdacarFactory"/> (Modern
/// Horizons 2, {B}{R}{G}).
///
/// Coverage:
///   - Identity (Legendary Creature — Human Shaman 4/4, {B}{R}{G}).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Tutor activated ability:
///       * shape (single {T} cost, sorcery-speed rider).
///       * Food card in library → moved to hand, library shuffled.
///       * Non-Food cards in library → no-op (still safe to call).
/// </summary>
public class AsmoranomardicadaistinaculdacarTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Asmoran_Identity_LegendaryCreature_4_4_AtCostBRG()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);

        card.Name.Should().Be("Asmoranomardicadaistinaculdacar");
        card.ManaCost.Should().Be("{B}{R}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(4);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Asmoran()
    {
        var card = NamedCardFactory.Create("Asmoranomardicadaistinaculdacar", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Asmoranomardicadaistinaculdacar");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Asmoran_HasOneActivatedAbility_SorcerySpeed_WithTapCost()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);

        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1,
            "the printed {T}: tutor-a-Food activated ability");

        var ability = abilities[0];
        ability.IsSorcerySpeed.Should().BeTrue(
            "the printed 'Activate only as a sorcery' rider (CR 117.1a / 307.5)");

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "{T} is the only cost");
    }

    [Fact]
    public void Asmoran_HasStaticAbilityMarkers_ForUnshippedPrimitives()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Should().Contain(k => k.Keyword == AsmoranomardicadaistinaculdacarFactory
            .MayLookAtTopOfLibraryMarker,
            "the 'may look at top of library' static ability is surfaced as a marker pending the primitive");
        keywords.Should().Contain(k => k.Keyword == AsmoranomardicadaistinaculdacarFactory
            .MayCastFoodFromLibraryMarker,
            "the 'may cast Food spells from top of library' static ability is surfaced as a marker pending the primitive");
    }

    [Fact]
    public void Tutor_FindsFoodCardInLibrary_AndMovesToHand()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Stock the library: one Food card and one non-Food artifact.
        var foodCard = new Artifact("Trail of Crumbs", "{1}{G}",
            subtypes: new[] { CardSubtype.Food })
        {
            Owner = _alice,
        };
        _alice.Zones.Library.AddCard(foodCard);
        foodCard.SetZone(ZoneType.Library);

        var nonFood = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(nonFood);
        nonFood.SetZone(ZoneType.Library);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects)
            effect.Execute();

        // Food card moved to hand.
        foodCard.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(foodCard);
        // Non-Food card stays in the library.
        _alice.Zones.Library.GetCards().Should().Contain(nonFood);
    }

    [Fact]
    public void Tutor_NoFoodInLibrary_IsCleanNoOp()
    {
        var card = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Library has no Food cards.
        var nonFood = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(nonFood);
        nonFood.SetZone(ZoneType.Library);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects)
            effect.Execute();

        // Nothing moved (CR 701.19a — no Food to find; the printed
        // "then shuffle" runs unconditionally but the library still
        // contains only Sol Ring).
        _alice.Zones.Hand.GetCards().Should().NotContain(nonFood);
        _alice.Zones.Library.GetCards().Should().Contain(nonFood);
    }

    private void SeatOnBattlefield(Creature card)
    {
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}

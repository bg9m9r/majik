using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Pest Control (Modern Horizons 3, {W}{B}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Destroy all nonland permanents with mana value 1 or less.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Mana-value-filtered mass destruction (CR 109.5 / 701.7) widened to all
/// nonland permanents (CR 110 / 305.1), paired with Cycling {2} (CR 702.32).
/// The dispatch + well-formedness asserts live in
/// <see cref="Majik.Core.Tests.CardData.CardFactoryContractTests"/>; this
/// class covers only Pest Control's unique behaviour + a single identity
/// assert.
/// </summary>
[Trait("Color", "M")]
public class PestControlFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity (multicolour stats)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_WhiteBlack_AtCostWB()
    {
        var card = PestControlFactory.Create(_alice);

        card.Name.Should().Be("Pest Control");
        card.ManaCost.Should().Be("{W}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White);
        colors.Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Destroy sweep — nonland permanents with mana value 1 or less
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysNonlandPermanents_WithManaValue1OrLess_AcrossAllBattlefields()
    {
        var aliceMv1 = NewControlledPermanent<Creature>(_alice, "Savannah Lions", "{W}", 2, 1); // MV 1
        var aliceMv0 = NewControlledPermanent<Artifact>(_alice, "Ornithopter", "{0}");           // MV 0
        var bobMv1 = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");                  // MV 1

        Resolve();

        aliceMv1.Zone.Should().Be(ZoneType.Graveyard, "MV 1 ≤ 1 (CR 701.7)");
        aliceMv0.Zone.Should().Be(ZoneType.Graveyard, "MV 0 ≤ 1");
        bobMv1.Zone.Should().Be(ZoneType.Graveyard,
            "the sweep is untargeted — it reaches every battlefield (CR 109.5)");
    }

    [Fact]
    public void Resolve_SparesNonlandPermanents_WithManaValue2OrMore()
    {
        var bear = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2); // MV 2

        Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            because: "mana value 2 > 1 — spared (CR 202.3)");
    }

    [Fact]
    public void Resolve_SparesLands_EvenAtManaValue0()
    {
        // Lands have mana value 0 (no mana cost) but are explicitly excluded
        // by the "nonland" qualifier (CR 305.1).
        var forest = new Land("Forest", supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_bob);
        forest.SetController(_bob);
        forest.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(forest);

        Resolve();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            because: "lands are excluded by 'nonland' even at mana value 0 (CR 305.1)");
    }

    // -----------------------------------------------------------------------
    // Cycling {2} — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void PestControl_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelf()
    {
        var card = PestControlFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {2} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Cycling {2} charges 2 generic mana");
    }

    [Fact]
    public void PestControl_HasCyclingKeywordMarker()
    {
        var card = PestControlFactory.Create(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve()
    {
        foreach (var fx in PestControlFactory.BuildResolveEffect(new[] { _alice, _bob }))
        {
            fx.Execute();
        }
    }

    private T NewControlledPermanent<T>(Player owner, string name, string cost,
        int power = 0, int toughness = 0)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Creature))
        {
            card = (T)(ICard)new Creature(name, cost, power, toughness);
        }
        else if (typeof(T) == typeof(Artifact))
        {
            card = (T)(ICard)new Artifact(name, cost);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}

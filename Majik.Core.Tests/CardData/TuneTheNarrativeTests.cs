using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TuneTheNarrativeFactory"/>.
///
/// Tune the Narrative (Aetherdrift, {U}, Instant):
///   "Draw a card. You get {E}{E} (two energy counters)."
///
/// The contract test (<c>CardFactoryContractTests</c>) already asserts
/// dispatch + well-formedness, so this suite covers only the card's UNIQUE
/// behaviour:
///   - Identity assert for the exact printed mana cost ({U}).
///   - Resolve: draws one card AND gains exactly two energy.
///   - Resolve order is independent — energy is gained even when the library
///     is empty (the draw flags the empty-draw SBA but the energy still
///     lands).
/// </summary>
[Trait("Color", "U")]
public class TuneTheNarrativeTests
{
    private readonly Player _alice = new("Alice", 20);

    private ICard SeedLibraryCard(string name)
    {
        var card = new Instant(name, "{U}");
        card.SetOwner(_alice);
        card.SetController(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    [Fact]
    public void TuneTheNarrative_Identity_IsBlueInstantForOneU()
    {
        var card = TuneTheNarrativeFactory.Create(_alice);

        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{U}", "printed cost is {U} (CR 117.5)");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TuneTheNarrative_Resolve_DrawsOneCardAndGainsTwoEnergy()
    {
        var top = SeedLibraryCard("Top");
        SeedLibraryCard("Next");

        _alice.EnergyCounters.Should().Be(0, "no energy before resolution");

        var effect = TuneTheNarrativeFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        // CR 121.1 — drew exactly the top card into hand.
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        top.Zone.Should().Be(ZoneType.Hand);

        // CR 122 — gained exactly two energy counters.
        _alice.EnergyCounters.Should().Be(2,
            "Tune the Narrative grants {E}{E} (two energy counters)");
    }

    [Fact]
    public void TuneTheNarrative_Resolve_EmptyLibrary_StillGainsTwoEnergy_FlagsEmptyDraw()
    {
        // No library cards: the draw flags the empty-draw SBA (CR 704.5b)
        // but the energy half of the resolution still lands.
        var effect = TuneTheNarrativeFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library flags the SBA-driven loss (CR 704.5b)");
        _alice.EnergyCounters.Should().Be(2,
            "the {E}{E} clause resolves independently of the draw outcome");
    }
}

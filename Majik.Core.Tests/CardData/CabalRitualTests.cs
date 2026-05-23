using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CabalRitualFactory"/>.
///
/// Cabal Ritual (Torment / Modern Horizons 2, {B}, Instant):
///   "Add {B}{B}{B}.
///    Threshold — Add {C}{C}{C}{C}{C} instead if seven or more cards
///    are in your graveyard."
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with 0 / 6 graveyard cards — base output (+3 B).
///   - Resolve with exactly 7 graveyard cards — threshold output
///     (+5 generic via {C}{C}{C}{C}{C} per CR 107.4c).
///   - Resolve with 10 graveyard cards — threshold output still applies
///     (the gate is ≥ 7, not == 7).
///   - "Instead" semantics (CR 702.50b): threshold REPLACES the base
///     output rather than stacking on top of it.
/// </summary>
public class CabalRitualTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CabalRitual_HasExpectedShape()
    {
        var card = CabalRitualFactory.Create(_alice);

        card.Name.Should().Be("Cabal Ritual");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CabalRitual()
    {
        var card = NamedCardFactory.Create("Cabal Ritual", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Cabal Ritual");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_EmptyGraveyard_AddsThreeBlackMana()
    {
        // No cards in graveyard — threshold (CR 702.50) not met; base
        // clause fires and produces {B}{B}{B}.
        _alice.ManaPool.Total.Should().Be(0);

        var effect = CabalRitualFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Black.Should().Be(3);
        _alice.ManaPool.Generic.Should().Be(0);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Blue.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(3);
    }

    [Fact]
    public void Resolve_SixGraveyardCards_StillBelowThreshold_AddsThreeBlack()
    {
        // Six cards in graveyard — strictly less than 7, so threshold
        // does NOT trigger. Output stays {B}{B}{B}.
        SeedGraveyard(6);
        CabalRitualFactory.IsThresholdActive(_alice).Should().BeFalse();

        var effect = CabalRitualFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Black.Should().Be(3);
        _alice.ManaPool.Generic.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(3);
    }

    [Fact]
    public void Resolve_SevenGraveyardCards_MeetsThreshold_AddsFiveColourless()
    {
        // Exactly 7 cards — threshold (CR 702.50) is satisfied. "Instead"
        // semantics (CR 702.50b) replace the base output: +5 colourless,
        // 0 black. {C} routes into the generic bucket per CR 107.4c.
        SeedGraveyard(7);
        CabalRitualFactory.IsThresholdActive(_alice).Should().BeTrue();

        var effect = CabalRitualFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Generic.Should().Be(5);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Blue.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(5);
    }

    [Fact]
    public void Resolve_TenGraveyardCards_ThresholdHolds_AddsFiveColourless()
    {
        // Threshold is a ≥ gate, not == — overshooting still produces the
        // same colourless output. Verifies the "instead" semantics one
        // more time at a deeper graveyard.
        SeedGraveyard(10);
        CabalRitualFactory.IsThresholdActive(_alice).Should().BeTrue();

        var effect = CabalRitualFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Generic.Should().Be(5);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(5);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void SeedGraveyard(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Filler{i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }
    }
}

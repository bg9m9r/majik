using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for <see cref="EscapeAlternativeCost"/> — CR 702.138
/// cast-from-graveyard alt cost with the "exile N other graveyard
/// cards" rider. Covers <see cref="EscapeAlternativeCost.CanCastFor"/>
/// gating + <see cref="EscapeAlternativeCost.Pay"/> atomicity. Pipeline-
/// level cast integration is exercised by the Phlage / Uro / Phoenix /
/// Cling factory tests; this file is the cost primitive in isolation.
/// </summary>
public class EscapeAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Construction ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ZeroExileCount_Throws()
    {
        Action act = () => new EscapeAlternativeCost(ManaCost.Parse("{2}{B}"), 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NullManaCost_Throws()
    {
        Action act = () => new EscapeAlternativeCost(null!, 3);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── CanCastFor — zone / owner / candidate-pool gates ────────────────────

    [Fact]
    public void CanCastFor_CardInGraveyard_SufficientOthers_ReturnsTrue()
    {
        var phlage = NewInGraveyard(_alice, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        // 3 OTHER cards in the graveyard.
        NewInGraveyard(_alice, new Instant("Filler 1", "{1}"));
        NewInGraveyard(_alice, new Instant("Filler 2", "{1}"));
        NewInGraveyard(_alice, new Instant("Filler 3", "{1}"));

        var cost = new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), exileFromGraveyardCount: 3);
        cost.CanCastFor(phlage, _alice).Should().BeTrue();
    }

    [Fact]
    public void CanCastFor_CardInHand_ReturnsFalse()
    {
        var phlage = new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4);
        phlage.SetOwner(_alice);
        phlage.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(phlage);

        // Stock graveyard with enough cards so the only failure is the zone.
        NewInGraveyard(_alice, new Instant("Filler 1", "{1}"));
        NewInGraveyard(_alice, new Instant("Filler 2", "{1}"));
        NewInGraveyard(_alice, new Instant("Filler 3", "{1}"));

        var cost = new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3);
        cost.CanCastFor(phlage, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_CardInOpponentGraveyard_ReturnsFalse()
    {
        var phlage = NewInGraveyard(_bob, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        // Caster's own graveyard has enough — but card is owned by Bob.
        NewInGraveyard(_alice, new Instant("Filler 1", "{1}"));
        NewInGraveyard(_alice, new Instant("Filler 2", "{1}"));
        NewInGraveyard(_alice, new Instant("Filler 3", "{1}"));

        var cost = new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3);
        cost.CanCastFor(phlage, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_InsufficientOthers_ReturnsFalse()
    {
        var phlage = NewInGraveyard(_alice, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        // Only 2 OTHER cards — Phlage's exile rider is 3.
        NewInGraveyard(_alice, new Instant("Filler 1", "{1}"));
        NewInGraveyard(_alice, new Instant("Filler 2", "{1}"));

        var cost = new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3);
        cost.CanCastFor(phlage, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_SpellCardCountedAsItself_NotOther()
    {
        // Phlage in graveyard + exactly N-1 OTHER cards → still illegal.
        // Asserts the "other" carve-out is honored (CR 702.138a).
        var phlage = NewInGraveyard(_alice, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        NewInGraveyard(_alice, new Instant("Filler 1", "{1}"));
        NewInGraveyard(_alice, new Instant("Filler 2", "{1}"));
        // Graveyard total = 3 cards (Phlage + 2). Escape needs 3 OTHER → 2 others is not enough.

        var cost = new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3);
        cost.CanCastFor(phlage, _alice).Should().BeFalse();
    }

    // ── Pay — atomicity + exile rider ────────────────────────────────────────

    [Fact]
    public void Pay_SufficientOthers_MovesNCardsGraveyardToExile_LeavesSpellInGrave()
    {
        var uro = NewInGraveyard(_alice, new Creature("Uro, Titan of Nature's Wrath", "{1}{G}{U}", 6, 6));
        var f1 = NewInGraveyard(_alice, new Instant("Filler 1", "{1}"));
        var f2 = NewInGraveyard(_alice, new Instant("Filler 2", "{1}"));
        var f3 = NewInGraveyard(_alice, new Instant("Filler 3", "{1}"));
        var f4 = NewInGraveyard(_alice, new Instant("Filler 4", "{1}"));
        var f5 = NewInGraveyard(_alice, new Instant("Filler 5", "{1}"));

        var cost = new EscapeAlternativeCost(ManaCost.Parse("{G}{G}{U}{U}"), 5);
        cost.Pay(_alice, uro).Should().BeTrue();

        cost.Paid.Should().BeTrue();
        cost.ExiledCards.Should().HaveCount(5);
        cost.ExiledCards.Should().NotContain(uro, "the spell card itself must not be picked as part of the 'other' exile rider");

        // Each picked filler moved to exile.
        foreach (var filler in new[] { f1, f2, f3, f4, f5 })
        {
            filler.Zone.Should().Be(ZoneType.Exile);
            _alice.Zones.Exile.GetCards().Should().Contain(filler);
            _alice.Zones.Graveyard.GetCards().Should().NotContain(filler);
        }

        // Uro is still in the graveyard — the cast pipeline (not the
        // alt-cost) moves it Stack → Battlefield on resolve.
        uro.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(uro);
    }

    [Fact]
    public void Pay_InsufficientOthers_ReturnsFalse_NoMutation()
    {
        var phlage = NewInGraveyard(_alice, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        var only = NewInGraveyard(_alice, new Instant("Only filler", "{1}"));
        // 1 other, needs 3.

        var cost = new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3);
        cost.Pay(_alice, phlage).Should().BeFalse();
        cost.Paid.Should().BeFalse();
        cost.ExiledCards.Should().BeEmpty();

        // Atomicity — no zone mutation on failure (CR 601.2g).
        only.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(only);
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Pay_IsIdempotent_SecondCallReturnsTrue_DoesNotReexile()
    {
        var phlage = NewInGraveyard(_alice, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        NewInGraveyard(_alice, new Instant("F1", "{1}"));
        NewInGraveyard(_alice, new Instant("F2", "{1}"));
        NewInGraveyard(_alice, new Instant("F3", "{1}"));

        var cost = new EscapeAlternativeCost(ManaCost.Parse("{2}{R}{W}"), 3);
        cost.Pay(_alice, phlage).Should().BeTrue();
        var firstExiledSnapshot = cost.ExiledCards.ToList();

        // Second call short-circuits — does not re-pick anything.
        cost.Pay(_alice, phlage).Should().BeTrue();
        cost.ExiledCards.Should().Equal(firstExiledSnapshot);
        _alice.Zones.Exile.GetCards().Count().Should().Be(3, "the second Pay call must not exile anything new");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T NewInGraveyard<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }
}

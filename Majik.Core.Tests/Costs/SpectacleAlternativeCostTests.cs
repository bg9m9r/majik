using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// CR 702.118 (Spectacle) — alt-cost legality + binder parsing + per-turn
/// life-loss tracking on <see cref="Player"/>.
///
/// Card under test: Skewer the Critics — "Spectacle {R} (You may cast this
/// spell for its spectacle cost rather than its mana cost if an opponent lost
/// life this turn.) Skewer the Critics deals 3 damage to any target."
/// </summary>
public class SpectacleAlternativeCostTests
{
    private const string SkewerOracle =
        "Spectacle {R} (You may cast this spell for its spectacle cost rather than " +
        "its mana cost if an opponent lost life this turn.)\n" +
        "Skewer the Critics deals 3 damage to any target.";

    // ───────────────────────────────────────────────────────────────────
    // Per-turn life-loss tracking (Player)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void LoseLife_IncrementsLifeLostThisTurn()
    {
        var p = new Player("Alice", 20);

        p.LoseLife(3);
        p.LifeLostThisTurn.Should().Be(3);

        p.LoseLife(2);
        p.LifeLostThisTurn.Should().Be(5);
    }

    [Fact]
    public void LoseLife_Zero_DoesNotCountAsLifeLoss()
    {
        // CR 119.4 — losing 0 life isn't "losing life".
        var p = new Player("Alice", 20);
        p.LoseLife(0);
        p.LifeLostThisTurn.Should().Be(0);
    }

    [Fact]
    public void ResetTurnTrackers_ZeroesLifeLost()
    {
        var p = new Player("Alice", 20);
        p.LoseLife(7);
        p.LifeLostThisTurn.Should().Be(7);

        p.ResetTurnTrackers();
        p.LifeLostThisTurn.Should().Be(0);
    }

    // ───────────────────────────────────────────────────────────────────
    // SpectacleAlternativeCost — legality
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void CanCastFor_OpponentLostLife_CardInHand_True()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.LoseLife(2); // burn earlier in turn

        var skewer = new Sorcery("Skewer the Critics", "2R")
        { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(skewer);

        var cost = new SpectacleAlternativeCost(
            ManaCost.Parse("R"), new[] { bob });

        cost.CanCastFor(skewer, alice).Should().BeTrue();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));
    }

    [Fact]
    public void CanCastFor_NoOpponentLostLife_False()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20); // bob.LifeLostThisTurn == 0

        var skewer = new Sorcery("Skewer the Critics", "2R")
        { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(skewer);

        var cost = new SpectacleAlternativeCost(
            ManaCost.Parse("R"), new[] { bob });

        cost.CanCastFor(skewer, alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_OnlyCasterLostLife_False()
    {
        // CR 702.118a — must be an OPPONENT's life loss, not the caster's.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        alice.LoseLife(3);

        var skewer = new Sorcery("Skewer the Critics", "2R")
        { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(skewer);

        var cost = new SpectacleAlternativeCost(
            ManaCost.Parse("R"), new[] { bob });

        cost.CanCastFor(skewer, alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_CardNotInHand_False()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.LoseLife(2);

        var skewer = new Sorcery("Skewer the Critics", "2R")
        { Owner = alice, Zone = ZoneType.Graveyard };

        var cost = new SpectacleAlternativeCost(
            ManaCost.Parse("R"), new[] { bob });

        cost.CanCastFor(skewer, alice).Should().BeFalse();
    }

    [Fact]
    public void OnResolved_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.LoseLife(2);
        var skewer = new Sorcery("Skewer the Critics", "2R")
        { Owner = alice, Zone = ZoneType.Hand };

        var cost = new SpectacleAlternativeCost(
            ManaCost.Parse("R"), new[] { bob });

        var act = () => cost.OnResolved(skewer, alice);
        act.Should().NotThrow();
        // Card zone unchanged by the cost itself (engine moves it to gy).
        skewer.Zone.Should().Be(ZoneType.Hand);
    }

    // ───────────────────────────────────────────────────────────────────
    // SpectacleBinder — oracle text parsing
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Binder_TryParseCost_SkewerOracle_ReturnsR()
    {
        SpectacleBinder.TryParseCost(SkewerOracle, out var cost).Should().BeTrue();
        cost.Should().Be(ManaCost.Parse("R"));
    }

    [Fact]
    public void Binder_TryParseCost_NoSpectacle_False()
    {
        SpectacleBinder.TryParseCost(
            "Lightning Bolt deals 3 damage to any target.", out var cost)
            .Should().BeFalse();
        cost.Should().Be(ManaCost.Zero);
    }

    [Fact]
    public void Binder_TryParseCost_MultiSymbolCost()
    {
        // Hypothetical "Spectacle {1}{R}" — verifies regex tolerance.
        SpectacleBinder.TryParseCost(
            "Spectacle {1}{R} (...)", out var cost).Should().BeTrue();
        cost.Should().Be(ManaCost.Parse("1R"));
    }

    [Fact]
    public void Binder_TryBind_OpponentLostLife_YieldsAltCost()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.LoseLife(1);

        var alt = SpectacleBinder.TryBind(SkewerOracle, alice, new[] { alice, bob });

        alt.Should().NotBeNull();
        alt!.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));
    }

    [Fact]
    public void Binder_TryBind_NoOpponentLifeLoss_ReturnsNull()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var alt = SpectacleBinder.TryBind(SkewerOracle, alice, new[] { alice, bob });

        alt.Should().BeNull();
    }

    [Fact]
    public void Binder_TryBind_NoSpectacleText_ReturnsNull()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.LoseLife(3); // would satisfy condition if text matched

        var alt = SpectacleBinder.TryBind(
            "Lightning Bolt deals 3 damage to any target.",
            alice, new[] { alice, bob });

        alt.Should().BeNull();
    }
}

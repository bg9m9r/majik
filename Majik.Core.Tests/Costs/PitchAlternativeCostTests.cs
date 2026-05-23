using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for <see cref="PitchAlternativeCost"/> — the Force-of-Will-cycle
/// alternative cost (CR 118.9 + "if it's not your turn" timing predicate).
/// </summary>
public class PitchAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── CanCastFor (color/zone/owner gates) ──────────────────────────────────

    [Fact]
    public void CanCastFor_BlueCardInHand_OwnedBySelf_ReturnsTrue()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var fow = BlueInstantInHand(_alice, "Force of Will", "{3}{U}{U}");

        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);
        cost.CanCastFor(fow, _alice).Should().BeTrue();
    }

    [Fact]
    public void CanCastFor_NonBlueCard_ReturnsFalse()
    {
        var redInstant = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        redInstant.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(redInstant);
        var fow = BlueInstantInHand(_alice, "Force of Will", "{3}{U}{U}");

        var cost = new PitchAlternativeCost(ManaColor.Blue, redInstant, lifeCost: 1);
        cost.CanCastFor(fow, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_PitchCardInGraveyard_ReturnsFalse()
    {
        var brainstorm = new Instant("Brainstorm", "{U}") { Owner = _alice };
        brainstorm.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(brainstorm);
        var fow = BlueInstantInHand(_alice, "Force of Will", "{3}{U}{U}");

        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);
        cost.CanCastFor(fow, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_PitchCardOwnedByOpponent_ReturnsFalse()
    {
        var brainstorm = BlueInstantInHand(_bob, "Brainstorm", "{U}");
        var fow = BlueInstantInHand(_alice, "Force of Will", "{3}{U}{U}");

        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);
        cost.CanCastFor(fow, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_PitchCardSameAsSpell_ReturnsFalse()
    {
        // Can't pitch the spell itself.
        var fow = BlueInstantInHand(_alice, "Force of Will", "{3}{U}{U}");

        var cost = new PitchAlternativeCost(ManaColor.Blue, fow, lifeCost: 1);
        cost.CanCastFor(fow, _alice).Should().BeFalse();
    }

    // ── IsLegalInContext (timing gate) ───────────────────────────────────────

    [Fact]
    public void IsLegalInContext_OpponentsTurn_ReturnsTrue()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);

        // Active player is Bob — Alice can pitch.
        cost.IsLegalInContext(_bob).Should().BeTrue();
    }

    [Fact]
    public void IsLegalInContext_OwnTurn_ReturnsFalse()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);

        // Active player is Alice — pitch is illegal on her own turn.
        cost.IsLegalInContext(_alice).Should().BeFalse();
    }

    // ── AlternativeManaCost / Description ────────────────────────────────────

    [Fact]
    public void AlternativeManaCost_IsZero()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm);

        cost.AlternativeManaCost.Should().Be(ManaCost.Zero);
    }

    [Fact]
    public void Description_WithLifeCost_MentionsLife()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);

        cost.Description.Should().Contain("Blue");
        cost.Description.Should().Contain("1 life");
    }

    [Fact]
    public void Description_NoLifeCost_OmitsLife()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm);

        cost.Description.Should().Contain("Blue");
        cost.Description.Should().NotContain("life");
    }

    // ── OnResolved (exile + life loss) ───────────────────────────────────────

    [Fact]
    public void OnResolved_ExilesPitchCard_LosesLife()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var fow = BlueInstantInHand(_alice, "Force of Will", "{3}{U}{U}");
        var startingLife = _alice.LifeTotal;

        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);
        cost.OnResolved(fow, _alice);

        brainstorm.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(brainstorm);
        _alice.Zones.Hand.GetCards().Should().NotContain(brainstorm);
        _alice.LifeTotal.Should().Be(startingLife - 1);
    }

    [Fact]
    public void OnResolved_NoLifeCost_DoesNotLoseLife()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var fon = BlueInstantInHand(_alice, "Force of Negation", "{1}{U}{U}");
        var startingLife = _alice.LifeTotal;

        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm);
        cost.OnResolved(fon, _alice);

        brainstorm.Zone.Should().Be(ZoneType.Exile);
        _alice.LifeTotal.Should().Be(startingLife);
    }

    [Fact]
    public void OnResolved_PitchAlreadyMoved_DoesNotThrow()
    {
        var brainstorm = new Instant("Brainstorm", "{U}") { Owner = _alice };
        brainstorm.SetZone(ZoneType.Exile);
        var fow = BlueInstantInHand(_alice, "Force of Will", "{3}{U}{U}");

        var cost = new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: 1);
        var act = () => cost.OnResolved(fow, _alice);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NegativeLifeCost_Throws()
    {
        var brainstorm = BlueInstantInHand(_alice, "Brainstorm", "{U}");
        var act = () => new PitchAlternativeCost(ManaColor.Blue, brainstorm, lifeCost: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Instant BlueInstantInHand(Player owner, string name, string cost)
    {
        var c = new Instant(name, cost) { Owner = owner };
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }
}

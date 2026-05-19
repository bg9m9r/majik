using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Locks in the post-Phase-2-slice-2 encapsulation contract:
/// - ICard surface is read-only (no settable Owner/Controller/Zone).
/// - Concrete Card exposes behavior methods (ChangeOwner/ChangeController)
///   for external mutation; raw setters are internal-only.
/// - Player mutation flags (TriedToDrawFromEmptyLibrary, PoisonCounters,
///   Commander, HasLost) are surfaced through semantic methods.
/// </summary>
public class EncapsulationTests
{
    [Fact]
    public void ICard_SurfaceIsReadOnly()
    {
        var props = typeof(ICard).GetProperties();
        var ownerSetter = props.First(p => p.Name == nameof(ICard.Owner)).SetMethod;
        var controllerSetter = props.First(p => p.Name == nameof(ICard.Controller)).SetMethod;
        var zoneSetter = props.First(p => p.Name == nameof(ICard.Zone)).SetMethod;

        ownerSetter.Should().BeNull("ICard.Owner must be read-only for external consumers");
        controllerSetter.Should().BeNull("ICard.Controller must be read-only for external consumers");
        zoneSetter.Should().BeNull("ICard.Zone must be read-only for external consumers");
    }

    [Fact]
    public void Card_ChangeController_MutatesController()
    {
        var alice = new Player("Alice");
        var card = new Card("Bear", "1G", new[] { CardType.Creature });

        card.ChangeController(alice);

        card.Controller.Should().Be(alice);
    }

    [Fact]
    public void Card_ChangeOwner_MutatesOwner()
    {
        var alice = new Player("Alice");
        var card = new Card("Bear");

        card.ChangeOwner(alice);

        card.Owner.Should().Be(alice);
    }

    [Fact]
    public void Permanent_MarkAsToken_SetsIsToken()
    {
        var token = new Creature("Spirit", "", 1, 1);

        token.MarkAsToken();

        token.IsToken.Should().BeTrue();
    }

    [Fact]
    public void Permanent_ClearSummoningSickness_FlipsFlag()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.HasSummoningSickness.Should().BeTrue("creatures enter with summoning sickness (CR 302.1)");

        bear.ClearSummoningSickness();

        bear.HasSummoningSickness.Should().BeFalse();
    }

    [Fact]
    public void Player_MarkTriedToDrawFromEmptyLibrary_SetsSticky()
    {
        var alice = new Player("Alice");

        alice.MarkTriedToDrawFromEmptyLibrary();

        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue("SBA 704.5b reads this flag");
    }

    [Fact]
    public void Player_AddPoisonCounters_Accumulates()
    {
        var alice = new Player("Alice");

        alice.AddPoisonCounters(3);
        alice.AddPoisonCounters(2);

        alice.PoisonCounters.Should().Be(5);
    }

    [Fact]
    public void Player_AddPoisonCounters_NegativeThrows()
    {
        var alice = new Player("Alice");
        var act = () => alice.AddPoisonCounters(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Player_MarkLost_Idempotent()
    {
        var alice = new Player("Alice");

        alice.MarkLost();
        alice.MarkLost();

        alice.HasLost.Should().BeTrue();
    }
}

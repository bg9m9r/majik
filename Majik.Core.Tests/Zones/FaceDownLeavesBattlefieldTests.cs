using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Zones;

/// <summary>
/// CR 708.9 — a face-down permanent that leaves the battlefield is revealed;
/// in its new zone it is the card it represented, face up (CR 400.7). The
/// event still names the face-down creature that left (last known
/// information, CR 603.10), with the card it turned out to be.
/// </summary>
public class FaceDownLeavesBattlefieldTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public FaceDownLeavesBattlefieldTests() => _zones = new ZoneService(_bus);

    private (ManifestedCreature Wrapper, Card Card) Manifest()
    {
        var land = new Land("Forest");
        land.SetOwner(_alice);
        var wrapper = ManifestEffect.ManifestCard(_alice, land, _zones);
        return (wrapper, land);
    }

    private bool InAnyZone(ICard card) =>
        ZoneIntegrityZones.Any(z => _alice.Zones.GetZone(z).GetCards().Contains(card));

    private static readonly ZoneType[] ZoneIntegrityZones =
        { ZoneType.Battlefield, ZoneType.Graveyard, ZoneType.Hand, ZoneType.Exile, ZoneType.Library, ZoneType.Stack };

    [Theory]
    [InlineData(ZoneType.Graveyard)]
    [InlineData(ZoneType.Hand)]
    [InlineData(ZoneType.Exile)]
    public void TheRepresentedCard_GoesToTheNewZone_FaceUp_AndTheWrapperIsGone(ZoneType to)
    {
        var (wrapper, land) = Manifest();
        CardMovedEvent? moved = null;
        _bus.Subscribe<CardMovedEvent>(e => moved = e);

        _zones.MoveCard(wrapper, ZoneType.Battlefield, to, _alice);

        _alice.Zones.GetZone(to).GetCards().Should().ContainSingle().Which.Should().BeSameAs(land);
        land.Zone.Should().Be(to);
        InAnyZone(wrapper).Should().BeFalse("the face-down object no longer exists anywhere");
        wrapper.IsFaceDown.Should().BeFalse("it was revealed (CR 708.9)");
        moved!.Card.Should().BeSameAs(wrapper, "leaves/dies triggers see the creature that left");
        moved.RevealedCard.Should().BeSameAs(land);
    }

    [Fact]
    public void ADiesTrigger_StillSeesACreatureDie()
    {
        var (wrapper, _) = Manifest();
        var died = 0;
        _bus.Subscribe<CardMovedEvent>(e =>
        {
            if (Triggers.OnDies(wrapper).Matches(e, null!)) died++;
        });

        _zones.MoveCard(wrapper, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        died.Should().Be(1);
    }

    [Fact]
    public void AFollowUpMoveOfTheWrapper_MovesTheRevealedCard()
    {
        // "Return that card to its owner's hand" acting on the event's card.
        var (wrapper, land) = Manifest();
        _zones.MoveCard(wrapper, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        _zones.MoveCard(wrapper, ZoneType.Graveyard, ZoneType.Hand, _alice);

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(land);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}

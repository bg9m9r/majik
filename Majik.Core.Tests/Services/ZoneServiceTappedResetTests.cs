using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Services;

/// <summary>
/// CR 400.7 / 613.7 / 614 — a permanent that changes zones becomes a NEW
/// object and loses all status, including its tapped/untapped state. These
/// tests pin that a tapped permanent leaving the battlefield (to graveyard,
/// hand, exile) arrives in its new zone untapped.
/// </summary>
public class ZoneServiceTappedResetTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land TappedLandOnBattlefield()
    {
        var land = new Land("Mountain")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(land);
        land.Tap();
        land.IsTapped.Should().BeTrue("precondition — the land is tapped on the battlefield");
        return land;
    }

    [Fact]
    public void MoveCard_BattlefieldToGraveyard_ResetsTapped()
    {
        var zones = new ZoneService();
        var land = TappedLandOnBattlefield();

        zones.MoveCard(land, ZoneType.Battlefield, ZoneType.Graveyard);

        land.Zone.Should().Be(ZoneType.Graveyard);
        land.IsTapped.Should().BeFalse(
            "CR 400.7 — the card becomes a new object on zone change and loses tapped status");
    }

    [Fact]
    public void MoveCard_BattlefieldToHand_ResetsTapped()
    {
        var zones = new ZoneService();
        var land = TappedLandOnBattlefield();

        zones.MoveCard(land, ZoneType.Battlefield, ZoneType.Hand);

        land.Zone.Should().Be(ZoneType.Hand);
        land.IsTapped.Should().BeFalse(
            "CR 400.7 — bounce to hand makes a new object that is untapped");
    }

    [Fact]
    public void MoveCard_BattlefieldToExile_ResetsTapped()
    {
        var zones = new ZoneService();
        var land = TappedLandOnBattlefield();

        zones.MoveCard(land, ZoneType.Battlefield, ZoneType.Exile);

        land.Zone.Should().Be(ZoneType.Exile);
        land.IsTapped.Should().BeFalse(
            "CR 400.7 — exile makes a new object that is untapped");
    }

    [Fact]
    public void OracleSpellBinder_MoveToGraveyard_ResetsTapped()
    {
        var land = TappedLandOnBattlefield();

        OracleSpellBinder.MoveToGraveyard(land, ZoneMoveReason.Sacrifice);

        land.Zone.Should().Be(ZoneType.Graveyard);
        land.IsTapped.Should().BeFalse(
            "CR 400.7 — sacrifice to graveyard makes a new object that is untapped");
    }
}

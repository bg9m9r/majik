using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="OracleOfMulDayaFactory"/> (Zendikar, {3}{G}).
///
/// Card: Oracle of Mul Daya — Creature — Elf Shaman 2/2.
///   "You may play an additional land on each of your turns.
///    Play with the top card of your library revealed.
///    You may play lands from the top of your library."
///
/// Covers identity + dispatch, the description riders, the additional-land
/// static (CR 305.2 / 720) summed live by <see cref="LandDropTracker"/>, and
/// the battlefield-gated play-lands-from-top + reveal grant
/// (CR 601.3e / CR 305.6 / CR 715.4) registered/revoked via the bus lifecycle.
/// </summary>
[Trait("Color", "G")]
public class OracleOfMulDayaFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => LibraryTopPlayPermissions.Clear();

    private static (ZoneService zones, EventBus bus) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        return (zones, bus);
    }

    private static Land NewLand(Player owner, string name = "Forest")
    {
        var land = new Land(name, subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(owner);
        return land;
    }

    private static void EnterBattlefield(ZoneService zones, Player owner, ICard card)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        zones.MoveCardTo(card, ZoneType.Battlefield, controller: owner);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Oracle_Identity_ElfShaman_2_2_At3G()
    {
        var oracle = OracleOfMulDayaFactory.Create(_alice);

        oracle.Name.Should().Be("Oracle of Mul Daya");
        oracle.ManaCost.Should().Be("{3}{G}");
        oracle.HasType(CardType.Creature).Should().BeTrue();
        oracle.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        oracle.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        oracle.BasePower.Should().Be(2);
        oracle.BaseToughness.Should().Be(2);
        oracle.Owner.Should().BeSameAs(_alice);
        oracle.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Oracle()
    {
        var card = NamedCardFactory.Create("Oracle of Mul Daya", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Oracle of Mul Daya");
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    [Fact]
    public void Oracle_HasRiders_AdditionalLand_RevealTop_PlayLands()
    {
        var oracle = OracleOfMulDayaFactory.Create(_alice);

        var statics = oracle.Abilities.OfType<StaticAbility>().Select(s => s.Description).ToList();
        statics.Should().Contain(OracleOfMulDayaFactory.AdditionalLandDescription);
        statics.Should().Contain(OracleOfMulDayaFactory.RevealTopDescription);
        statics.Should().Contain(OracleOfMulDayaFactory.PlayLandsFromTopDescription);
    }

    // -----------------------------------------------------------------------
    // Additional-land static (CR 305.2 / 720)
    // -----------------------------------------------------------------------

    [Fact]
    public void Oracle_StampsOneAdditionalLandPlay()
    {
        var oracle = OracleOfMulDayaFactory.Create(_alice);

        oracle.AdditionalLandPlaysGranted.Should().Be(OracleOfMulDayaFactory.AdditionalLandPlays);
        OracleOfMulDayaFactory.AdditionalLandPlays.Should().Be(1);
    }

    [Fact]
    public void Oracle_OnBattlefield_RaisesEffectiveLandCapByOne()
    {
        var tracker = new LandDropTracker();

        // Without Oracle: the default one land per turn (CR 305.2).
        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(1);

        var oracle = OracleOfMulDayaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(oracle);
        oracle.SetZone(ZoneType.Battlefield);

        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(2,
            "Oracle grants one additional land play on each of your turns");
    }

    [Fact]
    public void Oracle_AdditionalLand_StacksWith_Azusa()
    {
        var tracker = new LandDropTracker();

        var oracle = OracleOfMulDayaFactory.Create(_alice);
        var azusa = AzusaLostButSeekingFactory.Create(_alice);
        foreach (var p in new Permanent[] { oracle, azusa })
        {
            _alice.Zones.Battlefield.AddCard(p);
            p.SetZone(ZoneType.Battlefield);
        }

        // 1 base + 1 (Oracle) + 2 (Azusa) = 4.
        tracker.EffectiveMaxLandDropsThisTurn(_alice).Should().Be(4,
            "additional-land statics stack additively across sources (CR 720)");
    }

    // -----------------------------------------------------------------------
    // Battlefield-gated play-from-top + reveal grant
    // -----------------------------------------------------------------------

    [Fact]
    public void Oracle_OnBattlefield_TopLand_IsPlayableAndRevealed()
    {
        var (zones, bus) = BuildEngine();
        var oracle = OracleOfMulDayaFactory.Create(_alice, bus);

        var forest = NewLand(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // Before Oracle is on the battlefield: no permission.
        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeFalse();

        EnterBattlefield(zones, _alice, oracle);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeTrue(
            "Oracle grants 'may play lands from the top of your library'");
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeTrue(
            "Oracle plays with the top card revealed (CR 715.4)");
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeSameAs(forest);
    }

    [Fact]
    public void Oracle_OnBattlefield_TopNonLand_NotPlayableButRevealed()
    {
        var (zones, bus) = BuildEngine();
        var oracle = OracleOfMulDayaFactory.Create(_alice, bus);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        EnterBattlefield(zones, _alice, oracle);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, bolt).Should().BeFalse(
            "Oracle only lets you play LANDS from the top, not a nonland");
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeNull();
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeTrue();
    }

    [Fact]
    public void Oracle_LeavesBattlefield_PermissionRevoked()
    {
        var (zones, bus) = BuildEngine();
        var oracle = OracleOfMulDayaFactory.Create(_alice, bus);

        var forest = NewLand(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        EnterBattlefield(zones, _alice, oracle);
        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeTrue();

        // Oracle dies / leaves — grant revoked (CR 603.6e).
        zones.MoveCardTo(oracle, ZoneType.Graveyard, controller: _alice);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeFalse(
            "the grant ends when Oracle leaves the battlefield");
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeFalse();
    }

    [Fact]
    public void Oracle_PlayTopLand_AdvancesRevealedTop()
    {
        var (zones, bus) = BuildEngine();
        var oracle = OracleOfMulDayaFactory.Create(_alice, bus);
        EnterBattlefield(zones, _alice, oracle);

        var topForest = NewLand(_alice, "Forest");
        var nextCard = new Instant("Opt", "{U}");
        nextCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topForest);
        topForest.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(nextCard);
        nextCard.SetZone(ZoneType.Library);

        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeSameAs(topForest);

        // Play the top land — it is played from the library (CR 601.3e).
        zones.MoveCardTo(topForest, ZoneType.Battlefield, controller: _alice);

        _alice.Zones.Library.GetCards().Should().NotContain(topForest);
        OracleOfMulDayaFactory.RevealedTopCard(_alice).Should().BeSameAs(nextCard,
            "after playing the top land the next card becomes the revealed top");
        topForest.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Oracle_DoesNotGrantTopPlay_ToOpponent()
    {
        var (zones, bus) = BuildEngine();
        var oracle = OracleOfMulDayaFactory.Create(_alice, bus);
        EnterBattlefield(zones, _alice, oracle);

        var swamp = new Land("Swamp");
        swamp.SetOwner(_bob);
        _bob.Zones.Library.AddCard(swamp);
        swamp.SetZone(ZoneType.Library);

        LibraryTopPlayPermissions.MayPlayTopCard(_bob, swamp).Should().BeFalse(
            "Oracle only grants its controller the play-from-top permission");
        LibraryTopPlayPermissions.IsTopRevealed(_bob).Should().BeFalse();
    }
}

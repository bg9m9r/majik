using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// CR 400.7 — a permanent that enters the battlefield from another zone is a
/// new object with none of the status the card had the last time it was on
/// the battlefield: no counters (CR 122.2), summoning sick (CR 302.6), a new
/// timestamp (CR 613.7d), no regeneration shields, no "activated a loyalty
/// ability this turn" (CR 606.3), no deathtouch mark (CR 704.5h), not "dealt
/// damage this turn", no imprinted cards, no evoke / blitz paid unless it was
/// just cast (CR 702.74, 702.152).
///
/// The reset is on ENTRY, so a card that just left still carries what its
/// leaves-the-battlefield and dies triggers read (CR 603.10 last known
/// information: undying, persist, The Ozolith, Unstoppable Slasher), and
/// counters an effect puts on a card in another zone stay there (suspend's
/// time counters, CR 702.62a). "Enters with" counters (CR 614.1c) land after
/// the reset.
/// </summary>
public class NewObjectOnBattlefieldEntryTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);

    public NewObjectOnBattlefieldEntryTests() => _zones = new ZoneService(_bus);

    private Creature OnBattlefield(string name = "Bear")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(_alice); c.SetController(_alice);
        c.SetZone(ZoneType.Hand); _alice.Zones.Hand.AddCard(c);
        _zones.MoveCard(c, ZoneType.Hand, ZoneType.Battlefield, _alice);
        return c;
    }

    [Theory]
    [InlineData(ZoneType.Graveyard)]
    [InlineData(ZoneType.Hand)]
    [InlineData(ZoneType.Exile)]
    [InlineData(ZoneType.Library)]
    public void ACreatureReturningToTheBattlefield_HasNoneOfItsOldStatus(ZoneType via)
    {
        var bear = OnBattlefield();
        bear.Counters.Add(CounterType.PlusOnePlusOne, 2);
        bear.Counters.Add(CounterType.MinusOneMinusOne, 1);
        bear.ClearSummoningSickness();
        bear.AddRegenerationShield();
        bear.LoyaltyAbilityActivatedThisTurn = true;
        bear.MarkedForDestructionByDeathtouch = true;
        bear.RecordDamageDealt(1);
        bear.CanAttackAsThoughItDidntHaveDefenderThisTurn = true;
        bear.EvokeWasPaid = true;
        bear.BlitzWasPaid = true;
        var imprinted = new Instant("Bolt", "{R}");
        bear.AddImprinted(imprinted);
        var firstTimestamp = bear.EnteredBattlefieldTimestamp;

        _zones.MoveCard(bear, ZoneType.Battlefield, via);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2, "still on the card it left behind (last known information)");
        _zones.MoveCard(bear, via, ZoneType.Battlefield, _alice);

        bear.Counters.HasAny.Should().BeFalse("counters don't stay with an object that changes zones (CR 122.2)");
        bear.HasSummoningSickness.Should().BeTrue("CR 302.6");
        bear.EnteredBattlefieldTimestamp.Should().NotBe(firstTimestamp, "CR 613.7d — a new timestamp");
        bear.HasRegenerationShield.Should().BeFalse();
        bear.LoyaltyAbilityActivatedThisTurn.Should().BeFalse("CR 606.3 — once per turn per object");
        bear.MarkedForDestructionByDeathtouch.Should().BeFalse();
        bear.WasDealtDamageThisTurn.Should().BeFalse();
        bear.CanAttackAsThoughItDidntHaveDefenderThisTurn.Should().BeFalse();
        bear.EvokeWasPaid.Should().BeFalse("it was not cast this time");
        bear.BlitzWasPaid.Should().BeFalse();
        bear.ImprintedCards.Should().BeEmpty();
    }

    [Fact]
    public void ASpellResolvingOntoTheBattlefield_KeepsWhatItsCastPaid()
    {
        var c = new Creature("Solitude", "{3}{W}{W}", 3, 2);
        c.SetOwner(_alice); c.SetController(_alice);
        c.SetZone(ZoneType.Stack); _alice.Zones.Stack.AddCard(c);
        c.EvokeWasPaid = true;

        _zones.MoveCard(c, ZoneType.Stack, ZoneType.Battlefield, _alice);

        c.EvokeWasPaid.Should().BeTrue("its evoke sacrifice trigger reads it (CR 702.74a)");
    }

    [Fact]
    public void EntersWithCounters_LandAfterTheReset()
    {
        var bear = OnBattlefield();
        bear.Counters.Add(CounterType.PlusOnePlusOne, 3);
        _zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Graveyard);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);

        // Undying's shape: return it, then put one +1/+1 counter on it.
        _zones.MoveCard(bear, ZoneType.Graveyard, ZoneType.Battlefield, _alice);
        bear.Counters.Add(CounterType.PlusOnePlusOne, 1);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void CountersOnACardInExile_StayUntilItEntersTheBattlefield()
    {
        // Suspend: exiled with time counters (CR 702.62a), which stay on it in exile.
        var bear = OnBattlefield();
        _zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Hand);
        _zones.MoveCard(bear, ZoneType.Hand, ZoneType.Exile);
        bear.Counters.Add(CounterType.Time, 3);
        bear.Counters.Count(CounterType.Time).Should().Be(3);

        _zones.MoveCard(bear, ZoneType.Exile, ZoneType.Stack);
        _zones.MoveCard(bear, ZoneType.Stack, ZoneType.Battlefield, _alice);

        bear.Counters.HasAny.Should().BeFalse();
    }
}
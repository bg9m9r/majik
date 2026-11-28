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

    private T Put<T>(T p) where T : Permanent
    {
        p.SetOwner(_alice); p.SetController(_alice);
        p.SetZone(ZoneType.Hand); _alice.Zones.Hand.AddCard(p);
        _zones.MoveCard(p, ZoneType.Hand, ZoneType.Battlefield, _alice);
        return p;
    }

    [Fact]
    public void AFlickeredCreature_ComesBackWithNothingAttached()
    {
        // CR 400.7 / 303.4d / 301.5c — the creature that returns is a new
        // object; the Aura and Equipment were attached to the old one, so
        // they are attached to nothing (and SBA 704.5m/n deal with them).
        var bear = OnBattlefield();
        var aura = Put(new Enchantment("Rancor", "{G}"));
        var sword = Put(new Artifact("Sword", "{2}"));
        aura.AttachTo(bear); sword.AttachTo(bear);

        _zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Exile);
        _zones.MoveCard(bear, ZoneType.Exile, ZoneType.Battlefield, _alice);

        bear.Attachments.Should().BeEmpty();
        aura.AttachedTo.Should().BeNull();
        sword.AttachedTo.Should().BeNull();
    }

    private void RunSbasTwice()
    {
        var sba = new Majik.Core.Rules.StateBasedActions(_bus, _zones);
        for (var i = 0; i < 2; i++)
            sba.CheckStateBasedActions(new[] { _alice }, _alice.Zones.Battlefield.GetCards().ToList());
    }

    [Fact]
    public void AFlickeredCreature_UnderPacifism_SendsPacifismToTheGraveyard()
    {
        // CR 704.5m — Ephemerate's exile-and-return within one resolution:
        // Pacifism was attached to the old object and is now attached to
        // nothing, so it is put into its owner's graveyard.
        var bear = OnBattlefield();
        var pacifism = Put(Majik.Core.CardData.Factories.PacifismFactory.Create(_alice));
        pacifism.AttachTo(bear);

        _zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Exile);
        _zones.MoveCard(bear, ZoneType.Exile, ZoneType.Battlefield, _alice);
        RunSbasTwice();

        pacifism.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(pacifism);
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void AFlickeredCreature_LeavesItsEquipmentOnTheBattlefield_Unattached()
    {
        // CR 704.5n / 301.5c — Equipment attached to nothing simply stays.
        var bear = OnBattlefield();
        var sword = Put(new Artifact("Sword", "{2}", subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Equipment }));
        sword.AttachTo(bear);

        _zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Exile);
        _zones.MoveCard(bear, ZoneType.Exile, ZoneType.Battlefield, _alice);
        RunSbasTwice();

        sword.Zone.Should().Be(ZoneType.Battlefield);
        sword.AttachedTo.Should().BeNull();
    }

    [Fact]
    public void AnAuraPutOntoTheBattlefieldByAnEffect_IsAttached_AndStays()
    {
        // CR 303.4f — Old-Growth Troll's return as an Aura: the effect that
        // puts it onto the battlefield attaches it, before any SBA check.
        var host = OnBattlefield("Forest Elemental");
        var troll = OnBattlefield("Old-Growth Troll");
        _zones.MoveCard(troll, ZoneType.Battlefield, ZoneType.Graveyard);

        var aura = Majik.Core.Effects.ReturnAsAuraOnDeathEffect.Apply(
            troll, "Old-Growth Troll", "{G}{G}{G}", p => ReferenceEquals(p, host),
            Array.Empty<Func<Permanent, Majik.Core.Abilities.IAbility>>(), null, _zones);
        RunSbasTwice();

        aura.Should().NotBeNull();
        aura!.Zone.Should().Be(ZoneType.Battlefield);
        aura.AttachedTo.Should().BeSameAs(host);
    }

    [Fact]
    public void ACurse_EnchantingAPlayer_IsNotPutIntoTheGraveyard()
    {
        // CR 303.4 / 704.5m — a Curse enchants a player, which the engine
        // tracks outside AttachedTo; it is attached to something.
        var curse = Put(Majik.Core.CardData.Factories.TrespassersCurseFactory.Create(_alice));
        Majik.Core.CardData.Factories.TrespassersCurseFactory.SetEnchantedPlayer(curse, new Player("Bob", 20));

        RunSbasTwice();

        curse.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void AFlickeredAura_ComesBackAttachedToNothing()
    {
        var bear = OnBattlefield();
        var aura = Put(new Enchantment("Rancor", "{G}"));
        aura.AttachTo(bear);

        _zones.MoveCard(aura, ZoneType.Battlefield, ZoneType.Exile);
        aura.AttachedTo.Should().BeSameAs(bear, "last known information still reads its host (CR 603.10)");
        _zones.MoveCard(aura, ZoneType.Exile, ZoneType.Battlefield, _alice);

        aura.AttachedTo.Should().BeNull("an Aura entering not cast is attached by the effect that returns it (CR 303.4f)");
        bear.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void AReturningPlaneswalker_HasItsPrintedLoyalty()
    {
        // CR 306.5b / 400.7 — it enters with loyalty counters equal to its
        // printed loyalty, not what the old object had.
        var jace = Put(new Planeswalker("Jace", "{2}{U}{U}", 3));
        jace.AddLoyalty(4);
        jace.LoyaltyAbilityActivatedThisTurn = true;

        _zones.MoveCard(jace, ZoneType.Battlefield, ZoneType.Exile);
        jace.Loyalty.Should().Be(7, "last known information");
        _zones.MoveCard(jace, ZoneType.Exile, ZoneType.Battlefield, _alice);

        jace.Loyalty.Should().Be(3);
        jace.LoyaltyAbilityActivatedThisTurn.Should().BeFalse();
    }

    [Fact]
    public void AReturningClass_IsLevelOne()
    {
        // CR 716.2a / 400.7 — a Class enters as a level-1 Class; the level the
        // old object reached does not come back with it.
        var talent = new Enchantment("Bandit's Talent", "{1}{B}");
        talent.AttachClassState(new Majik.Core.CardData.Classes.ClassState(4));
        Put(talent);
        talent.ClassState!.LevelUp().Should().BeTrue();
        talent.ClassState!.LevelUp().Should().BeTrue();

        _zones.MoveCard(talent, ZoneType.Battlefield, ZoneType.Hand);
        talent.ClassState!.CurrentLevel.Should().Be(3, "last known information");
        _zones.MoveCard(talent, ZoneType.Hand, ZoneType.Battlefield, _alice);

        talent.ClassState!.CurrentLevel.Should().Be(1);
    }
}
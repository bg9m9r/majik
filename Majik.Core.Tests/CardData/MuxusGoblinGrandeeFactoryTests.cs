using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MuxusGoblinGrandeeFactory"/> (Jumpstart,
/// {4}{R}{R}, Legendary Creature — Goblin Noble 4/4).
///
/// Oracle text:
///   "Haste.
///    When Muxus, Goblin Grandee enters, reveal the top six cards of your
///    library. Put all Goblin creature cards from among them onto the
///    battlefield and the rest on the bottom of your library in a random
///    order.
///    Other Goblins you control get +1/+1."
///
/// Covers:
///   - Identity (Legendary Creature — Goblin Noble, {4}{R}{R}, 4/4,
///     owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Printed Haste keyword marker (CR 702.10).
///   - Lord static — other controller's Goblins get +1/+1, includeSelf
///     false; Muxus is NOT self-pumped; opponent's Goblins are NOT
///     pumped; LTB lifts the bonus.
///   - ETB resolution: peeks top 6, publishes CardRevealedEvent per
///     peeked card, moves Goblin creature cards onto battlefield, bottoms
///     the rest randomly.
///   - Goblin creature gate — non-creature Goblin tribal cards (none
///     exist as printed, but the gate is defensive) stay bottomed; non-
///     Goblin creatures stay bottomed.
///   - Short library (&lt; 6) handled gracefully.
/// </summary>
public class MuxusGoblinGrandeeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeGoblin(Player owner, string name, ZoneType zone)
    {
        var c = new Creature(name, "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(zone);
        switch (zone)
        {
            case ZoneType.Library: owner.Zones.Library.AddCard(c); break;
            case ZoneType.Battlefield: owner.Zones.Battlefield.AddCard(c); break;
            case ZoneType.Hand: owner.Zones.Hand.AddCard(c); break;
        }
        return c;
    }

    private static Creature MakeNonGoblin(Player owner, string name, ZoneType zone)
    {
        var c = new Creature(name, "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(zone);
        if (zone == ZoneType.Library) owner.Zones.Library.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Muxus_Identity()
    {
        var muxus = MuxusGoblinGrandeeFactory.Create(_alice);

        muxus.Name.Should().Be("Muxus, Goblin Grandee");
        muxus.ManaCost.Should().Be("{4}{R}{R}");
        muxus.HasType(CardType.Creature).Should().BeTrue();
        muxus.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        muxus.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        muxus.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        muxus.BasePower.Should().Be(4);
        muxus.BaseToughness.Should().Be(4);
        muxus.Owner.Should().BeSameAs(_alice);
        muxus.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Muxus_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Muxus, Goblin Grandee", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Muxus, Goblin Grandee");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
    }

    [Fact]
    public void Muxus_HasPrintedHaste()
    {
        var muxus = MuxusGoblinGrandeeFactory.Create(_alice);
        muxus.Zone = ZoneType.Battlefield;

        muxus.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste");
        CombatAbilities.HasHaste(muxus).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Lord static
    // -----------------------------------------------------------------------

    [Fact]
    public void Muxus_BuffsOtherControllerGoblin_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherGoblin = MakeGoblin(_alice, "Mogg Fanatic", ZoneType.Battlefield);
        otherGoblin.ActiveEffects = svc;

        var muxus = MuxusGoblinGrandeeFactory.Create(_alice,
            continuousEffects: svc, zoneService: null, eventBus: null, triggers: null);
        muxus.Zone = ZoneType.Battlefield;
        muxus.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(2);
        otherGoblin.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Muxus_DoesNotSelfPump_PlusOnePlusOne()
    {
        var svc = new ContinuousEffectsService();

        var muxus = MuxusGoblinGrandeeFactory.Create(_alice,
            continuousEffects: svc, zoneService: null, eventBus: null, triggers: null);
        muxus.Zone = ZoneType.Battlefield;
        muxus.ActiveEffects = svc;

        muxus.GetPower().Should().Be(4, "includeSelf:false — Muxus doesn't pump itself.");
        muxus.GetToughness().Should().Be(4);
    }

    [Fact]
    public void Muxus_DoesNotPump_OpponentGoblin()
    {
        var svc = new ContinuousEffectsService();

        var oppGoblin = MakeGoblin(_bob, "Mogg Fanatic", ZoneType.Battlefield);
        oppGoblin.ActiveEffects = svc;

        var muxus = MuxusGoblinGrandeeFactory.Create(_alice,
            continuousEffects: svc, zoneService: null, eventBus: null, triggers: null);
        muxus.Zone = ZoneType.Battlefield;
        muxus.ActiveEffects = svc;

        oppGoblin.GetPower().Should().Be(1, "opponentsOnly:false — controller-scoped.");
        oppGoblin.GetToughness().Should().Be(1);
    }

    [Fact]
    public void Muxus_LTB_LiftsBonusFromOtherGoblin()
    {
        var svc = new ContinuousEffectsService();

        var otherGoblin = MakeGoblin(_alice, "Mogg Fanatic", ZoneType.Battlefield);
        otherGoblin.ActiveEffects = svc;

        var muxus = MuxusGoblinGrandeeFactory.Create(_alice,
            continuousEffects: svc, zoneService: null, eventBus: null, triggers: null);
        muxus.Zone = ZoneType.Battlefield;
        muxus.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(2);
        muxus.SetZone(ZoneType.Graveyard); // LTB
        otherGoblin.GetPower().Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Muxus_HasEtbTrigger_OnEnterBattlefield()
    {
        var muxus = MuxusGoblinGrandeeFactory.Create(_alice);
        var trig = muxus.Abilities.OfType<TriggeredAbility>().FirstOrDefault();
        trig.Should().NotBeNull("Muxus has an ETB trigger");
        trig!.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // ETB resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Muxus_ResolveEtb_RevealsTopSix_PutsGoblinsOnBattlefield_BottomsRest()
    {
        // Pin RNG for deterministic bottom order.
        GameRandomRegistry.SetDefault(new GameRandom(seed: 1));

        var bus = new EventBus();
        var zones = new ZoneService(bus);

        // Seed Alice's library: 3 Goblin creatures + 3 non-Goblin creatures
        // + 2 follower cards under the window so we can assert the bottoms
        // land beneath them.
        var goblin1 = MakeGoblin(_alice, "Mogg Fanatic", ZoneType.Library);
        var goblin2 = MakeGoblin(_alice, "Goblin Lackey", ZoneType.Library);
        var goblin3 = MakeGoblin(_alice, "Goblin Warchief", ZoneType.Library);
        var bear1 = MakeNonGoblin(_alice, "Grizzly Bears", ZoneType.Library);
        var bear2 = MakeNonGoblin(_alice, "Runeclaw Bear", ZoneType.Library);
        var bear3 = MakeNonGoblin(_alice, "River Bear", ZoneType.Library);
        // followers under the top-6 window
        var follower1 = MakeNonGoblin(_alice, "Filler A", ZoneType.Library);
        var follower2 = MakeNonGoblin(_alice, "Filler B", ZoneType.Library);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var result = MuxusGoblinGrandeeFactory.ResolveEtb(_alice, zones, bus);

        // Six reveals.
        revealed.Should().HaveCount(6);
        revealed.Select(r => r.Card).Should().BeEquivalentTo(
            new ICard[] { goblin1, goblin2, goblin3, bear1, bear2, bear3 });
        revealed.Should().OnlyContain(r => r.Reason == "muxus");

        // All three Goblins on battlefield.
        new[] { goblin1, goblin2, goblin3 }.Should().OnlyContain(g => g.Zone == ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(new ICard[] { goblin1, goblin2, goblin3 });

        // Three bears bottomed; follower1/follower2 still on top.
        new[] { bear1, bear2, bear3 }.Should().OnlyContain(b => b.Zone == ZoneType.Library);
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Take(2).Should().Equal(follower1, follower2);
        lib.Skip(2).Should().BeEquivalentTo(new ICard[] { bear1, bear2, bear3 });

        // Result record reflects the partition.
        result.Peeked.Should().HaveCount(6);
        result.ToBattlefield.Should().BeEquivalentTo(new ICard[] { goblin1, goblin2, goblin3 });
        result.ToBottom.Should().BeEquivalentTo(new ICard[] { bear1, bear2, bear3 });
    }

    [Fact]
    public void Muxus_ResolveEtb_ShortLibrary_RevealsAllAndStops()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        // Only 2 cards in library — both Goblins.
        var goblin1 = MakeGoblin(_alice, "Mogg Fanatic", ZoneType.Library);
        var goblin2 = MakeGoblin(_alice, "Goblin Lackey", ZoneType.Library);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var result = MuxusGoblinGrandeeFactory.ResolveEtb(_alice, zones, bus);

        revealed.Should().HaveCount(2);
        result.Peeked.Should().HaveCount(2);
        result.ToBattlefield.Should().HaveCount(2);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        new[] { goblin1, goblin2 }.Should().OnlyContain(g => g.Zone == ZoneType.Battlefield);
    }

    [Fact]
    public void Muxus_ResolveEtb_EmptyLibrary_NoOp()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var result = MuxusGoblinGrandeeFactory.ResolveEtb(_alice, zones, bus);

        revealed.Should().BeEmpty();
        result.Peeked.Should().BeEmpty();
        result.ToBattlefield.Should().BeEmpty();
        result.ToBottom.Should().BeEmpty();
    }

    [Fact]
    public void Muxus_ResolveEtb_NoZoneService_StillMovesViaRawZones()
    {
        // No zone service / event bus — raw path. Goblins should still
        // land on the battlefield.
        var goblin = MakeGoblin(_alice, "Mogg Fanatic", ZoneType.Library);
        var bear = MakeNonGoblin(_alice, "Grizzly Bears", ZoneType.Library);

        var result = MuxusGoblinGrandeeFactory.ResolveEtb(_alice);

        goblin.Zone.Should().Be(ZoneType.Battlefield);
        bear.Zone.Should().Be(ZoneType.Library);
        result.ToBattlefield.Should().ContainSingle().Which.Should().BeSameAs(goblin);
        result.ToBottom.Should().ContainSingle().Which.Should().BeSameAs(bear);
    }
}

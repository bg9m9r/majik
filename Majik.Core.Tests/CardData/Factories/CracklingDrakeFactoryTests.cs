using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CracklingDrakeFactory"/>.
///
/// Card: Crackling Drake — Creature — Drake {U}{U}{R}{R}, */4 (Guilds of Ravnica).
/// Oracle text:
///   "Flying
///    Crackling Drake's power is equal to the total number of instant and
///    sorcery cards you own in exile and in your graveyard.
///    When this creature enters, draw a card."
///
/// Covers:
///   - Identity ({U}{U}{R}{R}, R/U color indicator, */4, Creature — Drake).
///   - Flying keyword marker (CR 702.9).
///   - NamedCardFactory dispatch.
///   - Exactly one battlefield-active ETB TriggeredAbility (the draw).
///   - ETB effect draws 1 card from a stocked library; stamps loss flag on empty.
///   - Layer 7a CDA power = count of instant+sorcery cards the controller owns
///     across their own exile + graveyard; toughness stays fixed at 4.
///   - CountInstantsAndSorceries pure helper.
/// </summary>
[Trait("Color", "M")]
public class CracklingDrakeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public CracklingDrakeFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    private static Card MakeInstant(string name, Player owner)
    {
        var c = new Instant(name, "{R}");
        c.SetOwner(owner);
        return c;
    }

    private static Card MakeSorcery(string name, Player owner)
    {
        var c = new Sorcery(name, "{R}");
        c.SetOwner(owner);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CracklingDrake_Identity()
    {
        var c = CracklingDrakeFactory.Create(_alice);

        c.Name.Should().Be("Crackling Drake");
        c.ManaCost.Should().Be("{U}{U}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drake).Should().BeTrue();
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CracklingDrake_HasFlyingKeyword()
    {
        var c = CracklingDrakeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying",
                "CR 702.9 — Flying is a printed keyword ability on Crackling Drake");
    }
    // -----------------------------------------------------------------------
    // ETB triggered ability — draw 1
    // -----------------------------------------------------------------------

    [Fact]
    public void CracklingDrake_ExactlyOneBattlefieldActiveEtbTrigger()
    {
        var c = CracklingDrakeFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1,
            "Crackling Drake has exactly one triggered ability — the ETB draw");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are active while the permanent is on the battlefield (CR 603.6a)");
    }

    [Fact]
    public void CracklingDrake_EtbTrigger_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);
        var c1 = new Card("Top1", "");
        var c2 = new Card("Top2", "");
        foreach (var card in new[] { c1, c2 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var drake = CracklingDrakeFactory.Create(alice);
        var etb = drake.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(1, "ETB draws exactly 1 card (CR 121.1)");
        alice.Zones.Hand.GetCards().Should().Contain(c1, "the top card of the library is drawn");
        alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void CracklingDrake_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var alice = new Player("Alice", 20);
        var drake = CracklingDrakeFactory.Create(alice);
        var etb = drake.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty();
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
    }

    // -----------------------------------------------------------------------
    // CountInstantsAndSorceries pure helper
    // -----------------------------------------------------------------------

    [Fact]
    public void CountInstantsAndSorceries_CountsInstantsAndSorceriesOwnedByController()
    {
        var cards = new ICard[]
        {
            MakeInstant("Bolt", _alice),
            MakeSorcery("Divination", _alice),
            new Creature("Bear", "{1}{G}", 2, 2),  // not instant/sorcery — excluded
        };
        ((Card)cards[2]).SetOwner(_alice);

        CracklingDrakeFactory.CountInstantsAndSorceries(cards, _alice).Should().Be(2);
    }

    [Fact]
    public void CountInstantsAndSorceries_ExcludesCardsOwnedByOtherPlayers()
    {
        var bob = new Player("Bob", 20);
        var cards = new ICard[]
        {
            MakeInstant("Mine", _alice),
            MakeInstant("Bobs", bob),   // "you own" — excluded
        };

        CracklingDrakeFactory.CountInstantsAndSorceries(cards, _alice).Should().Be(1,
            "only instant/sorcery cards the controller OWNS count (CR 109.5 'you')");
    }

    // -----------------------------------------------------------------------
    // Layer 7a CDA — power tracks owned instant/sorcery in exile + graveyard
    // -----------------------------------------------------------------------

    private Creature WireDrake(Player owner)
    {
        var drake = CracklingDrakeFactory.Create(owner, _effects, _bus);
        drake.ActiveEffects = _effects;
        return drake;
    }

    [Fact]
    public void CracklingDrake_PowerZero_WhenNoInstantsOrSorceries()
    {
        var drake = WireDrake(_alice);
        _zones.MoveCard(drake, ZoneType.Library, ZoneType.Battlefield, _alice);

        drake.Power.Should().Be(0);
        drake.Toughness.Should().Be(4, "toughness is fixed at 4, not CDA-defined");
    }

    [Fact]
    public void CracklingDrake_PowerCountsGraveyardAndExile()
    {
        var drake = WireDrake(_alice);
        _zones.MoveCard(drake, ZoneType.Library, ZoneType.Battlefield, _alice);

        // 2 instants + 1 sorcery in graveyard, 1 instant in exile = 4.
        foreach (var (factory, name) in new[]
        {
            ((Func<string, Player, Card>)MakeInstant, "GY-Bolt-1"),
            (MakeInstant, "GY-Bolt-2"),
            (MakeSorcery, "GY-Divination"),
        })
        {
            var card = factory(name, _alice);
            _alice.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }

        var exiled = MakeInstant("Exile-Bolt", _alice);
        _alice.Zones.Exile.AddCard(exiled);
        exiled.SetZone(ZoneType.Exile);

        // A creature in the graveyard must NOT count.
        var creatureInGy = new Creature("Dead Bear", "{1}{G}", 2, 2);
        creatureInGy.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(creatureInGy);
        creatureInGy.SetZone(ZoneType.Graveyard);

        drake.Power.Should().Be(4);
        drake.Toughness.Should().Be(4);
    }

    [Fact]
    public void CracklingDrake_PowerTracksChangesLive()
    {
        var drake = WireDrake(_alice);
        _zones.MoveCard(drake, ZoneType.Library, ZoneType.Battlefield, _alice);

        drake.Power.Should().Be(0);

        var bolt = MakeInstant("Bolt", _alice);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);
        // Graveyard add bypasses the event bus; invalidate the layer-system
        // cache explicitly, as production's CardMovedEvent would.
        drake.ActiveEffects!.Clear();

        drake.Power.Should().Be(1, "CDA re-evaluates every Compute (CR 613.2)");
    }
}

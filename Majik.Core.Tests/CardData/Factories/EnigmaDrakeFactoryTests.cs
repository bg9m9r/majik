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
/// Tests for <see cref="EnigmaDrakeFactory"/>.
///
/// Card: Enigma Drake — Creature — Drake {1}{U}{R}, */4 (Dragons of Tarkir).
/// Oracle text:
///   "Flying
///    Enigma Drake's power is equal to the number of instant and sorcery
///    cards in your graveyard."
///
/// Covers:
///   - Identity ({1}{U}{R}, */4, Creature — Drake).
///   - Flying keyword marker (CR 702.9).
///   - No triggered abilities (Enigma Drake — unlike Crackling Drake — has no ETB).
///   - Layer 7a CDA power = count of instant+sorcery cards the controller owns
///     in their own graveyard ONLY (no exile); toughness stays fixed at 4.
///   - CountInstantsAndSorceries pure helper.
/// </summary>
[Trait("Color", "M")]
public class EnigmaDrakeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public EnigmaDrakeFactoryTests()
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
    public void EnigmaDrake_Identity()
    {
        var c = EnigmaDrakeFactory.Create(_alice);

        c.Name.Should().Be("Enigma Drake");
        c.ManaCost.Should().Be("{1}{U}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drake).Should().BeTrue();
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EnigmaDrake_HasFlyingKeyword()
    {
        var c = EnigmaDrakeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying",
                "CR 702.9 — Flying is a printed keyword ability on Enigma Drake");
    }

    [Fact]
    public void EnigmaDrake_HasNoTriggeredAbilities()
    {
        var c = EnigmaDrakeFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Enigma Drake has no triggered abilities (no ETB draw, unlike Crackling Drake)");
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

        EnigmaDrakeFactory.CountInstantsAndSorceries(cards, _alice).Should().Be(2);
    }

    [Fact]
    public void CountInstantsAndSorceries_ExcludesCardsOwnedByOtherPlayers()
    {
        var bob = new Player("Bob", 20);
        var cards = new ICard[]
        {
            MakeInstant("Mine", _alice),
            MakeInstant("Bobs", bob),   // "your graveyard" — excluded
        };

        EnigmaDrakeFactory.CountInstantsAndSorceries(cards, _alice).Should().Be(1,
            "only instant/sorcery cards the controller OWNS count (CR 109.5 'you')");
    }

    // -----------------------------------------------------------------------
    // Layer 7a CDA — power tracks owned instant/sorcery in graveyard ONLY
    // -----------------------------------------------------------------------

    private Creature WireDrake(Player owner)
    {
        var drake = EnigmaDrakeFactory.Create(owner, _effects, _bus);
        drake.ActiveEffects = _effects;
        return drake;
    }

    [Fact]
    public void EnigmaDrake_PowerZero_WhenNoInstantsOrSorceries()
    {
        var drake = WireDrake(_alice);
        _zones.MoveCard(drake, ZoneType.Library, ZoneType.Battlefield, _alice);

        drake.Power.Should().Be(0);
        drake.Toughness.Should().Be(4, "toughness is fixed at 4, not CDA-defined");
    }

    [Fact]
    public void EnigmaDrake_PowerCountsGraveyardOnly()
    {
        var drake = WireDrake(_alice);
        _zones.MoveCard(drake, ZoneType.Library, ZoneType.Battlefield, _alice);

        // 2 instants + 1 sorcery in graveyard = 3.
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

        // An instant in EXILE must NOT count (graveyard only, unlike Crackling Drake).
        var exiled = MakeInstant("Exile-Bolt", _alice);
        _alice.Zones.Exile.AddCard(exiled);
        exiled.SetZone(ZoneType.Exile);

        // A creature in the graveyard must NOT count.
        var creatureInGy = new Creature("Dead Bear", "{1}{G}", 2, 2);
        creatureInGy.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(creatureInGy);
        creatureInGy.SetZone(ZoneType.Graveyard);

        drake.Power.Should().Be(3, "only graveyard instants/sorceries count, not exile");
        drake.Toughness.Should().Be(4);
    }

    [Fact]
    public void EnigmaDrake_PowerTracksChangesLive()
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

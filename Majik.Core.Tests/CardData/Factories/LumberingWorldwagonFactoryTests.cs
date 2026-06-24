using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LumberingWorldwagonFactory"/> — Artifact — Vehicle
/// {2}{G} */4 (Edge of Eternities). Oracle:
///   "This Vehicle's power is equal to the number of lands you control.
///    Whenever this Vehicle enters or attacks, you may search your library for
///    a basic land card, put it onto the battlefield tapped, then shuffle.
///    Crew 4"
///
/// Covers the card's UNIQUE behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   - One *_Identity assert: Artifact + Creature + Vehicle, {2}{G}, printed
///     toughness 4, Crew 4, power seeded 0 (the CDA's "*").
///   - Layer 7a CDA: power = lands you control; toughness stays the fixed 4.
///   - Ability shape: exactly two TriggeredAbility (enter + attack), no
///     activated / mana abilities.
///   - Enter trigger resolve: tutors ONE basic land to battlefield tapped.
///   - Attack trigger resolve: same tutor body.
///   - Pure helper CountLands.
/// </summary>
[Trait("Color", "G")]
public class LumberingWorldwagonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public LumberingWorldwagonFactoryTests()
    {
        _zones = new ZoneService(_bus);
        ZoneServiceRegistry.Set(_alice, _zones);
    }

    private Func<IEnumerable<ICard>> AliceBattlefield =>
        () => _alice.Zones.Battlefield.GetCards();

    private Creature WireWagon()
    {
        var wagon = LumberingWorldwagonFactory.Create(
            _alice, _effects, _bus, AliceBattlefield, triggers: null);
        wagon.ActiveEffects = _effects;
        return wagon;
    }

    private static Land MakeBasic(string name, CardSubtype subtype)
    {
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Worldwagon_IsArtifactVehicle_AtCost2G_Toughness4_Crew4()
    {
        var wagon = LumberingWorldwagonFactory.Create(_alice);

        wagon.Name.Should().Be("Lumbering Worldwagon");
        wagon.ManaCost.Should().Be("{2}{G}");
        wagon.HasType(CardType.Artifact).Should().BeTrue(
            "Lumbering Worldwagon is an Artifact (Vehicle) — CR 301.1 / 302.1");
        wagon.HasType(CardType.Creature).Should().BeTrue(
            "vehicles are modelled as Creature shells so crew can ship base P/T");
        wagon.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
        // Printed power is "*" (CDA) — seeded 0 per CR 208.2c; toughness 4.
        wagon.BasePower.Should().Be(0);
        wagon.BaseToughness.Should().Be(4);
        LumberingWorldwagonFactory.CrewCost.Should().Be(4, "Crew 4 — CR 702.122");
    }

    // -----------------------------------------------------------------------
    // Layer 7a CDA — power = lands you control; toughness fixed at 4
    // -----------------------------------------------------------------------

    [Fact]
    public void Power_EqualsLandsYouControl_ToughnessStaysFour()
    {
        var wagon = WireWagon();
        _zones.MoveCard(wagon, ZoneType.Library, ZoneType.Battlefield, _alice);

        // No lands yet: power 0, toughness 4.
        wagon.Power.Should().Be(0);
        wagon.Toughness.Should().Be(4);

        foreach (var (name, subtype) in new[]
                 {
                     ("Forest", CardSubtype.Forest),
                     ("Mountain", CardSubtype.Mountain),
                     ("Island", CardSubtype.Island),
                 })
        {
            var land = MakeBasic(name, subtype);
            land.SetOwner(_alice);
            land.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        // A non-land permanent must NOT count.
        var bear = new Card("Grizzly Bears", "{1}{G}", new[] { CardType.Creature });
        bear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bear);

        // Lands were added directly to the zone (no CardMovedEvent), so the
        // P/T compute cache is stale from the no-lands read above; invalidate
        // it so the CDA re-reads the now-3-land battlefield.
        _effects.BumpGeneration();

        wagon.Power.Should().Be(3, "power = number of lands you control (CR 604.3)");
        wagon.Toughness.Should().Be(4, "toughness is the fixed printed 4, not a CDA");
    }

    [Fact]
    public void CountLands_PureHelper_CountsOnlyLands()
    {
        var forest = MakeBasic("Forest", CardSubtype.Forest);
        var island = MakeBasic("Island", CardSubtype.Island);
        var bolt = new Card("Lightning Bolt", "{R}", new[] { CardType.Instant });

        LumberingWorldwagonFactory.CountLands(new ICard[] { forest, bolt, island })
            .Should().Be(2);
        LumberingWorldwagonFactory.CountLands(Array.Empty<ICard>())
            .Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Ability shape — exactly an enter trigger + an attack trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void HasTwoTriggers_NoActivatedOrManaAbilities()
    {
        var wagon = LumberingWorldwagonFactory.Create(_alice);

        wagon.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        wagon.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        wagon.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one enter trigger + one attack trigger — both run the tutor body");
    }

    // -----------------------------------------------------------------------
    // Enter / attack tutor — search a basic land to battlefield tapped
    // -----------------------------------------------------------------------

    [Fact]
    public void EnterTrigger_Tutors_OneBasicToBattlefieldTapped()
    {
        SeedTwoBasics();

        var wagon = LumberingWorldwagonFactory.Create(_alice);
        // First trigger registered is the enter trigger.
        var triggers = wagon.Abilities.OfType<TriggeredAbility>().ToList();
        triggers[0].Resolve();

        AssertExactlyOneBasicTutoredTapped();
    }

    [Fact]
    public void AttackTrigger_Tutors_OneBasicToBattlefieldTapped()
    {
        SeedTwoBasics();

        var wagon = LumberingWorldwagonFactory.Create(_alice);
        // Second trigger registered is the attack trigger.
        var triggers = wagon.Abilities.OfType<TriggeredAbility>().ToList();
        triggers[1].Resolve();

        AssertExactlyOneBasicTutoredTapped();
    }

    [Fact]
    public void EnterTrigger_NoBasicsInLibrary_MovesNoLand()
    {
        var bog = new Land("Bojuka Bog"); // nonbasic
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var wagon = LumberingWorldwagonFactory.Create(_alice);
        wagon.Abilities.OfType<TriggeredAbility>().First().Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }

    private void SeedTwoBasics()
    {
        foreach (var (name, subtype) in new[]
                 {
                     ("Forest", CardSubtype.Forest),
                     ("Island", CardSubtype.Island),
                 })
        {
            var land = MakeBasic(name, subtype);
            land.SetOwner(_alice);
            _alice.Zones.Library.AddCard(land);
            land.SetZone(ZoneType.Library);
        }
    }

    private void AssertExactlyOneBasicTutoredTapped()
    {
        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Count(c => c is Land).Should().Be(1,
            "the tutor searches for A (one) basic land");
        var moved = battlefield.OfType<Land>().Single();
        moved.IsTapped.Should().BeTrue("the basic enters tapped (CR 701.18)");
        moved.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Library.GetCards().Should().Contain(c => c is Land,
            "only one of the two basics is taken");
    }
}

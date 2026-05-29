using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Nishoba Brawler — Creature — Cat Warrior {1}{G},
/// printed P/T */3:
///   "Trample
///    Domain — Nishoba Brawler's power is equal to the number of basic
///    land types among lands you control."
///
/// CR 604.3 / 613.2 — Layer 7a characteristic-defining power; toughness
/// is printed 3. CR 702.16 — Domain count. CR 702.19 — Trample.
///
/// Validates:
///   * Card identity + Trample keyword + NamedCardFactory dispatch.
///   * Layer 7a power = distinct basic land types you control; toughness
///     stays 3.
///   * A single dual land contributes both basic types; duplicates
///     collapse (distinct count).
///   * Layer 7c +1/+1 counter stacks on top of the CDA power.
/// </summary>
public class NishobaBrawlerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public NishobaBrawlerTests()
    {
        _zones = new ZoneService(_bus);
    }

    private Creature WireBrawler(Player owner)
    {
        var card = NishobaBrawlerFactory.Create(owner, _effects, _bus);
        card.ActiveEffects = _effects;
        return card;
    }

    private void PutBasicOnBattlefield(Player controller, CardSubtype basic)
    {
        var land = new Land(
            basic.ToString(),
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { basic });
        land.SetOwner(controller);
        land.SetController(controller);
        land.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(land);
        _zones.MoveCard(land, ZoneType.Library, ZoneType.Battlefield, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NishobaBrawler_IsCatWarrior_AtCost1G_WithTrample()
    {
        var brawler = NishobaBrawlerFactory.Create(_alice);

        brawler.Name.Should().Be("Nishoba Brawler");
        brawler.HasType(CardType.Creature).Should().BeTrue();
        brawler.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        brawler.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        brawler.ManaCost.Should().Be("{1}{G}");
        brawler.BaseToughness.Should().Be(3);
        CombatAbilities.HasTrample(brawler).Should().BeTrue();
        brawler.Owner.Should().BeSameAs(_alice);
        brawler.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_NishobaBrawler()
    {
        var card = NamedCardFactory.Create("Nishoba Brawler", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Nishoba Brawler");
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        CombatAbilities.HasTrample((Creature)card).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Layer 7a — Domain power CDA, printed toughness 3
    // -----------------------------------------------------------------------

    [Fact]
    public void NishobaBrawler_NoLands_PowerIs0_ToughnessIs3()
    {
        var brawler = WireBrawler(_alice);
        _zones.MoveCard(brawler, ZoneType.Library, ZoneType.Battlefield, _alice);

        brawler.Power.Should().Be(0);
        brawler.Toughness.Should().Be(3);
    }

    [Fact]
    public void NishobaBrawler_OneBasic_PowerIs1()
    {
        var brawler = WireBrawler(_alice);
        _zones.MoveCard(brawler, ZoneType.Library, ZoneType.Battlefield, _alice);

        PutBasicOnBattlefield(_alice, CardSubtype.Forest);

        brawler.Power.Should().Be(1);
        brawler.Toughness.Should().Be(3);
    }

    [Fact]
    public void NishobaBrawler_FiveDistinctBasics_PowerIs5()
    {
        var brawler = WireBrawler(_alice);
        _zones.MoveCard(brawler, ZoneType.Library, ZoneType.Battlefield, _alice);

        PutBasicOnBattlefield(_alice, CardSubtype.Plains);
        PutBasicOnBattlefield(_alice, CardSubtype.Island);
        PutBasicOnBattlefield(_alice, CardSubtype.Swamp);
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);
        PutBasicOnBattlefield(_alice, CardSubtype.Forest);

        brawler.Power.Should().Be(5);
        brawler.Toughness.Should().Be(3);
    }

    [Fact]
    public void NishobaBrawler_DuplicateBasics_CountDistinctOnly()
    {
        var brawler = WireBrawler(_alice);
        _zones.MoveCard(brawler, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Two Forests + one Mountain → 2 distinct basic land types.
        PutBasicOnBattlefield(_alice, CardSubtype.Forest);
        PutBasicOnBattlefield(_alice, CardSubtype.Forest);
        PutBasicOnBattlefield(_alice, CardSubtype.Mountain);

        brawler.Power.Should().Be(2);
    }

    [Fact]
    public void NishobaBrawler_DualLand_CountsBothBasicTypes()
    {
        var brawler = WireBrawler(_alice);
        _zones.MoveCard(brawler, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Stomping Ground — one nonbasic with Mountain + Forest subtypes
        // contributes both basic land types (CR 702.16) → power 2.
        var dual = new Land(
            "Stomping Ground",
            supertypes: null,
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest });
        dual.SetOwner(_alice);
        dual.SetController(_alice);
        dual.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(dual);
        _zones.MoveCard(dual, ZoneType.Library, ZoneType.Battlefield, _alice);

        brawler.Power.Should().Be(2);
    }

    [Fact]
    public void NishobaBrawler_PlusOneCounter_StacksOnTopOfCda()
    {
        var brawler = WireBrawler(_alice);
        _zones.MoveCard(brawler, ZoneType.Library, ZoneType.Battlefield, _alice);

        // One basic → CDA power 1, toughness 3.
        PutBasicOnBattlefield(_alice, CardSubtype.Forest);

        // +1/+1 counter (CR 613.7 postlude) stacks on top of 7a.
        brawler.Counters.Add(CounterType.PlusOnePlusOne);

        brawler.Power.Should().Be(2);
        brawler.Toughness.Should().Be(4);
    }
}

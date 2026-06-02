using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Grumgully, the Generous (Modern Horizons 2 — Legendary Creature
/// — Goblin Shaman {1}{R}{G} 3/3).
///
/// Oracle (verified against Scryfall):
///   "Each other non-Human creature you control enters with an additional
///    +1/+1 counter on it."
///
/// Coverage:
///   * Identity: Legendary Creature — Goblin Shaman, {1}{R}{G}, 3/3.
///   * NamedCardFactory dispatch.
///   * Unwired single-arg path: no replacement registered.
///   * Another non-Human creature you control enters with a +1/+1 counter.
///   * A Human creature you control is unaffected (the "non-Human" filter).
///   * An opponent's non-Human creature is unaffected ("creature YOU
///     control", CR 109.5).
///   * Grumgully itself does not get a counter ("each OTHER creature").
///   * Grumgully leaving the battlefield lifts the replacement.
/// </summary>
[Trait("Color", "RG")]
public class GrumgullyTheGenerousFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;

    public GrumgullyTheGenerousFactoryTests()
    {
        _zones = new ZoneService(_bus, _replacements);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Grumgully_IsLegendaryGoblinShaman_3_3_AtCost1RG()
    {
        var c = GrumgullyTheGenerousFactory.Create(_alice);

        c.Name.Should().Be("Grumgully, the Generous");
        c.ManaCost.Should().Be("{1}{R}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Grumgully_DispatchesThroughNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Grumgully, the Generous", _alice);

        c.Should().NotBeNull();
        c!.Name.Should().Be("Grumgully, the Generous");
    }

    [Fact]
    public void Grumgully_SingleArgPath_NoReplacementWired()
    {
        // The single-arg path registers no replacement, so an entering
        // non-Human creature is unaffected even though Grumgully is "out".
        var grumgully = GrumgullyTheGenerousFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(grumgully);
        grumgully.SetZone(ZoneType.Battlefield);

        var goblin = MakeCreature("Goblin Guide", _alice, CardSubtype.Goblin);
        _alice.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblin, ZoneType.Hand, ZoneType.Battlefield, _alice);

        goblin.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the single-arg path wires no ReplacementBus");
    }

    // -----------------------------------------------------------------------
    // "Each other non-Human creature you control enters with a +1/+1 counter"
    // (CR 614.1d)
    // -----------------------------------------------------------------------

    private static Creature MakeCreature(string name, Player owner, params CardSubtype[] subtypes)
    {
        var c = new Creature(name, "{R}", 2, 2, subtypes: subtypes);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private Creature GrumgullyOnBattlefield()
    {
        var grumgully = GrumgullyTheGenerousFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(grumgully);
        grumgully.SetZone(ZoneType.Library);
        _zones.MoveCard(grumgully, ZoneType.Library, ZoneType.Battlefield, _alice);
        return grumgully;
    }

    [Fact]
    public void OtherNonHumanCreatureYouControl_EntersWithCounter()
    {
        GrumgullyOnBattlefield();

        var goblin = MakeCreature("Goblin Guide", _alice, CardSubtype.Goblin);
        _alice.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        _zones.MoveCard(goblin, ZoneType.Hand, ZoneType.Battlefield, _alice);

        goblin.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 614.1d — another non-Human creature you control enters with an additional +1/+1 counter");
    }

    [Fact]
    public void HumanCreatureYouControl_EntersWithoutCounter()
    {
        GrumgullyOnBattlefield();

        var human = MakeCreature("Champion of the Parish", _alice, CardSubtype.Human, CardSubtype.Soldier);
        _alice.Zones.Hand.AddCard(human);
        human.SetZone(ZoneType.Hand);

        _zones.MoveCard(human, ZoneType.Hand, ZoneType.Battlefield, _alice);

        human.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "a Human creature is excluded by the printed 'non-Human' filter");
    }

    [Fact]
    public void OpponentNonHumanCreature_EntersWithoutCounter()
    {
        GrumgullyOnBattlefield();

        var oppGoblin = MakeCreature("Goblin Guide", _bob, CardSubtype.Goblin);
        _bob.Zones.Hand.AddCard(oppGoblin);
        oppGoblin.SetZone(ZoneType.Hand);

        _zones.MoveCard(oppGoblin, ZoneType.Hand, ZoneType.Battlefield, _bob);

        oppGoblin.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "'creature YOU control' is controller-scoped (CR 109.5)");
    }

    [Fact]
    public void Grumgully_DoesNotGiveItselfACounter()
    {
        // Route Grumgully's OWN entry through ZoneService while a SECOND
        // Grumgully's replacement is live — "each OTHER creature" excludes the
        // entering permanent only when it IS the source. Here we verify the
        // first Grumgully (the source) doesn't counter itself on its own entry.
        var grumgully = GrumgullyTheGenerousFactory.Create(_alice, _replacements, _bus);
        _alice.Zones.Library.AddCard(grumgully);
        grumgully.SetZone(ZoneType.Library);

        _zones.MoveCard(grumgully, ZoneType.Library, ZoneType.Battlefield, _alice);

        grumgully.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Grumgully does not give ITSELF a counter ('each OTHER creature', CR 109.5)");
    }

    [Fact]
    public void GrumgullyLeavesBattlefield_ReplacementLifts()
    {
        var grumgully = GrumgullyOnBattlefield();

        // Fires while Grumgully is out.
        var goblinBefore = MakeCreature("Goblin Guide", _alice, CardSubtype.Goblin);
        _alice.Zones.Hand.AddCard(goblinBefore);
        goblinBefore.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblinBefore, ZoneType.Hand, ZoneType.Battlefield, _alice);
        goblinBefore.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // Grumgully dies.
        _zones.MoveCard(grumgully, ZoneType.Battlefield, ZoneType.Graveyard);

        // A fresh Goblin now enters without a counter.
        var goblinAfter = MakeCreature("Goblin Guide", _alice, CardSubtype.Goblin);
        _alice.Zones.Hand.AddCard(goblinAfter);
        goblinAfter.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblinAfter, ZoneType.Hand, ZoneType.Battlefield, _alice);

        goblinAfter.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the replacement must lift when Grumgully leaves the battlefield");
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SunbondFactory"/>.
///
/// Card: Sunbond — Enchantment — Aura {3}{W} (Magic 2015).
///   "Enchant creature"
///   "Enchanted creature has \"Whenever you gain life, put that many
///    +1/+1 counters on this creature.\""
///
/// Covers:
///   - Identity / dispatch (Enchantment — Aura, {3}{W}, white).
///   - "Enchant creature" cast-time target predicate filters non-creatures.
///   - Granted lifegain trigger (CR 603.1 — the aura grants the ability to the
///     enchanted creature): on the controller gaining N life, N +1/+1 counters
///     go on the enchanted creature ("this creature"), with "that many" read
///     from the LifeChangedEvent delta (CR 603.7 snapshot).
///   - "you" = the enchanted creature's controller (CR 603.3c): the trigger
///     condition matches the controller's gain, not an opponent's.
///   - Inert while unattached.
///
/// Trigger resolution mirrors <c>VitoThornOfTheDuskRoseTests</c>: fire a
/// <see cref="LifeChangedEvent"/> on the bus to stamp the "that many" amount
/// slot, then execute the trigger's effects directly.
/// </summary>
public class SunbondTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Sunbond_Identity()
    {
        var c = SunbondFactory.Create(_alice);

        c.Name.Should().Be("Sunbond");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Sunbond()
    {
        var card = NamedCardFactory.Create("Sunbond", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Sunbond");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // "Enchant creature" target predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersToCreatures()
    {
        var aura = SunbondFactory.Create(_alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);
        var land = new Land("Plains");
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });

        var battlefield = new Permanent[] { bear, land, pacifism };
        var def = SunbondFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(pacifism);
    }

    // -----------------------------------------------------------------------
    // Granted lifegain trigger condition — "you" = enchanted creature's
    // controller (CR 603.3c) + strictly-positive delta (CR 119.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void LifegainTrigger_MatchesControllerGain_NotOpponentOrLoss()
    {
        var aura = SunbondFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);
        var bear = NewCreatureOnBattlefield("Bear", _alice);
        aura.AttachTo(bear);

        var trigger = aura.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 23), trigger)
            .Should().BeTrue("the enchanted creature's controller gained life");
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 23), trigger)
            .Should().BeFalse("'you' = the controller, not an opponent (CR 603.3c)");
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger)
            .Should().BeFalse("life loss is not a gain (CR 119.3)");
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 20), trigger)
            .Should().BeFalse("no net change is not a gain");
    }

    // -----------------------------------------------------------------------
    // "that many" +1/+1 counters on the enchanted creature ("this creature")
    // -----------------------------------------------------------------------

    [Fact]
    public void LifeGain_PutsThatManyCounters_OnEnchantedCreature()
    {
        var bus = new EventBus();
        var aura = SunbondFactory.Create(_alice, bus);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);
        aura.AttachTo(bear);

        ResolveGain(bus, aura, _alice, +3);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "controller gained 3 life → 'that many' = 3 counters on this creature");
    }

    [Fact]
    public void LifeGain_AccumulatesAcrossMultipleGains()
    {
        var bus = new EventBus();
        var aura = SunbondFactory.Create(_alice, bus);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);
        aura.AttachTo(bear);

        ResolveGain(bus, aura, _alice, +2);
        ResolveGain(bus, aura, _alice, +5);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(7);
    }

    [Fact]
    public void LifeLoss_PutsNoCounters()
    {
        var bus = new EventBus();
        var aura = SunbondFactory.Create(_alice, bus);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);
        aura.AttachTo(bear);

        ResolveGain(bus, aura, _alice, -4); // life LOSS — not a gain

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void LifeGain_ByNonControllingPlayer_PutsNoCounters()
    {
        // "you" = the enchanted creature's controller (CR 603.3c). Bob gaining
        // life must not stamp the amount slot, so no counters land.
        var bus = new EventBus();
        var aura = SunbondFactory.Create(_alice, bus);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);
        aura.AttachTo(bear);

        ResolveGain(bus, aura, _bob, +3);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void Inert_WhileUnattached()
    {
        var bus = new EventBus();
        var aura = SunbondFactory.Create(_alice, bus);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear", _alice);
        // Don't attach — the granted ability is on no creature.

        ResolveGain(bus, aura, _alice, +3);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fire a <see cref="LifeChangedEvent"/> on the bus (stamps the "that many"
    /// amount slot), then execute the granted trigger's effects directly —
    /// mirroring the resolution shape used by VitoThornOfTheDuskRoseTests.
    /// </summary>
    private static void ResolveGain(EventBus bus, Enchantment aura, Player player, int delta)
    {
        var prev = player.LifeTotal;
        bus.Publish(new LifeChangedEvent(player, prev, prev + delta));

        var trigger = aura.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();
    }

    private Creature NewCreatureOnBattlefield(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}

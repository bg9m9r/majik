using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KeldonMaraudersFactory"/>.
///
/// Keldon Marauders (Time Spiral + Modern Horizons reprint, {1}{R}):
///   Creature — Human Warrior 3/1.
///   "Vanishing 2 (This creature enters with two time counters on it. At
///    the beginning of your upkeep, remove a time counter from it. When the
///    last is removed, sacrifice it.)
///    When this creature enters or leaves the battlefield, it deals 1
///    damage to target player or planeswalker."
///
/// Covers:
///   - Identity (Human Warrior 3/1, {1}{R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Vanishing 2 — enters with two time counters.
///   - Upkeep tick removes a time counter; no sacrifice while counters
///     remain.
///   - Last upkeep tick removes the final counter and sacrifices the
///     creature (CR 702.63d).
///   - Two damage triggers (enters + leaves), each a 1..1
///     "target player or planeswalker" request.
///   - Damage resolution: 1 to a player; 1 to a planeswalker routes through
///     loyalty removal (CR 306.8); a creature target no-ops (CR 608.2b).
/// </summary>
[Trait("Color", "R")]
public class KeldonMaraudersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KeldonMarauders_Identity()
    {
        var km = KeldonMaraudersFactory.Create(_alice);

        km.Name.Should().Be("Keldon Marauders");
        km.ManaCost.Should().Be("{1}{R}");
        km.HasType(CardType.Creature).Should().BeTrue();
        km.HasSubtype(CardSubtype.Human).Should().BeTrue();
        km.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        km.BasePower.Should().Be(3);
        km.BaseToughness.Should().Be(1);
        km.Owner.Should().BeSameAs(_alice);
        km.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KeldonMarauders_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Keldon Marauders", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Keldon Marauders");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Vanishing 2 — time counters + upkeep loop
    // -----------------------------------------------------------------------

    [Fact]
    public void KeldonMarauders_EntersWithTwoTimeCounters()
    {
        var km = KeldonMaraudersFactory.Create(_alice);

        km.Counters.Count(CounterType.Time).Should().Be(2,
            "Vanishing 2 — enters with two time counters (CR 702.63b)");
    }

    [Fact]
    public void UpkeepTick_RemovesOneTimeCounter_NoSacrificeWhileCountersRemain()
    {
        var km = KeldonMaraudersFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        KeldonMaraudersFactory.PerformUpkeepTick(km, _alice, zones: null);

        km.Counters.Count(CounterType.Time).Should().Be(1,
            "one time counter removed at upkeep (CR 702.63c)");
        km.Zone.Should().Be(ZoneType.Battlefield,
            "still has a time counter — not sacrificed yet");
        _alice.Zones.Battlefield.GetCards().Should().Contain(km);
    }

    [Fact]
    public void UpkeepTick_RemovingLastCounter_SacrificesCreature()
    {
        var km = KeldonMaraudersFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        // First tick: 2 → 1, no sacrifice.
        KeldonMaraudersFactory.PerformUpkeepTick(km, _alice, zones: null);
        // Second tick: 1 → 0, sacrifice (CR 702.63d).
        KeldonMaraudersFactory.PerformUpkeepTick(km, _alice, zones: null);

        km.Counters.Count(CounterType.Time).Should().Be(0);
        km.Zone.Should().Be(ZoneType.Graveyard,
            "last time counter removed — sacrifice it (CR 702.63d / CR 701.16)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(km);
        _alice.Zones.Graveyard.GetCards().Should().Contain(km);
    }

    [Fact]
    public void UpkeepTick_OffBattlefield_NoOp()
    {
        var km = KeldonMaraudersFactory.Create(_alice);
        km.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(km);

        var before = km.Counters.Count(CounterType.Time);
        KeldonMaraudersFactory.PerformUpkeepTick(km, _alice, zones: null);

        km.Counters.Count(CounterType.Time).Should().Be(before,
            "off-battlefield upkeep tick is a no-op");
    }

    // -----------------------------------------------------------------------
    // Damage triggers — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void KeldonMarauders_HasEntersAndLeavesDamageTriggers()
    {
        var km = KeldonMaraudersFactory.Create(_alice);

        // Upkeep (no targets) + enters + leaves = 3 triggered abilities;
        // two of them are 1..1 "target player or planeswalker" damage
        // triggers.
        var damageTriggers = km.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.TargetRequests.Count == 1
                        && t.TargetRequests[0].Description.Contains("player or planeswalker"))
            .ToList();

        damageTriggers.Should().HaveCount(2,
            "enters AND leaves the battlefield each deal 1 damage");
        damageTriggers.Should().OnlyContain(t =>
            t.TargetRequests[0].MinTargets == 1 && t.TargetRequests[0].MaxTargets == 1);
    }

    // -----------------------------------------------------------------------
    // Damage triggers — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTrigger_DealsOneToPlayerTarget()
    {
        var km = KeldonMaraudersFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = DamageTriggers(km).First();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        trigger.Resolve();

        _bob.LifeTotal.Should().Be(19, "1 damage to Bob");
        _bob.LifeLostThisTurn.Should().Be(1);
    }

    [Fact]
    public void DamageTrigger_DealsOneToPlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.8 — damage to a planeswalker removes that many loyalty counters.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 5,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var km = KeldonMaraudersFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = DamageTriggers(km).First();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        trigger.Resolve();

        pw.Loyalty.Should().Be(4, "1 loyalty counter removed (5 - 1)");
    }

    [Fact]
    public void DamageTrigger_CreatureTarget_NoOps()
    {
        // CR 608.2b — a creature is not a legal "player or planeswalker"
        // target; if one is somehow resolved (redirect), the effect no-ops.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var km = KeldonMaraudersFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = DamageTriggers(km).First();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        trigger.Resolve();

        bears.Damage.Should().Be(0, "a creature is not a legal target — no damage");
    }

    [Fact]
    public void DamageTrigger_NoChosenTarget_NoOps()
    {
        var km = KeldonMaraudersFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(km);
        km.SetZone(ZoneType.Battlefield);

        var trigger = DamageTriggers(km).First();

        // No targets set — resolution is a clean no-op (CR 608.2b).
        trigger.Resolve();

        _bob.LifeTotal.Should().Be(20);
    }

    private static List<TriggeredAbility> DamageTriggers(Creature km) =>
        km.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.TargetRequests.Count == 1
                        && t.TargetRequests[0].Description.Contains("player or planeswalker"))
            .ToList();
}

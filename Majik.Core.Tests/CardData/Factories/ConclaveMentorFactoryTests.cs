using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ConclaveMentorFactory"/> — Creature — Centaur Cleric
/// {G}{W} 2/2 (Jumpstart / Commander Legends). Oracle:
///   "If one or more +1/+1 counters would be put on a creature you control,
///    that many plus one +1/+1 counters are put on that creature instead.
///    When this creature dies, you gain life equal to its power."
///
/// Covers:
///   - Card identity (Creature — Centaur Cleric, {G}{W}, 2/2, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Counter-replacement: +1/+1 placement on a controlled creature bumps by 1
///     (CR 614 — same shape as Hardened Scales, +1 not doubling).
///   - Scoping: opponent's creature + -1/-1 counters are not bumped.
///   - Dies trigger: controller gains life equal to the creature's power
///     (CR 603.6c / 603.10a last-known-information).
/// </summary>
public class ConclaveMentorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ConclaveMentor_IsCentaurCleric_AtGW_TwoTwo()
    {
        var c = ConclaveMentorFactory.Create(_alice);

        c.Name.Should().Be("Conclave Mentor");
        c.ManaCost.Should().Be("{G}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Centaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ConclaveMentor()
    {
        var card = NamedCardFactory.Create("Conclave Mentor", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Conclave Mentor");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{G}{W}");
    }

    [Fact]
    public void Mentor_HasOneTrigger_NoActivatedOrManaAbilities()
    {
        var c = ConclaveMentorFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "only the dies trigger is a triggered ability; the counter bump is a replacement");
    }

    // -----------------------------------------------------------------------
    // Counter-replacement — +1 (not doubling), CR 614
    // -----------------------------------------------------------------------

    [Fact]
    public void CountersService_OnControlledCreature_BumpsByOne()
    {
        var bus = new ReplacementBus();
        var mentor = ConclaveMentorFactory.Create(_alice, triggers: null, replacements: bus);
        PlaceOnBattlefield(mentor, _alice);

        var bear = new Creature("Some Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(2, "1 requested + 1 from Conclave Mentor");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void CountersService_ThreeCounters_BumpsToFour()
    {
        var bus = new ReplacementBus();
        var mentor = ConclaveMentorFactory.Create(_alice, triggers: null, replacements: bus);
        PlaceOnBattlefield(mentor, _alice);

        var bear = new Creature("Some Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 3, bus);

        placed.Should().Be(4, "3 + 1 (Conclave Mentor adds just one, never doubles)");
    }

    [Fact]
    public void CountersService_OpponentCreature_NotBumped()
    {
        var bus = new ReplacementBus();
        var mentor = ConclaveMentorFactory.Create(_alice, triggers: null, replacements: bus);
        PlaceOnBattlefield(mentor, _alice);

        var bobBear = new Creature("Bob Bear", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bobBear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(1, "Conclave Mentor only affects 'a creature you control'");
    }

    [Fact]
    public void CountersService_MinusOneMinusOne_NotBumped()
    {
        var bus = new ReplacementBus();
        var mentor = ConclaveMentorFactory.Create(_alice, triggers: null, replacements: bus);
        PlaceOnBattlefield(mentor, _alice);

        var bear = new Creature("Some Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bear, CounterType.MinusOneMinusOne, 1, bus);

        placed.Should().Be(1, "Conclave Mentor scopes to +1/+1 counters only");
    }

    [Fact]
    public void ConclaveMentor_InHand_DoesNotBump()
    {
        var bus = new ReplacementBus();
        var mentor = ConclaveMentorFactory.Create(_alice, triggers: null, replacements: bus);
        // Not placed on battlefield.

        var bear = new Creature("Some Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);
        placed.Should().Be(1, "Conclave Mentor must be on the battlefield to fire");
    }

    // -----------------------------------------------------------------------
    // Dies trigger — gain life equal to its power (CR 603.6c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Dies_GainsLifeEqualToPower()
    {
        var start = _alice.LifeTotal;

        var mentor = ConclaveMentorFactory.Create(_alice);
        var dies = mentor.Abilities.OfType<TriggeredAbility>().Single();
        dies.Resolve();

        _alice.LifeTotal.Should().Be(start + 2,
            "Conclave Mentor is a 2/2, so its controller gains 2 life on death");
    }

    [Fact]
    public void Dies_GainsLifeEqualToPower_WithCounters()
    {
        var start = _alice.LifeTotal;

        var mentor = ConclaveMentorFactory.Create(_alice);
        mentor.SetController(_alice);
        // Wire the continuous-effects layer so +1/+1 counters feed Power
        // (CR 613.4d — counters apply in the P/T layer).
        mentor.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(mentor);
        mentor.SetZone(ZoneType.Battlefield);
        // Two +1/+1 counters → power 4.
        mentor.Counters.Add(CounterType.PlusOnePlusOne, 2);
        mentor.Power.Should().Be(4, "2 base + 2 counters");

        var dies = mentor.Abilities.OfType<TriggeredAbility>().Single();
        dies.Resolve();

        _alice.LifeTotal.Should().Be(start + 4,
            "a 2/2 with two +1/+1 counters is a 4/4, so the controller gains 4 life");
    }

    private static void PlaceOnBattlefield(Creature mentor, Player owner)
    {
        owner.Zones.Battlefield.AddCard(mentor);
        mentor.SetZone(ZoneType.Battlefield);
    }
}

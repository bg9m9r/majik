using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="InfernoTitanFactory"/> (Magic 2011 / Modern staple,
/// {4}{R}{R}). Creature — Giant, 6/6:
///   "{R}: This creature gets +1/+0 until end of turn.
///    Whenever this creature enters or attacks, it deals 3 damage divided as
///    you choose among one, two, or three targets."
///
/// Covers:
/// - Identity (Creature, Giant subtype, {4}{R}{R}, 6/6, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Firebreathing {R}: +1/+0 EOT activated ability (CR 602 / CR 613.1f).
/// - ETB trigger: fires when Inferno Titan enters the battlefield (CR 603.6a).
/// - Attack trigger: matches Inferno Titan only (CR 508.1f self-match).
/// - Divided-damage allocation summing to exactly 3 across 1..3 targets
///   (CR 601.2d / CR 119.4).
/// </summary>
[Trait("Color", "R")]
public class InfernoTitanFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void InfernoTitan_Identity()
    {
        var c = InfernoTitanFactory.Create(_alice);

        c.Name.Should().Be("Inferno Titan");
        c.ManaCost.Should().Be("{4}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void InfernoTitan_DispatchesThroughNamedFactory()
    {
        var c = NamedCardFactory.Create("Inferno Titan", _alice);

        c.Should().NotBeNull();
        c.Name.Should().Be("Inferno Titan");
        c.Should().BeAssignableTo<Creature>();
    }

    // -----------------------------------------------------------------------
    // Firebreathing {R}: +1/+0
    // -----------------------------------------------------------------------

    [Fact]
    public void InfernoTitan_HasFirebreathingActivatedAbility()
    {
        var c = InfernoTitanFactory.Create(_alice);

        var pump = c.Abilities.OfType<ActivatedAbility>().ToList();
        pump.Should().ContainSingle(
            "CR 602 — Inferno Titan has the single {R}: +1/+0 firebreathing ability.");

        var cost = pump[0].Costs.OfType<ManaCostCost>().Single();
        cost.Cost.Should().Be(Majik.Core.ValueObjects.ManaCost.Parse("{R}"),
            "CR 602 — firebreathing costs {R}.");
    }

    [Fact]
    public void InfernoTitan_Firebreathing_AddsPlusOnePlusZeroForTheTurn()
    {
        var c = InfernoTitanFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        // Wire a continuous-effects service so the Layer 7c pump is observable
        // (CR 613.1f). Without it the printed 6/6 surfaces unmodified.
        var ces = new ContinuousEffectsService();
        c.ActiveEffects = ces;

        var pump = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in pump.Effects) e.Execute();

        c.Power.Should().Be(7, "CR 613.1f Layer 7c — {R}: +1/+0 until end of turn.");
        c.Toughness.Should().Be(6, "firebreathing only modifies power.");
    }

    // -----------------------------------------------------------------------
    // ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void InfernoTitan_EtbTrigger_FiresOnSelfEntering()
    {
        var c = InfernoTitanFactory.Create(_alice);

        var trigger = GetEtbTrigger(c);
        var cond = (EventTriggerCondition<CardMovedEvent>)trigger.Condition;

        cond.Matches(
            new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeTrue("CR 603.6a — 'whenever this creature enters'.");

        var other = new Creature("Bear", "G", 2, 2);
        cond.Matches(
            new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeFalse("only Inferno Titan's own ETB fires this trigger.");

        cond.Matches(
            new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Graveyard), trigger)
            .Should().BeFalse("leaving the battlefield is not an enter event.");
    }

    // -----------------------------------------------------------------------
    // Attack trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void InfernoTitan_AttackTrigger_MatchesSelfOnly()
    {
        var c = InfernoTitanFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(c);

        trigger.IsTriggered(new CreatureAttacksEvent(c, _bob)).Should().BeTrue(
            "CR 508.1f — 'whenever this creature attacks' self-match.");

        var other = new Creature("Bear", "G", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the attack trigger only fires for Inferno Titan itself.");
    }

    // -----------------------------------------------------------------------
    // Divided-damage allocation (CR 601.2d / CR 119.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void DealsThreeDamage_DefaultSplit_OneTarget_AllOnIt()
    {
        var alloc = InfernoTitanFactory.DefaultAllocation(new object[] { _bob }, 3);

        alloc.Values.Sum().Should().Be(3,
            "CR 119.4 — the full 3 damage must be assigned.");
        alloc[_bob].Should().Be(3, "single target takes all 3.");
    }

    [Fact]
    public void DealsThreeDamage_DefaultSplit_ThreeTargets_OneEach()
    {
        var t1 = new Creature("A", "R", 4, 4);
        var t2 = new Creature("B", "R", 4, 4);
        var t3 = new Creature("C", "R", 4, 4);

        var alloc = InfernoTitanFactory.DefaultAllocation(new object[] { t1, t2, t3 }, 3);

        alloc.Values.Sum().Should().Be(3,
            "CR 119.4 — exactly 3 damage divided, at least 1 per chosen target.");
        alloc[t1].Should().Be(1);
        alloc[t2].Should().Be(1);
        alloc[t3].Should().Be(1);
    }

    [Fact]
    public void DealsThreeDamage_AppliesAllocationToTargets()
    {
        var c1 = new Creature("Grizzly", "G", 2, 2);
        c1.SetController(_bob);
        c1.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(c1);

        var startLife = _bob.LifeTotal;

        // 2 to the creature (lethal vs a 2/2), 1 to Bob's face.
        var distribute = new Func<IReadOnlyList<object>, int, IReadOnlyDictionary<object, int>>(
            (targets, total) => new Dictionary<object, int> { [c1] = 2, [_bob] = 1 });

        InfernoTitanFactory.DealDividedDamage(
            new object[] { c1, _bob }, distribute);

        c1.Damage.Should().Be(2, "CR 119.2 — marked damage on the creature.");
        _bob.LifeTotal.Should().Be(startLife - 1, "CR 119.3 — 1 damage to the player.");
    }

    [Fact]
    public void DealDividedDamage_NoTargets_IsNoOp()
    {
        var act = () => InfernoTitanFactory.DealDividedDamage(
            System.Array.Empty<object>(), distribute: null);

        act.Should().NotThrow("CR 608.2b — no legal targets = the ability does nothing.");
    }
}

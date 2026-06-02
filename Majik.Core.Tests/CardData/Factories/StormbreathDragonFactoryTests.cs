using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StormbreathDragonFactory"/> (Theros, {3}{R}{R}).
///
/// Creature — Dragon 4/4. Oracle text:
///   "Flying. Haste. Protection from white.
///    Monstrosity 3.
///    When Stormbreath Dragon becomes monstrous, if you have seven or
///    more cards in hand, it deals 3 damage to each opponent."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - Flying + Haste keyword markers.
///   - Protection from white via Rules.Protection.HasProtectionFromColor.
///   - Monstrosity activation places three +1/+1 counters + flips the
///     monstrous flag; second activation no-ops.
///   - Becomes-monstrous trigger fires 3-damage-each-opponent IFF hand
///     size ≥ 7.
/// </summary>
[Trait("Color", "R")]
public class StormbreathDragonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_ShipsCreatureShape_RedHybrid()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);

        dragon.Should().BeOfType<Creature>();
        dragon.Name.Should().Be("Stormbreath Dragon");
        dragon.Power.Should().Be(4);
        dragon.Toughness.Should().Be(4);
        dragon.ManaCost.Should().Be("{3}{R}{R}");
        dragon.ManaCostValue.TotalValue.Should().Be(5);
        dragon.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        dragon.Owner.Should().BeSameAs(_alice);
        dragon.Controller.Should().BeSameAs(_alice);
    }
    // -------------------------------------------------------------------------
    // Keyword markers + protection
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_AttachesFlying_Haste_ProtectionFromWhite()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);

        var keywords = dragon.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Haste");

        // Protection from white as a ProtectionAbility (not a keyword
        // marker — Protection rides its own ability shape).
        dragon.Abilities.OfType<ProtectionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ProtectionFromWhite_HasProtectionFromColor_ReadsWhite()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);

        Protection.HasProtectionFromColor(dragon, ManaColor.White).Should().BeTrue();
        Protection.HasProtectionFromColor(dragon, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(dragon, ManaColor.Red).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Monstrosity activation
    // -------------------------------------------------------------------------

    [Fact]
    public void Monstrosity_AddsThreePlusOnePlusOneCounters_AndMarksMonstrous()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);
        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();

        monstrosity.IsMonstrous.Should().BeFalse("starts not-monstrous");
        dragon.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        foreach (var e in monstrosity.Effects) e.Execute();

        monstrosity.IsMonstrous.Should().BeTrue();
        dragon.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(StormbreathDragonFactory.MonstrosityCounters);
    }

    [Fact]
    public void Monstrosity_SecondActivation_NoOps()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);
        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();

        foreach (var e in monstrosity.Effects) e.Execute();
        foreach (var e in monstrosity.Effects) e.Execute(); // second pop

        dragon.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(StormbreathDragonFactory.MonstrosityCounters,
                "CR 702.95b — the activation self-gates on the monstrous flag");
    }

    [Fact]
    public void Monstrosity_Cost_IsFiveRR()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);
        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();

        var manaCost = monstrosity.Costs.OfType<ManaCostCost>().SingleOrDefault();
        manaCost.Should().NotBeNull();
        manaCost!.Cost.TotalValue.Should().Be(7, "5 generic + 2 red = 7");
        manaCost!.Cost.Red.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Becomes-monstrous trigger
    // -------------------------------------------------------------------------

    [Fact]
    public void BecomesMonstrous_HandSizeSeven_DealsThreeToEachOpponent()
    {
        var dragon = StormbreathDragonFactory.Create(_alice, opponentsResolver: () => new[] { _bob });

        // Fill Alice's hand to 7 cards (vanilla shells).
        for (var i = 0; i < 7; i++)
        {
            var c = new Card($"Filler{i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(c);
        }

        var bobLifeBefore = _bob.LifeTotal;

        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();
        foreach (var e in monstrosity.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore - StormbreathDragonFactory.BecomesMonstrousDamage);
    }

    [Fact]
    public void BecomesMonstrous_HandSizeUnderSeven_DoesNotDeal()
    {
        var dragon = StormbreathDragonFactory.Create(_alice, opponentsResolver: () => new[] { _bob });

        // Only 6 cards in hand — intervening-if fails (CR 603.6c).
        for (var i = 0; i < 6; i++)
        {
            var c = new Card($"Filler{i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(c);
        }

        var bobLifeBefore = _bob.LifeTotal;

        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();
        foreach (var e in monstrosity.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore,
            "intervening-if fails at 6 cards — no damage even though monstrous flips");
    }

    [Fact]
    public void BecomesMonstrous_NoOpponentsResolver_NoOp()
    {
        // Single-arg overload — no opponents resolver, no damage even
        // when the hand threshold is met (defensive — shape-only tests).
        var dragon = StormbreathDragonFactory.Create(_alice);

        for (var i = 0; i < 7; i++)
        {
            var c = new Card($"Filler{i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(c);
        }

        var bobLifeBefore = _bob.LifeTotal;

        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();
        foreach (var e in monstrosity.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore);
    }
}

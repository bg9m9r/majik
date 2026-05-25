using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="HollowOneFactory"/>.
///
/// Hollow One (Hour of Devotion, {5}):
///   Artifact Creature — Golem 4/4.
///   This spell costs {2} less to cast for each card you've cycled or
///   discarded this turn.
///
/// Covers:
///   - Card identity (Golem 4/4, {5}, Artifact + Creature dual-type,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Cost reduction at 0 / 1 / 2 / 3 discards (full path:
///     0 → {5}, 1 → {3}, 2 → {1}, 3 → {0} via floor-at-zero).
///   - Cycles + discards stack (both counters feed the reducer).
///   - Shape-only path (no TurnState) keeps the reducer ability but
///     returns 0 reduction.
///   - Other-player discards don't contribute to the caster's reducer.
///   - TurnState.Reset clears the cycle + discard counters.
/// </summary>
public class HollowOneTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HollowOne_Identity_ArtifactGolem_4_4_At_5()
    {
        var hollow = HollowOneFactory.Create(_alice);

        hollow.Name.Should().Be("Hollow One");
        hollow.ManaCost.Should().Be("{5}");
        hollow.HasType(CardType.Creature).Should().BeTrue();
        hollow.HasType(CardType.Artifact).Should().BeTrue(
            "Hollow One is an Artifact Creature — both types must read true");
        hollow.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        hollow.BasePower.Should().Be(4);
        hollow.BaseToughness.Should().Be(4);
        hollow.Owner.Should().BeSameAs(_alice);
        hollow.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HollowOne()
    {
        var card = NamedCardFactory.Create("Hollow One", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Hollow One");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
        ((Creature)card).BaseToughness.Should().Be(4);

        // Reducer ability attached.
        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void HollowOne_NoTurnState_ReducerReturnsZero_PaysFullCost()
    {
        // Shape-only path — TurnState is null → reducer always returns 0.
        var hollow = HollowOneFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(hollow, _alice);

        effective.Generic.Should().Be(5);
    }

    [Fact]
    public void HollowOne_EmptyTurnState_PaysFullCost()
    {
        // Fresh TurnState — 0 cycles, 0 discards. Pays {5}.
        var ts = new TurnState();
        var hollow = HollowOneFactory.Create(_alice, ts);

        var effective = CostReduction.GetEffectiveCost(hollow, _alice);

        effective.Generic.Should().Be(5);
    }

    [Fact]
    public void HollowOne_OneDiscardThisTurn_Reduces_2()
    {
        // 1 discard → reduction = 1 * 2 = 2. Pays {3}.
        var ts = new TurnState();
        ts.RecordCardDiscarded(_alice);

        var hollow = HollowOneFactory.Create(_alice, ts);
        var effective = CostReduction.GetEffectiveCost(hollow, _alice);

        effective.Generic.Should().Be(3);
    }

    [Fact]
    public void HollowOne_TwoDiscardsThisTurn_Reduces_4()
    {
        // 2 discards → reduction = 2 * 2 = 4. Pays {1}.
        var ts = new TurnState();
        ts.RecordCardDiscarded(_alice);
        ts.RecordCardDiscarded(_alice);

        var hollow = HollowOneFactory.Create(_alice, ts);
        var effective = CostReduction.GetEffectiveCost(hollow, _alice);

        effective.Generic.Should().Be(1);
    }

    [Fact]
    public void HollowOne_ThreeDiscards_FloorsAtZero()
    {
        // 3 discards → reduction = 6 > printed 5. Floors at 0 generic
        // (no coloured pips on Hollow One). Pays {0}.
        var ts = new TurnState();
        ts.RecordCardDiscarded(_alice);
        ts.RecordCardDiscarded(_alice);
        ts.RecordCardDiscarded(_alice);

        var hollow = HollowOneFactory.Create(_alice, ts);
        var effective = CostReduction.GetEffectiveCost(hollow, _alice);

        effective.Generic.Should().Be(0,
            "reduction can't drive cost below zero (CR 117.7c)");
    }

    [Fact]
    public void HollowOne_CyclesAndDiscards_Stack()
    {
        // 1 cycle + 1 discard → reduction = (1 + 1) * 2 = 4. Pays {1}.
        var ts = new TurnState();
        ts.RecordCardCycled(_alice);
        ts.RecordCardDiscarded(_alice);

        var hollow = HollowOneFactory.Create(_alice, ts);
        var effective = CostReduction.GetEffectiveCost(hollow, _alice);

        effective.Generic.Should().Be(1);
    }

    [Fact]
    public void HollowOne_DiscardsByOtherPlayer_DoNotReduce()
    {
        // Bob discards — Alice's Hollow One reducer reads
        // DiscardsByPlayer(alice) which is still 0.
        var bob = new Player("Bob", 20);
        var ts = new TurnState();
        ts.RecordCardDiscarded(bob);
        ts.RecordCardDiscarded(bob);

        var hollow = HollowOneFactory.Create(_alice, ts);
        var effective = CostReduction.GetEffectiveCost(hollow, _alice);

        effective.Generic.Should().Be(5,
            "Hollow One's reducer is scoped to the caster — opponent " +
            "discards don't count");
    }

    [Fact]
    public void TurnState_Reset_ClearsCyclesAndDiscards()
    {
        var ts = new TurnState();
        ts.RecordCardCycled(_alice);
        ts.RecordCardDiscarded(_alice);
        ts.RecordCardDiscarded(_alice);

        ts.CyclesByPlayer(_alice).Should().Be(1);
        ts.DiscardsByPlayer(_alice).Should().Be(2);

        ts.Reset();

        ts.CyclesByPlayer(_alice).Should().Be(0);
        ts.DiscardsByPlayer(_alice).Should().Be(0);
    }
}

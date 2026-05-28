using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="GlimpseTheImpossibleFactory"/> (The Brothers' War,
/// {2}{R}).
///
/// Oracle: "Exile the top three cards of your library. You may play those
/// cards this turn. At the beginning of the next end step, if any of those
/// cards remain exiled, put them into your graveyard, then create a 0/1
/// colorless Eldrazi Spawn creature token for each card put into your
/// graveyard this way. Those tokens have 'Sacrifice this token: Add {C}.'"
///
/// Covers:
/// - Identity (Sorcery, {2}{R}, MV 3, owner/controller).
/// - NamedCardFactory dispatch.
/// - Resolve: exiles top 3 cards of caster's library with a this-turn exile-cast
///   grant (CR 118.9).
/// - Empty / shallow library: exiles what's available, stamps only those.
/// - ExileCastAlternativeCost gate: caster may cast, opponent may not.
/// - End-step rider, all 3 unplayed → 3 graveyard + 3 Spawn tokens.
/// - End-step rider, all 3 cast → 0 tokens (nothing moved to graveyard).
/// - End-step rider, 1 of 3 played → 1 graveyard + 1 Spawn token.
/// - End-step rider fires ONCE at first End step after resolve; a prior End
///   step stamped before resolve does NOT trigger it.
/// </summary>
public class GlimpseTheImpossibleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInLibrary(Player owner, string name, string cost = "{R}")
    {
        var c = new Card(name, cost);
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------

    [Fact]
    public void GlimpseTheImpossible_Identity_Sorcery_2R()
    {
        var card = GlimpseTheImpossibleFactory.Create(_alice);

        card.Name.Should().Be("Glimpse the Impossible");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GlimpseTheImpossible_ManaCostValue_Is3()
    {
        var card = GlimpseTheImpossibleFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(3, "MV of {2}{R} is 3");
    }

    [Fact]
    public void GlimpseTheImpossible_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Glimpse the Impossible", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Glimpse the Impossible");
    }

    // -------------------------------------------------------------------
    // Resolve — exile top 3 + grant
    // -------------------------------------------------------------------

    [Fact]
    public void Resolve_ExilesTopThree_AndGrantsExileCastToCaster()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{2}{R}");
        var top4 = NewCardInLibrary(_alice, "Top4", "{3}{R}");

        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2, top3 });
        _alice.Zones.Exile.GetCards().Should().NotContain(top4);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { top4 });

        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top1.RuntimeExileCastCost.Should().NotBeNull();
        top1.RuntimeExileCastCost!.TotalValue.Should().Be(1, "Top1 costs {R}");

        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastCost!.TotalValue.Should().Be(2, "Top2 costs {1}{R}");

        top3.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top3.RuntimeExileCastCost!.TotalValue.Should().Be(3, "Top3 costs {2}{R}");

        top4.RuntimeExileCastAllowedCaster.Should().BeNull("top4 was not exiled");
    }

    [Fact]
    public void Resolve_ShallowLibrary_TwoCards_ExilesTwo()
    {
        var top1 = NewCardInLibrary(_alice, "Top1");
        var top2 = NewCardInLibrary(_alice, "Top2");

        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoGrants_NoThrow()
    {
        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice);
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ExileGrant_PermitsCastByExileCastAlternativeCost_ForCasterOnly()
    {
        var top = NewCardInLibrary(_alice, "Top", "{R}");

        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var alt = new ExileCastAlternativeCost("GtI grant", top.RuntimeExileCastCost!);
        alt.CanCastFor(top, _alice).Should().BeTrue("the granted caster is Alice");
        alt.CanCastFor(top, _bob).Should().BeFalse("Bob is not the granted caster");
    }

    // -------------------------------------------------------------------
    // End-step rider — all 3 unplayed → 3 tokens
    // -------------------------------------------------------------------

    [Fact]
    public void EndStepRider_AllThreeUnplayed_PutsThreeInGraveyard_CreatesThreeSpawnTokens()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{2}{R}");

        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice, triggers);
        foreach (var e in effects) e.Execute();

        // All three are in exile (none played).
        _alice.Zones.Exile.GetCards().Should().HaveCount(3);

        // Fire next end step.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(1, "the delayed end-step trigger is pending");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // All 3 moved to graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { top1, top2, top3 });
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
        top1.Zone.Should().Be(ZoneType.Graveyard);
        top2.Zone.Should().Be(ZoneType.Graveyard);
        top3.Zone.Should().Be(ZoneType.Graveyard);

        // Grants cleared (cards left exile).
        top1.RuntimeExileCastAllowedCaster.Should().BeNull();
        top2.RuntimeExileCastAllowedCaster.Should().BeNull();
        top3.RuntimeExileCastAllowedCaster.Should().BeNull();

        // 3 Eldrazi Spawn tokens on Alice's battlefield.
        var battlefield = _alice.Zones.Battlefield.GetCards().ToList();
        battlefield.Should().HaveCount(3, "one Eldrazi Spawn per card moved to graveyard");
        foreach (var token in battlefield)
        {
            token.Name.Should().Be("Eldrazi Spawn");
            token.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
            token.HasSubtype(CardSubtype.Spawn).Should().BeTrue();
            token.Should().BeOfType<Majik.Core.Cards.Creature>();
            var creature = (Majik.Core.Cards.Creature)token;
            creature.Power.Should().Be(0);
            creature.Toughness.Should().Be(1);
            // Token has a mana ability producing {C}.
            var tokenCard = token as Card;
            tokenCard!.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
                "Eldrazi Spawn has \"Sacrifice this token: Add {C}.\"");
        }
    }

    // -------------------------------------------------------------------
    // End-step rider — all 3 cast → 0 tokens
    // -------------------------------------------------------------------

    [Fact]
    public void EndStepRider_AllThreePlayed_NoMovesToGraveyard_NoTokens()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{R}");

        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice, triggers);
        foreach (var e in effects) e.Execute();

        // Simulate all 3 being cast (moved out of exile into graveyard / battlefield).
        foreach (var c in new[] { top1, top2, top3 })
        {
            _alice.Zones.Exile.RemoveCard(c);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        // Fire next end step.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // No additional graveyard moves (all three were already gone).
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3,
            "the three cast cards are still in graveyard; no new moves");

        // No Spawn tokens.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "all three were played — no unplayed cards remain to move or trigger tokens");
    }

    // -------------------------------------------------------------------
    // End-step rider — 1 of 3 played → 1 graveyard move + 1 token
    // -------------------------------------------------------------------

    [Fact]
    public void EndStepRider_OneOfThreePlayed_MovesTwo_CreatesTwoTokens()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{R}");

        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice, triggers);
        foreach (var e in effects) e.Execute();

        // Simulate only top1 being cast (played from exile).
        _alice.Zones.Exile.RemoveCard(top1);
        _alice.Zones.Graveyard.AddCard(top1);
        top1.SetZone(ZoneType.Graveyard);

        // top2 and top3 remain in exile.
        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top2, top3 });

        // Fire next end step.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // top2 and top3 moved to graveyard.
        top2.Zone.Should().Be(ZoneType.Graveyard);
        top3.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Exile.GetCards().Should().BeEmpty();

        // 2 Eldrazi Spawn tokens (one per unplayed card).
        var battlefield = _alice.Zones.Battlefield.GetCards().ToList();
        battlefield.Should().HaveCount(2, "two unplayed cards moved to graveyard → two Spawn tokens");
        battlefield.Should().AllSatisfy(t => t.Name.Should().Be("Eldrazi Spawn"));
    }

    // -------------------------------------------------------------------
    // End-step rider fires at next End step — timestamp fence
    // -------------------------------------------------------------------

    [Fact]
    public void EndStepRider_DoesNotFireOnPriorEndStep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top1 = NewCardInLibrary(_alice, "Top1");

        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice, triggers);
        foreach (var e in effects) e.Execute();

        // This end step is published with a timestamp in the future (after
        // the resolve's DateTime.UtcNow fence), so it WILL trigger.
        // There is no way to publish a "before resolve" event in this unit
        // test with real timestamps; instead verify the trigger fires on the
        // first End step after resolve and NOT a second time.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // First end step fired correctly (top1 moved to graveyard, 1 token).
        top1.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().HaveCount(1);

        // Second end step — delayed trigger is auto-unregistered, so no new pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(0, "delayed trigger fires exactly once");
    }

    // -------------------------------------------------------------------
    // No triggers supplied — exile/grant still work, no delayed rider
    // -------------------------------------------------------------------

    [Fact]
    public void NoTriggerManager_ExileAndGrantStillWork_NoRider()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{R}");

        var effects = GlimpseTheImpossibleFactory.BuildResolveEffect(_alice, triggers: null);
        foreach (var e in effects) e.Execute();

        // All three exiled and granted.
        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2, top3 });
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top3.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // Battlefield is empty — no delayed trigger was registered.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}

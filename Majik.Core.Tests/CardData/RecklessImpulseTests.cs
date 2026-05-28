using FluentAssertions;
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
/// Tests for <see cref="RecklessImpulseFactory"/> (Innistrad: Crimson Vow,
/// {1}{R}). Oracle: "Exile the top two cards of your library. Until the end
/// of your next turn, you may play those cards."
///
/// Covers:
/// - Identity (Sorcery, {1}{R}, MV 2, owner/controller).
/// - NamedCardFactory dispatch.
/// - Resolve: exiles top 2 cards of caster's library to their Exile zone
///   and stamps a runtime exile-cast grant (CR 118.9) for the caster on each.
/// - "Until end of your next turn" cleanup: first Cleanup of the caster's
///   current turn keeps the grant; second Cleanup (caster's next turn) clears.
/// - Empty / shallow library: exiles what's available, stamps only those.
/// </summary>
public class RecklessImpulseTests
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
    public void RecklessImpulse_Identity_Sorcery_1R()
    {
        var card = RecklessImpulseFactory.Create(_alice);

        card.Name.Should().Be("Reckless Impulse");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RecklessImpulse_ManaCostValue_Is2()
    {
        var card = RecklessImpulseFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(2, "MV of {1}{R} is 2");
    }

    [Fact]
    public void RecklessImpulse_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Reckless Impulse", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Reckless Impulse");
    }

    // -------------------------------------------------------------------
    // Resolve — exile top 2 + grant
    // -------------------------------------------------------------------

    [Fact]
    public void Resolve_ExilesTopTwo_AndGrantsExileCastToCaster()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{2}{R}");

        var effects = RecklessImpulseFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2 });
        _alice.Zones.Exile.GetCards().Should().NotContain(top3);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { top3 });

        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top1.RuntimeExileCastCost.Should().NotBeNull();
        top1.RuntimeExileCastCost!.TotalValue.Should().Be(1, "Top1 costs {R}");

        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastCost!.TotalValue.Should().Be(2, "Top2 costs {1}{R}");
    }

    [Fact]
    public void Resolve_ShallowLibrary_OneCard_ExilesOneGrantsOne()
    {
        var top1 = NewCardInLibrary(_alice, "Top1");

        var effects = RecklessImpulseFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(top1);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoGrants_NoThrow()
    {
        var effects = RecklessImpulseFactory.BuildResolveEffect(_alice);
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ExileGrant_PermitsCastByExileCastAlternativeCost_ForCaster()
    {
        var top = NewCardInLibrary(_alice, "Top", "{R}");

        var effects = RecklessImpulseFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var alt = new ExileCastAlternativeCost("RI grant", top.RuntimeExileCastCost!);
        alt.CanCastFor(top, _alice).Should().BeTrue("the granted caster is Alice");
        alt.CanCastFor(top, _bob).Should().BeFalse("Bob is not the granted caster");
    }

    // -------------------------------------------------------------------
    // EOT cleanup via Cleanup-step counting
    // -------------------------------------------------------------------

    [Fact]
    public void EotCleanup_FirstCleanupKeepsGrant_SecondClears()
    {
        var bus = new EventBus();

        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");

        var effects = RecklessImpulseFactory.BuildResolveEffect(_alice, bus);
        foreach (var e in effects) e.Execute();

        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // 1st Cleanup — Alice's current turn. Grant must persist.
        bus.Publish(new Majik.Core.Events.StepStartedEvent(PhaseStateType.Cleanup, _alice));
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "first cleanup belongs to caster's current turn — grant persists through EOT");
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "first cleanup belongs to caster's current turn — grant persists through EOT");

        // A cleanup on Bob's intervening turn does NOT count. Grant still alive.
        bus.Publish(new Majik.Core.Events.StepStartedEvent(PhaseStateType.Cleanup, _bob));
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Bob's cleanup is not 'your next turn' — Alice's grant survives");

        // 2nd Cleanup belonging to Alice — her next turn. Grant clears.
        bus.Publish(new Majik.Core.Events.StepStartedEvent(PhaseStateType.Cleanup, _alice));
        top1.RuntimeExileCastAllowedCaster.Should().BeNull(
            "second cleanup belonging to caster = end of caster's next turn — grant cleared");
        top2.RuntimeExileCastAllowedCaster.Should().BeNull(
            "second cleanup belonging to caster = end of caster's next turn — grant cleared");
    }

    [Fact]
    public void EotCleanup_NoBus_GrantPersistsIndefinitely()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");

        var effects = RecklessImpulseFactory.BuildResolveEffect(_alice, eventBus: null);
        foreach (var e in effects) e.Execute();

        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EotCleanup_ExilesExactlyTwo_NotThree_DistinctFromLightUpTheStage()
    {
        // Confirm Reckless Impulse exiles TWO, not three (unlike Light Up the Stage).
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{R}");

        var effects = RecklessImpulseFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().HaveCount(2);
        _alice.Zones.Library.GetCards().Should().Contain(top3);
        top3.RuntimeExileCastAllowedCaster.Should().BeNull("top3 was not exiled");
    }
}

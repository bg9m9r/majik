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
/// Tests for <see cref="LightUpTheStageFactory"/> (Ravnica Allegiance,
/// {2}{R}). Oracle: "Spectacle {R} (...) Exile the top three cards of your
/// library. Until the end of your next turn, you may play those cards."
///
/// Covers:
/// - Identity (Sorcery, {2}{R}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Resolve: exiles top 3 cards of caster's library to their Exile zone
///   and stamps a runtime exile-cast grant (CR 118.9) for the caster on
///   each card.
/// - "Until end of your next turn" cleanup: first Cleanup of the caster's
///   current turn keeps the grant; second Cleanup (caster's next turn)
///   clears the grant.
/// - Empty / shallow library: exiles what's available and stamps only those.
/// - Spectacle alt cost: returns a non-null cost when an opponent has lost
///   life this turn; returns null otherwise.
/// </summary>
public class LightUpTheStageTests
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
    public void LightUpTheStage_Identity_Sorcery_2R()
    {
        var card = LightUpTheStageFactory.Create(_alice);

        card.Name.Should().Be("Light Up the Stage");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LightUpTheStage_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Light Up the Stage", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Light Up the Stage");
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

        var effects = LightUpTheStageFactory.BuildResolveEffect(_alice);
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
    }

    [Fact]
    public void Resolve_ShallowLibrary_ExilesAvailable_NoExtraGrants()
    {
        // Only two cards in library.
        var top1 = NewCardInLibrary(_alice, "Top1");
        var top2 = NewCardInLibrary(_alice, "Top2");

        var effects = LightUpTheStageFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoGrants_NoThrow()
    {
        var effects = LightUpTheStageFactory.BuildResolveEffect(_alice);
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ExileGrant_PermitsCastByExileCastAlternativeCost_ForCaster()
    {
        // Confirm the grant is wired so ExileCastAlternativeCost accepts it.
        var top = NewCardInLibrary(_alice, "Top", "{R}");

        var effects = LightUpTheStageFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var alt = new ExileCastAlternativeCost("LUTS grant", top.RuntimeExileCastCost!);
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

        var top = NewCardInLibrary(_alice, "Top", "{R}");

        var effects = LightUpTheStageFactory.BuildResolveEffect(_alice, bus);
        foreach (var e in effects) e.Execute();

        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // 1st Cleanup — Alice's current turn. Grant must persist.
        bus.Publish(new Majik.Core.Events.StepStartedEvent(PhaseStateType.Cleanup, _alice));
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "first cleanup belongs to caster's current turn — grant persists through EOT");

        // A cleanup on Bob's intervening turn does NOT count (e.Player !=
        // caster). Grant still alive.
        bus.Publish(new Majik.Core.Events.StepStartedEvent(PhaseStateType.Cleanup, _bob));
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Bob's cleanup is not 'your next turn' — Alice's grant survives");

        // 2nd Cleanup belonging to Alice — her next turn. Grant clears.
        bus.Publish(new Majik.Core.Events.StepStartedEvent(PhaseStateType.Cleanup, _alice));
        top.RuntimeExileCastAllowedCaster.Should().BeNull(
            "second cleanup belonging to caster = end of caster's next turn — grant cleared");
    }

    [Fact]
    public void EotCleanup_NoBus_GrantPersistsIndefinitely()
    {
        // No bus → no scheduling. Test path must manually clear if desired.
        var top = NewCardInLibrary(_alice, "Top", "{R}");

        var effects = LightUpTheStageFactory.BuildResolveEffect(_alice, eventBus: null);
        foreach (var e in effects) e.Execute();

        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------
    // Spectacle alt cost
    // -------------------------------------------------------------------

    [Fact]
    public void Spectacle_OpponentLostLifeThisTurn_BindsCostR()
    {
        _bob.LoseLife(1);
        _bob.LifeLostThisTurn.Should().Be(1);

        var cost = LightUpTheStageFactory.BuildSpectacleCost(_alice, new[] { _alice, _bob });

        cost.Should().NotBeNull();
        cost!.AlternativeManaCost.Red.Should().Be(1, "Spectacle cost is {R}");
        cost.AlternativeManaCost.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Spectacle_NoOpponentLostLifeThisTurn_ReturnsNull()
    {
        // Nobody has lost life this turn.
        _bob.LifeLostThisTurn.Should().Be(0);

        var cost = LightUpTheStageFactory.BuildSpectacleCost(_alice, new[] { _alice, _bob });

        cost.Should().BeNull("Spectacle alt cost is illegal until an opponent loses life");
    }

    [Fact]
    public void Spectacle_CasterLostLife_DoesNotEnableSpectacle()
    {
        // Caster's own life loss does NOT enable Spectacle (CR 702.118a).
        _alice.LoseLife(3);

        var cost = LightUpTheStageFactory.BuildSpectacleCost(_alice, new[] { _alice, _bob });

        cost.Should().BeNull("Spectacle keys on an OPPONENT losing life, not the caster");
    }
}

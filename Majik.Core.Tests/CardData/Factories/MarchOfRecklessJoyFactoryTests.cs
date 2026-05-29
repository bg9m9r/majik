using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MarchOfRecklessJoyFactory"/>
/// (Kamigawa: Neon Dynasty, {X}{R}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, you may exile any number
///    of red cards from your hand. This spell costs {2} less to cast for
///    each card exiled this way.
///    Exile the top X cards of your library. You may play up to two of
///    those cards until the end of your next turn."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - BuildAdditionalCost: red MarchAdditionalCost wiring.
///   - BuildSpellDefinition: HasVariableX=true, no target requests.
///   - Resolve X=3: exiles top 3, grants only first 2 (cap enforced).
///   - Resolve X=1: exiles 1, grants 1 (under cap — all receive grant).
///   - Resolve X=0: no-op.
///   - Shallow library: exiles what is available; grants up to 2 of those.
///   - Empty library: no throw, no grants.
///   - ExileCastAlternativeCost.CanCastFor: true for caster, false for other.
///   - EOT cleanup: first caster Cleanup keeps grants; second clears.
///   - EOT cleanup: non-caster Cleanup does not count.
///   - "Play up to 2" cap: 3rd-exiled card has NO grant (cannot be played).
/// </summary>
public class MarchOfRecklessJoyFactoryTests
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

    private static ChosenSpellParams XParam(int x) =>
        new(ModeIndex: null, X: x, Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    // ── identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ShipsInstantShape_XR_Red()
    {
        var march = MarchOfRecklessJoyFactory.Create(_alice);

        march.Should().BeOfType<Instant>();
        march.Name.Should().Be("March of Reckless Joy");
        march.ManaCost.Should().Be("{X}{R}");
        march.HasType(CardType.Instant).Should().BeTrue();
        march.Owner.Should().BeSameAs(_alice);
        march.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(march).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsInstantShape()
    {
        var dispatched = NamedCardFactory.Create("March of Reckless Joy", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("March of Reckless Joy");
        dispatched.ManaCost.Should().Be("{X}{R}");
    }

    // ── SpellDefinition shape ───────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasVariableX_NoTargetRequests()
    {
        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().BeEmpty("March of Reckless Joy targets nothing");
    }

    // ── additional cost helper ──────────────────────────────────────────────

    [Fact]
    public void BuildAdditionalCost_WiresRedMarchCost()
    {
        var spell = MarchOfRecklessJoyFactory.Create(_alice);
        var redCard = new Creature("Red Helper", "{1}{R}", 1, 1);
        redCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(redCard);
        redCard.SetZone(ZoneType.Hand);

        var cost = MarchOfRecklessJoyFactory.BuildAdditionalCost(spell, new ICard[] { redCard });

        cost.Should().BeOfType<MarchAdditionalCost>();
        cost.RequiredColor.Should().Be(ManaColor.Red);
        cost.ExiledCount.Should().Be(1);
        cost.ReductionAmount.Should().Be(2);
        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void BuildAdditionalCost_EmptyList_IsLegal_NoReduction()
    {
        var spell = MarchOfRecklessJoyFactory.Create(_alice);

        var cost = MarchOfRecklessJoyFactory.BuildAdditionalCost(spell, Array.Empty<ICard>());

        cost.ExiledCount.Should().Be(0);
        cost.ReductionAmount.Should().Be(0);
        cost.CanPay(_alice).Should().BeTrue("March is OPTIONAL — zero exiles is legal");
    }

    // ── resolve — exile top X ───────────────────────────────────────────────

    [Fact]
    public void Resolve_X3_ExilesTopThree_GrantsOnlyFirstTwo()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{2}{R}");
        var top4 = NewCardInLibrary(_alice, "Top4", "{G}");  // beyond X

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(XParam(3))) e.Execute();

        // All three exiled; top4 stays in library.
        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2, top3 });
        _alice.Zones.Exile.GetCards().Should().NotContain(top4);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { top4 });

        // First two receive grants.
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "first exiled card receives play-from-exile grant");
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "second exiled card receives play-from-exile grant");

        // Third card is exiled but has NO grant — "up to 2" cap.
        top3.RuntimeExileCastAllowedCaster.Should().BeNull(
            "third exiled card exceeds the cap of 2 — no grant (CR 118.9)");
    }

    [Fact]
    public void Resolve_X1_ExilesOne_GrantsOne()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(XParam(1))) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(top1);
        _alice.Zones.Exile.GetCards().Should().NotContain(top2);
        _alice.Zones.Library.GetCards().Should().Contain(top2);
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_X2_ExilesTwo_BothGranted()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{2}{R}");

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(XParam(2))) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { top3 });
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_X0_NoCards_NoGrants_NoThrow()
    {
        NewCardInLibrary(_alice, "Top1", "{R}");

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);
        var act = () => { foreach (var e in def.EffectFactory(XParam(0))) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Exile.GetCards().Should().BeEmpty("X=0 exiles nothing");
    }

    [Fact]
    public void Resolve_ShallowLibrary_ExilesAvailable_GrantsUpToCap()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        // Library has only 1 card; X=5.

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(XParam(5))) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(top1);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "only card exiled is within the 2-card cap — it gets a grant");
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoGrants_NoThrow()
    {
        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);
        var act = () => { foreach (var e in def.EffectFactory(XParam(3))) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // ── ExileCastAlternativeCost integration ────────────────────────────────

    [Fact]
    public void GrantedCard_CanCastFor_Caster_True_OtherPlayer_False()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(XParam(1))) e.Execute();

        var alt = new ExileCastAlternativeCost("march grant", top1.RuntimeExileCastCost!);
        alt.CanCastFor(top1, _alice).Should().BeTrue("alice is the granted caster");
        alt.CanCastFor(top1, _bob).Should().BeFalse("bob is not the granted caster");
    }

    [Fact]
    public void ThirdExiledCard_CannotBePlayedByGrantCheck()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{2}{R}");

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(XParam(3))) e.Execute();

        // top3 has no grant — CanCastFor must return false for any player.
        top3.RuntimeExileCastAllowedCaster.Should().BeNull();
        var noAlt = top3.RuntimeExileCastCost;
        noAlt.Should().BeNull("third card has no exile-cast grant at all");
    }

    // ── EOT cleanup via Cleanup-step counting ───────────────────────────────

    [Fact]
    public void EotCleanup_FirstCleanupKeepsGrants_SecondClears()
    {
        var bus = new EventBus();
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice, bus);
        foreach (var e in def.EffectFactory(XParam(2))) e.Execute();

        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // 1st Cleanup — caster's current turn. Grant must persist.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "first cleanup belongs to caster's current turn — grant persists");
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "first cleanup belongs to caster's current turn — grant persists");

        // Non-caster Cleanup does not count.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _bob));
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Bob's cleanup is not caster's next turn — grant survives");

        // 2nd Cleanup belonging to Alice — her next turn. Grant clears.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        top1.RuntimeExileCastAllowedCaster.Should().BeNull(
            "second cleanup belonging to caster = end of caster's next turn — grant cleared");
        top2.RuntimeExileCastAllowedCaster.Should().BeNull(
            "second cleanup belonging to caster = end of caster's next turn — grant cleared");
    }

    [Fact]
    public void EotCleanup_X3_OnlyFirstTwo_AreCleared()
    {
        var bus = new EventBus();
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");
        var top3 = NewCardInLibrary(_alice, "Top3", "{2}{R}");

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice, bus);
        foreach (var e in def.EffectFactory(XParam(3))) e.Execute();

        // top3 never got a grant.
        top3.RuntimeExileCastAllowedCaster.Should().BeNull();

        // Fire both cleanups.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        top1.RuntimeExileCastAllowedCaster.Should().BeNull("grant cleared after next-turn cleanup");
        top2.RuntimeExileCastAllowedCaster.Should().BeNull("grant cleared after next-turn cleanup");
        top3.RuntimeExileCastAllowedCaster.Should().BeNull("top3 was never granted");
    }

    [Fact]
    public void EotCleanup_NoBus_GrantsPersistIndefinitely()
    {
        var top1 = NewCardInLibrary(_alice, "Top1", "{R}");
        var top2 = NewCardInLibrary(_alice, "Top2", "{1}{R}");

        var def = MarchOfRecklessJoyFactory.BuildSpellDefinition(_alice, eventBus: null);
        foreach (var e in def.EffectFactory(XParam(2))) e.Execute();

        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "without event bus, grants persist until manually cleared (test pattern)");
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }
}

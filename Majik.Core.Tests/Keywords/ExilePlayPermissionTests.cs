using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Unit tests for <see cref="ExilePlayPermission"/> — the reusable
/// "temporary play-this-exiled-card permission with an expiry moment"
/// primitive (CR 118.9 / 514.2). Centralizes the EOT-expiry bookkeeping the
/// impulse-draw family (Reckless Impulse, Light Up the Stage, March of
/// Reckless Joy) + Harnfel previously duplicated inline.
/// </summary>
[Trait("Color", "R")]
public class ExilePlayPermissionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewExiled(Player owner, string name = "Top", string cost = "{1}{R}")
    {
        var c = new Card(name, cost);
        c.SetOwner(owner);
        owner.Zones.Exile.AddCard(c);
        c.SetZone(ZoneType.Exile);
        return c;
    }

    // ── grant stamps the runtime permission ─────────────────────────────────

    [Fact]
    public void GrantUntil_StampsRuntimeExileCast_ForCaster()
    {
        var card = NewExiled(_alice);

        ExilePlayPermission.GrantUntil(
            card, _alice, card.ManaCostValue, ExilePlayExpiry.EndOfTurn);

        card.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        card.RuntimeExileCastCost.Should().Be(card.ManaCostValue);

        // The same stamp ExileCastAlternativeCost consults — caster may cast,
        // nobody else.
        var alt = new ExileCastAlternativeCost("impulse", card.RuntimeExileCastCost!);
        alt.CanCastFor(card, _alice).Should().BeTrue();
        alt.CanCastFor(card, _bob).Should().BeFalse();
    }

    // ── EndOfTurn ("this turn") expiry — Harnfel ────────────────────────────

    [Fact]
    public void GrantUntil_EndOfTurn_ClearsOnFirstCasterCleanup()
    {
        var bus = new EventBus();
        var card = NewExiled(_alice);

        ExilePlayPermission.GrantUntil(
            card, _alice, card.ManaCostValue, ExilePlayExpiry.EndOfTurn, bus);

        // A non-caster cleanup must NOT expire a "this turn" grant.
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _bob));
        card.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Bob's cleanup is not the caster's cleanup — grant survives");

        // First cleanup the CASTER owns ends "this turn" — grant clears.
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
        card.RuntimeExileCastAllowedCaster.Should().BeNull(
            "first caster cleanup = end of this turn — grant revoked (CR 514.2)");
    }

    // ── EndOfYourNextTurn expiry — Reckless Impulse family ──────────────────

    [Fact]
    public void GrantUntil_EndOfYourNextTurn_SurvivesFirstCleanup_ClearsOnSecond()
    {
        var bus = new EventBus();
        var card = NewExiled(_alice);

        ExilePlayPermission.GrantUntil(
            card, _alice, card.ManaCostValue, ExilePlayExpiry.EndOfYourNextTurn, bus);

        // First caster cleanup = THIS turn — grant must survive.
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
        card.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "first caster cleanup belongs to this turn — grant persists");

        // An interleaved opponent turn cleanup does not count.
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _bob));
        card.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // Second caster cleanup = caster's NEXT turn — grant clears.
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
        card.RuntimeExileCastAllowedCaster.Should().BeNull(
            "second caster cleanup = end of caster's next turn — grant revoked");
    }

    // ── no bus = persists (test path) ───────────────────────────────────────

    [Fact]
    public void GrantUntil_NoBus_PersistsUntilManuallyCleared()
    {
        var card = NewExiled(_alice);

        var revoke = ExilePlayPermission.GrantUntil(
            card, _alice, card.ManaCostValue, ExilePlayExpiry.EndOfTurn, eventBus: null);

        card.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "without a bus the grant lingers until the returned revoke runs");

        revoke();
        card.RuntimeExileCastAllowedCaster.Should().BeNull(
            "the returned revoke action clears the per-card stamp");
    }

    // ── shared revocation for many cards under one window (Harnfel) ──────────

    [Fact]
    public void ScheduleRevocation_SharedWindow_ClearsAllCardsAtOnce()
    {
        var bus = new EventBus();
        var c1 = NewExiled(_alice, "A");
        var c2 = NewExiled(_alice, "B");

        c1.GrantRuntimeExileCast(_alice, c1.ManaCostValue);
        c2.GrantRuntimeExileCast(_alice, c2.ManaCostValue);

        ExilePlayPermission.ScheduleRevocation(
            _alice, ExilePlayExpiry.EndOfTurn, bus,
            () => { c1.ClearRuntimeExileCast(); c2.ClearRuntimeExileCast(); });

        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));

        c1.RuntimeExileCastAllowedCaster.Should().BeNull();
        c2.RuntimeExileCastAllowedCaster.Should().BeNull(
            "one shared subscription revokes every card stamped under the window");
    }

    [Fact]
    public void ScheduleRevocation_NoBus_IsNoOp()
    {
        var card = NewExiled(_alice);
        card.GrantRuntimeExileCast(_alice, card.ManaCostValue);

        var act = () => ExilePlayPermission.ScheduleRevocation(
            _alice, ExilePlayExpiry.EndOfTurn, eventBus: null,
            () => card.ClearRuntimeExileCast());

        act.Should().NotThrow();
        card.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "no bus = nothing scheduled; grant untouched");
    }

    // ── land-play half of CR 305.2 (Harnfel) ────────────────────────────────

    private static Land NewExiledLand(Player owner, string name = "Forest")
    {
        var land = new Land(name);
        land.SetOwner(owner);
        owner.Zones.Exile.AddCard(land);
        land.SetZone(ZoneType.Exile);
        return land;
    }

    [Fact]
    public void GrantUntil_OnExiledLand_StampsLandPlayPermission()
    {
        // CR 305.2 / 601.1 — a LAND in a "you may play those cards this turn"
        // exile pile is PLAYED, not cast: it needs the land-play grant, not the
        // spell-cast grant (a cast grant never makes a land a legal play source).
        var land = NewExiledLand(_alice);

        ExilePlayPermission.GrantUntil(
            land, _alice, land.ManaCostValue, ExilePlayExpiry.EndOfTurn);

        land.RuntimeExileLandPlayAllowedPlayer.Should().BeSameAs(_alice,
            "an exiled land receives the land-play half of the play permission");

        ExilePlayPermission.PlayableLandsFromExile(_alice)
            .Should().ContainSingle().Which.Should().BeSameAs(land);
        ExilePlayPermission.PlayableLandsFromExile(_bob)
            .Should().BeEmpty("the grant nominates Alice only");
    }

    [Fact]
    public void GrantUntil_OnExiledLand_EndOfTurn_RevokesLandPlayPermission()
    {
        var bus = new EventBus();
        var land = NewExiledLand(_alice);

        ExilePlayPermission.GrantUntil(
            land, _alice, land.ManaCostValue, ExilePlayExpiry.EndOfTurn, bus);

        land.RuntimeExileLandPlayAllowedPlayer.Should().BeSameAs(_alice);

        // First cleanup the player owns ends "this turn" — both halves clear.
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
        land.RuntimeExileLandPlayAllowedPlayer.Should().BeNull(
            "the land-play grant expires with the rest of the permission (CR 514.2)");
        ExilePlayPermission.PlayableLandsFromExile(_alice).Should().BeEmpty();
    }

    [Fact]
    public void GrantUntil_OnExiledNonland_DoesNotStampLandPlayPermission()
    {
        // A nonland card under the same permission is castable (the cast half),
        // never land-playable — PlayableLandsFromExile must not surface it.
        var card = NewExiled(_alice);

        ExilePlayPermission.GrantUntil(
            card, _alice, card.ManaCostValue, ExilePlayExpiry.EndOfTurn);

        card.RuntimeExileLandPlayAllowedPlayer.Should().BeNull();
        ExilePlayPermission.PlayableLandsFromExile(_alice).Should().BeEmpty();
    }
}

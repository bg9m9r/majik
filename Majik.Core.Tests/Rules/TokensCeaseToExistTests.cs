using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// CR 111.7 / 704.5d — a token in any zone other than the battlefield
/// ceases to exist. This is a state-based action. A dying/leaving token
/// may MOMENTARILY exist in its destination zone so that "dies" /
/// "leaves the battlefield" triggers (and any "whenever a creature dies"
/// watchers) can fire off the captured reference; the very next SBA check
/// removes it from that zone entirely.
///
/// These tests drive the LIVE call shape: the priority loop / combat pass
/// only ever hands <see cref="StateBasedActions.CheckStateBasedActions"/>
/// the cards currently on the battlefield. A token that just died is in
/// the graveyard, so it is NOT in that list — the check must therefore
/// scan players' non-battlefield zones itself, not rely on the caller's
/// card list.
/// </summary>
public class TokensCeaseToExistTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zoneService;
    private readonly Player _alice = new("Alice", 20);

    public TokensCeaseToExistTests()
    {
        _zoneService = new ZoneService(_bus);
    }

    /// <summary>Materializes the live "battlefield-only" card list the priority
    /// loop / combat code passes — see TurnDriver / GameDriver / CombatFlow.</summary>
    private static System.Collections.Generic.List<ICard> BattlefieldCards(params Player[] players)
        => players.SelectMany(p => p.Zones.Battlefield.GetCards()).ToList();

    private Creature MakeTokenOnBattlefield(string name = "Goblin")
    {
        var token = new Creature(name, "", 1, 1) { Owner = _alice, Controller = _alice };
        token.MarkAsToken();
        // Put it on the battlefield zone LIST (AddCard also stamps .Zone) so the
        // SBA check's zone.ContainsCard(token) membership query is meaningful.
        _alice.Zones.Battlefield.AddCard(token);
        return token;
    }

    [Fact]
    public void DeadToken_CeasesToExist_AfterSba_DrivenByBattlefieldOnlyCardList()
    {
        var sba = new StateBasedActions(_bus, _zoneService);
        var token = MakeTokenOnBattlefield();

        // Token dies through the normal zone-move path (event fires here).
        _zoneService.MoveCardTo(token, ZoneType.Graveyard);
        _alice.Zones.Graveyard.ContainsCard(token).Should().BeTrue(
            "the token momentarily exists in the graveyard so dies/LTB triggers can see it");

        // Live call shape: only battlefield cards are passed in.
        sba.CheckStateBasedActions(new[] { _alice }, BattlefieldCards(_alice));

        _alice.Zones.Graveyard.ContainsCard(token).Should().BeFalse(
            "CR 704.5d — a token in a non-battlefield zone ceases to exist");
    }

    [Fact]
    public void TokenInExile_CeasesToExist_AfterSba()
    {
        var sba = new StateBasedActions(_bus, _zoneService);
        var token = MakeTokenOnBattlefield();
        _zoneService.MoveCardTo(token, ZoneType.Exile);

        sba.CheckStateBasedActions(new[] { _alice }, BattlefieldCards(_alice));

        _alice.Zones.Exile.ContainsCard(token).Should().BeFalse();
    }

    [Fact]
    public void TokenInHand_CeasesToExist_AfterSba()
    {
        var sba = new StateBasedActions(_bus, _zoneService);
        var token = MakeTokenOnBattlefield();
        _zoneService.MoveCardTo(token, ZoneType.Hand);

        sba.CheckStateBasedActions(new[] { _alice }, BattlefieldCards(_alice));

        _alice.Zones.Hand.ContainsCard(token).Should().BeFalse();
    }

    [Fact]
    public void TokenInLibrary_CeasesToExist_AfterSba()
    {
        var sba = new StateBasedActions(_bus, _zoneService);
        var token = MakeTokenOnBattlefield();
        _zoneService.MoveCardTo(token, ZoneType.Library);

        sba.CheckStateBasedActions(new[] { _alice }, BattlefieldCards(_alice));

        _alice.Zones.Library.ContainsCard(token).Should().BeFalse();
    }

    [Fact]
    public void TokenOnBattlefield_IsUnaffected()
    {
        var sba = new StateBasedActions(_bus, _zoneService);
        var token = MakeTokenOnBattlefield();

        sba.CheckStateBasedActions(new[] { _alice }, BattlefieldCards(_alice));

        _alice.Zones.Battlefield.ContainsCard(token).Should().BeTrue(
            "a token on the battlefield is a normal permanent");
    }

    [Fact]
    public void NonTokenInGraveyard_IsUnaffected()
    {
        var sba = new StateBasedActions(_bus, _zoneService);
        var real = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(real);
        _zoneService.MoveCardTo(real, ZoneType.Graveyard);

        sba.CheckStateBasedActions(new[] { _alice }, BattlefieldCards(_alice));

        _alice.Zones.Graveyard.ContainsCard(real).Should().BeTrue(
            "only tokens cease to exist outside the battlefield");
    }

    [Fact]
    public void DiesTrigger_FromToken_StillFires_BeforeTokenCeasesToExist()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var sba = new StateBasedActions(_bus, _zoneService, triggerManager: triggers);

        var token = MakeTokenOnBattlefield();

        // "When this creature dies, ..." trigger on the token itself.
        var selfDies = new TriggeredAbility(
            source: token,
            controller: _alice,
            condition: Triggers.OnDies(token),
            effects: new[] { Fx.Inline("noop", () => { }) },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
        triggers.RegisterTriggeredAbility(selfDies);

        // Separate "whenever a creature you control dies" observer. Records the
        // dying card's reference at queue time to prove the watcher saw the token.
        ICard? observedDeadCard = null;
        var observer = new Creature("Blood Artist", "1B", 0, 1) { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(observer);
        var watcher = new TriggeredAbility(
            source: observer,
            controller: _alice,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                var dies = e.FromZone == ZoneType.Battlefield
                    && e.ToZone == ZoneType.Graveyard
                    && e.Card.HasType(CardType.Creature);
                if (dies) observedDeadCard = e.Card;
                return dies;
            }),
            effects: new[] { Fx.Inline("observe", () => { }) },
            activeZones: new[] { ZoneType.Battlefield });
        triggers.RegisterTriggeredAbility(watcher);

        // Token dies via the normal zone-move path — both triggers queue here,
        // referencing the token while it momentarily sits in the graveyard.
        _zoneService.MoveCardTo(token, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(2,
            "both the token's own dies-trigger and the death-watcher must queue on the move");
        observedDeadCard.Should().BeSameAs(token,
            "the death-watcher observed the token's death while it was in the graveyard");

        // SBA now removes the token; the queued triggers are untouched.
        sba.CheckStateBasedActions(new[] { _alice }, BattlefieldCards(_alice));

        _alice.Zones.Graveyard.ContainsCard(token).Should().BeFalse(
            "after triggers queued, the token ceases to exist (CR 704.5d)");
        triggers.PendingCount.Should().Be(2,
            "removing the token does not retract the already-queued dies triggers");
    }
}

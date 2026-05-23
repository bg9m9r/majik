using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// Closes the gap noted in PR #165 — <see cref="OracleSpellBinder.BindCompiled"/>
/// must thread <see cref="ZoneService"/> (and the other runtime managers)
/// into the <see cref="SpellBindContext"/> it constructs, so persisted
/// compiled-binding rows for zone-moving templates (e.g.
/// <c>ReanimateToBattlefieldTemplate</c>) get the same ETB-trigger-firing
/// path as the live binder.
///
/// Pre-fix: the compiled-bind ctx had <c>Zones == null</c>, the reanimate
/// effect hit its direct-zone-mutation fallback, no
/// <see cref="CardMovedEvent"/> was published, no ETB trigger queued.
/// </summary>
public class BindCompiledZonesThreadingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly ZoneService _zones;

    public BindCompiledZonesThreadingTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _zones = new ZoneService(eventBus: _bus);

        for (var i = 0; i < 5; i++)
        {
            var stock = new Creature($"Stock-{i}", "1", 1, 1) { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(stock);
        }
    }

    private Creature BuildEtbDrawerInGraveyard()
    {
        var card = new Creature("ETB Drawer", "2B", 2, 2) { Owner = _alice, Zone = ZoneType.Graveyard };
        var ability = new TriggeredAbility(
            source: card,
            controller: _alice,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[]
            {
                new Effect("etb-draw", () =>
                {
                    var top = _alice.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null) return;
                    _alice.Zones.Library.RemoveCard(top);
                    _alice.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }),
            });
        card.AddAbility(ability);
        _alice.Zones.Graveyard.AddCard(card);
        _triggers.BindCard(card);
        return card;
    }

    [Fact]
    public void BindCompiled_ReanimateToBattlefield_WithZones_RoutesThroughZoneService_AndEtbTriggerFires()
    {
        // Capture every CardMovedEvent on the bus.
        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var card = BuildEtbDrawerInGraveyard();
        var entity = new CardEntity
        {
            Name = "Reanimate",
            OracleText = "Return target creature card from your graveyard to the battlefield.",
        };

        // Compiled fast path — params dictionary as the offline pipeline
        // would persist for ReanimateToBattlefieldTemplate.
        var def = OracleSpellBinder.BindCompiled(
            templateName: "ReanimateToBattlefield",
            paramsJson: "{\"kind\":\"creature\"}",
            entity,
            _alice,
            resolver: o => o,
            effects: null,
            stack: _stack,
            replacements: null,
            triggers: _triggers,
            eventBus: _bus,
            zones: _zones);

        def.Should().NotBeNull("BindCompiled must thread ZoneService so the reanimate template can use it");

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { card } },
            Mana: ManaPayment.Empty);

        var handBefore = _alice.Zones.Hand.Count;
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        // ZoneService routed the move.
        card.Zone.Should().Be(ZoneType.Battlefield);
        card.Controller.Should().BeSameAs(_alice);
        _alice.Zones.Battlefield.GetCards().Should().Contain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);

        var move = movedEvents.SingleOrDefault(e => ReferenceEquals(e.Card, card));
        move.Should().NotBeNull(
            "ZoneService must publish CardMovedEvent so triggers can observe — " +
            "this is precisely the path that pre-fix BindCompiled skipped");
        move!.FromZone.Should().Be(ZoneType.Graveyard);
        move.ToZone.Should().Be(ZoneType.Battlefield);

        // ETB trigger queued and resolves to a card draw.
        _triggers.PendingCount.Should().Be(1);
        _triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        _stack.Count.Should().Be(1);
        while (!_stack.IsEmpty)
        {
            var ability = (TriggeredAbility)_stack.Pop()!;
            ability.Resolve();
        }
        _alice.Zones.Hand.Count.Should().Be(handBefore + 1);
    }

    [Fact]
    public void BindCompiled_ReanimateToBattlefield_WithoutZones_StillResolves_NoEtbTrigger()
    {
        // Regression: callers that build BindCompiled without a ZoneService
        // (legacy / vanilla cast contexts) must still get a runnable spell.
        // The effect falls back to direct zone mutation — no CardMovedEvent,
        // so no ETB trigger fires. This documents the fallback behavior.
        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var card = BuildEtbDrawerInGraveyard();
        var entity = new CardEntity
        {
            Name = "Reanimate",
            OracleText = "Return target creature card from your graveyard to the battlefield.",
        };

        var def = OracleSpellBinder.BindCompiled(
            templateName: "ReanimateToBattlefield",
            paramsJson: "{\"kind\":\"creature\"}",
            entity,
            _alice,
            resolver: o => o,
            effects: null,
            stack: null);

        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { card } },
            Mana: ManaPayment.Empty);

        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        card.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Where(e => ReferenceEquals(e.Card, card)).Should().BeEmpty(
            "fallback path mutates zones directly and does not publish CardMovedEvent");
        _triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void BindCompiled_NonZoneMovingTemplate_WithZonesThreaded_StillResolves()
    {
        // Regression sanity: threading ZoneService through BindCompiled must
        // not break templates that don't care about zones (Lightning Bolt
        // shape — pure damage).
        var entity = new CardEntity
        {
            Name = "Lightning Bolt",
            OracleText = "Lightning Bolt deals 3 damage to any target.",
        };

        var def = OracleSpellBinder.BindCompiled(
            templateName: "DamageAnyTarget",
            paramsJson: "{\"n\":\"3\"}",
            entity,
            _alice,
            resolver: o => o,
            effects: null,
            stack: _stack,
            replacements: null,
            triggers: _triggers,
            eventBus: _bus,
            zones: _zones);

        def.Should().NotBeNull();

        var bob = new Player("Bob", 20);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { bob } },
            Mana: ManaPayment.Empty);

        Action resolve = () =>
        {
            foreach (var e in def!.EffectFactory(chosen)) e.Execute();
        };

        resolve.Should().NotThrow();
        bob.LifeTotal.Should().Be(17);
    }
}

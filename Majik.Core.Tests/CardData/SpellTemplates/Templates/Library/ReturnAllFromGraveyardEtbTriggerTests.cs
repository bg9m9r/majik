using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Library;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// CR 603.6a — a "When ~ enters" trigger fires whenever the source moves
/// to the battlefield, regardless of source zone. The mass-reanimation
/// template (<see cref="ReturnAllFromGraveyardTemplate"/>, Living-Death /
/// Replenish shape) must therefore fire each reanimated permanent's ETB
/// trigger exactly once.
///
/// Prior to this fix, <c>ReturnAllFromGraveyardSpell</c> mutated zones
/// directly in a loop (<c>caster.Zones.Battlefield.AddCard</c>) and
/// published no <see cref="CardMovedEvent"/>, so the
/// <see cref="TriggerManager"/> never observed the moves and ETB
/// abilities never queued — the same bug PR #165 fixed for the single-
/// target reanimate path, but on the mass path.
/// </summary>
public class ReturnAllFromGraveyardEtbTriggerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly ZoneService _zones;

    public ReturnAllFromGraveyardEtbTriggerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _zones = new ZoneService(eventBus: _bus);

        // Stock the library so two ETB "draw a card" triggers each have
        // something to pull. The library content is irrelevant — only the
        // count matters for the hand-size delta assertions.
        for (var i = 0; i < 5; i++)
        {
            var stock = new Creature($"Stock-{i}", "1", 1, 1) { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(stock);
        }
    }

    /// <summary>
    /// Build a creature with a self-ETB trigger that draws one card on
    /// resolution. Mirrors the contract of "When ~ enters, draw a card."
    /// </summary>
    private Creature AddEtbDrawerToGraveyard(string name)
    {
        var card = new Creature(name, "2B", 2, 2) { Owner = _alice, Zone = ZoneType.Graveyard };
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
        // BindCard so the manager auto-registers the trigger when the
        // card enters the battlefield (Rule 603.6a active-zones sync).
        _triggers.BindCard(card);
        return card;
    }

    private static SpellBindContext CtxWithZones(Player caster, ZoneService zones) =>
        new(
            Entity: new CardEntity { Name = "Mass Reanimate", OracleText = "Return all creature cards from your graveyard to the battlefield." },
            Caster: caster,
            Resolver: o => o,
            Effects: null,
            Stack: null,
            Replacements: null,
            Triggers: null,
            EventBus: null,
            Zones: zones);

    [Fact]
    public void MassReanimate_RoutesEachMoveThroughZoneService_AndEveryEtbTriggerFires()
    {
        // Capture every CardMovedEvent on the bus so we can verify the
        // template published one move per reanimated card.
        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var first = AddEtbDrawerToGraveyard("ETB Drawer A");
        var second = AddEtbDrawerToGraveyard("ETB Drawer B");

        // Build the mass-reanimate spell via the template (the production
        // wiring) and drive its EffectFactory.
        var template = new ReturnAllFromGraveyardTemplate();
        var ctx = CtxWithZones(_alice, _zones);
        var def = template.Rehydrate(
            template.TryExtractParams(ctx.Text)!,
            ctx);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        var handBefore = _alice.Zones.Hand.Count;
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Both cards moved through ZoneService → battlefield, owned by Alice.
        first.Zone.Should().Be(ZoneType.Battlefield);
        second.Zone.Should().Be(ZoneType.Battlefield);
        first.Controller.Should().BeSameAs(_alice);
        second.Controller.Should().BeSameAs(_alice);
        _alice.Zones.Battlefield.GetCards().Should().Contain(new[] { (Majik.Core.Cards.ICard)first, second });
        _alice.Zones.Graveyard.GetCards().Should().NotContain(new[] { (Majik.Core.Cards.ICard)first, second });

        // Each canonical CardMovedEvent carries the right zones (CR 603.6a).
        var firstMove = movedEvents.SingleOrDefault(e => ReferenceEquals(e.Card, first));
        var secondMove = movedEvents.SingleOrDefault(e => ReferenceEquals(e.Card, second));
        firstMove.Should().NotBeNull("ZoneService must publish CardMovedEvent for each mass-reanimated card");
        secondMove.Should().NotBeNull("ZoneService must publish CardMovedEvent for each mass-reanimated card");
        firstMove!.FromZone.Should().Be(ZoneType.Graveyard);
        firstMove.ToZone.Should().Be(ZoneType.Battlefield);
        secondMove!.FromZone.Should().Be(ZoneType.Graveyard);
        secondMove.ToZone.Should().Be(ZoneType.Battlefield);

        // Each ETB trigger queued exactly once — one per reanimated permanent.
        _triggers.PendingCount.Should().Be(2);

        // Drain the queue and resolve — hand grows by exactly two cards
        // (one per ETB-draw trigger), confirming each fired exactly once.
        _triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        _stack.Count.Should().Be(2);
        while (!_stack.IsEmpty)
        {
            var ability = (TriggeredAbility)_stack.Pop()!;
            ability.Resolve();
        }
        _alice.Zones.Hand.Count.Should().Be(handBefore + 2);
    }

    [Fact]
    public void MassReanimate_EmptyGraveyard_IsNoOp_AndDoesNotThrow()
    {
        // Sanity regression: no cards to return → no moves, no events,
        // no triggers, no exceptions. Guards against an off-by-one or
        // unguarded snapshot-iteration regression on the empty path.
        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();

        var template = new ReturnAllFromGraveyardTemplate();
        var ctx = CtxWithZones(_alice, _zones);
        var def = template.Rehydrate(
            template.TryExtractParams(ctx.Text)!,
            ctx);

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        var resolve = () =>
        {
            foreach (var e in def.EffectFactory(chosen)) e.Execute();
        };

        resolve.Should().NotThrow();
        movedEvents.Should().BeEmpty();
        _triggers.PendingCount.Should().Be(0);
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}

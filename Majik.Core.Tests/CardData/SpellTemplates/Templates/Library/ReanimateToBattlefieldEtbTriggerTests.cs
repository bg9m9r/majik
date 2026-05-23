using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
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
/// to the battlefield, regardless of the source zone. Reanimation
/// (graveyard → battlefield) must therefore fire the trigger.
///
/// Prior to the fix, <c>ReanimateToBattlefieldSpell</c> mutated zones
/// directly (<c>caster.Zones.Battlefield.AddCard</c>) and published no
/// <see cref="CardMovedEvent"/>, so the <see cref="TriggerManager"/>
/// never observed the move and ETB abilities never queued.
/// </summary>
public class ReanimateToBattlefieldEtbTriggerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly ZoneService _zones;

    public ReanimateToBattlefieldEtbTriggerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _zones = new ZoneService(eventBus: _bus);

        // Stock the library so an ETB "draw a card" trigger has something
        // to pull. The library content is irrelevant — only the count
        // matters for the assertions.
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
        // BindCard so the manager auto-registers the trigger when the
        // card enters the battlefield (Rule 603.6a active-zones sync).
        _triggers.BindCard(card);
        return card;
    }

    private static SpellBindContext CtxWithZones(Player caster, ZoneService zones) =>
        new(
            Entity: new CardEntity { Name = "Reanimate", OracleText = "Return target creature card from your graveyard to the battlefield." },
            Caster: caster,
            Resolver: o => o,
            Effects: null,
            Stack: null,
            Replacements: null,
            Triggers: null,
            EventBus: null,
            Zones: zones);

    [Fact]
    public void Reanimate_RoutesThroughZoneService_PublishesCardMovedEvent_AndEtbTriggerFires()
    {
        // Capture every CardMovedEvent on the bus.
        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var card = BuildEtbDrawerInGraveyard();

        // Build the reanimate spell via the template (the production
        // wiring) and drive its EffectFactory with the chosen target.
        var template = new ReanimateToBattlefieldTemplate();
        var ctx = CtxWithZones(_alice, _zones);
        var def = template.Rehydrate(
            template.TryExtractParams(ctx.Text)!,
            ctx);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { card } },
            Mana: ManaPayment.Empty);

        var handBefore = _alice.Zones.Hand.Count;
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Card moved through ZoneService → battlefield, owned by Alice.
        card.Zone.Should().Be(ZoneType.Battlefield);
        card.Controller.Should().BeSameAs(_alice);
        _alice.Zones.Battlefield.GetCards().Should().Contain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);

        // The canonical CardMovedEvent carries the right zones (CR 603.6a).
        var move = movedEvents.SingleOrDefault(e => ReferenceEquals(e.Card, card));
        move.Should().NotBeNull("ZoneService must publish CardMovedEvent so triggers can observe");
        move!.FromZone.Should().Be(ZoneType.Graveyard);
        move.ToZone.Should().Be(ZoneType.Battlefield);

        // The ETB trigger was queued exactly once.
        _triggers.PendingCount.Should().Be(1);

        // Drain the queue and resolve — hand grows by exactly one card.
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
    public void CastFromHand_EtbTriggerFires_Regression()
    {
        // Regression: the same ETB ability fires when the creature
        // enters from the canonical hand→battlefield path, so we can
        // confirm both paths produce the same observable result.
        var card = new Creature("ETB Drawer", "2B", 2, 2) { Owner = _alice, Zone = ZoneType.Hand };
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
        _alice.Zones.Hand.AddCard(card);
        _triggers.BindCard(card);

        var handBefore = _alice.Zones.Hand.Count;

        // Cast → resolve: ZoneService moves the card hand → battlefield.
        _zones.MoveCard(card, ZoneType.Hand, ZoneType.Battlefield, _alice);

        card.Zone.Should().Be(ZoneType.Battlefield);
        _triggers.PendingCount.Should().Be(1);

        _triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        while (!_stack.IsEmpty)
        {
            var queued = (TriggeredAbility)_stack.Pop()!;
            queued.Resolve();
        }

        // The hand started with the ETB Drawer (count=handBefore which
        // included the drawer), the drawer left to the battlefield (-1),
        // then the ETB drew a card (+1). Net: same size as before.
        _alice.Zones.Hand.Count.Should().Be(handBefore);
        _alice.Zones.Hand.GetCards().Should().NotContain(card);
    }
}

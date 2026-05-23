using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SneakAttackFactory"/>.
///
/// Sneak Attack — Enchantment {2}{R} (Urza's Saga):
///   "{R}: You may put a creature card from your hand onto the battlefield.
///    That creature gains haste. Sacrifice it at the beginning of the next
///    end step."
///
/// Covers:
///   - Card identity (name, enchantment type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Activated ability shape: single ActivatedAbility, ManaCostCost({R}),
///     no tap cost (repeatable).
///   - Activate: creature from hand → battlefield, gains Haste, summoning
///     sickness cleared, CardMovedEvent publishes.
///   - End step fires the delayed sac trigger → creature → graveyard.
///   - Multiple activations same turn: each cheated-in creature is
///     sacrificed at the next end step (one delayed trigger per activation).
/// </summary>
public class SneakAttackTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public SneakAttackTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SneakAttack_HasExpectedShape()
    {
        var card = SneakAttackFactory.Create(_alice);

        card.Name.Should().Be("Sneak Attack");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        // Single activated ability: {R}, no tap cost, no targets.
        card.Abilities.Should().ContainSingle();
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().ContainSingle();
        var manaCost = ability.Costs.OfType<ManaCostCost>().Single();
        manaCost.Cost.Red.Should().Be(1, "activation cost is exactly one red mana");
        manaCost.Cost.Generic.Should().Be(0);
        manaCost.Cost.White.Should().Be(0);
        manaCost.Cost.Blue.Should().Be(0);
        manaCost.Cost.Black.Should().Be(0);
        manaCost.Cost.Green.Should().Be(0);
        manaCost.Cost.HasX.Should().BeFalse();
        ability.Controller.Should().BeSameAs(_alice);
        ability.Source.Should().BeSameAs(card);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SneakAttack()
    {
        var card = NamedCardFactory.Create("Sneak Attack", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Sneak Attack");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    // Activate: creature from hand → battlefield + Haste
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_PutsCreatureFromHandToBattlefield_WithHaste()
    {
        var continuous = new ContinuousEffectsService();
        var emrakul = new Creature("Emrakul, the Aeons Torn", "{15}", 15, 15)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Hand,
            ActiveEffects = continuous,
            HasSummoningSickness = true,
        };
        _alice.Zones.Hand.AddCard(emrakul);

        var sneakAttack = SneakAttackFactory.Create(_alice, _zones, triggers: null);
        _alice.Zones.Battlefield.AddCard(sneakAttack);
        sneakAttack.SetZone(ZoneType.Battlefield);

        // Pre-conditions: in hand, no haste, sick.
        emrakul.Zone.Should().Be(ZoneType.Hand);
        CombatAbilities.HasHaste(emrakul).Should().BeFalse();
        emrakul.HasSummoningSickness.Should().BeTrue();

        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var ability = sneakAttack.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Creature is on the battlefield under Alice's control.
        emrakul.Zone.Should().Be(ZoneType.Battlefield,
            "Sneak Attack puts the picked creature onto the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(emrakul);
        _alice.Zones.Hand.GetCards().Should().NotContain(emrakul);
        emrakul.Controller.Should().BeSameAs(_alice);

        // Haste granted (CR 702.10 / Layer 6 keyword grant).
        CombatAbilities.HasHaste(emrakul).Should().BeTrue(
            "Sneak Attack grants Haste to the cheated-in creature");
        emrakul.HasSummoningSickness.Should().BeFalse(
            "Haste clears summoning sickness for attack-declaration (CR 702.10b)");

        // ZoneService routed the move — CardMovedEvent fired so ETB triggers
        // on the cheated-in creature can land (CR 603.6a).
        movedEvents.Should().Contain(
            e => ReferenceEquals(e.Card, emrakul)
                && e.FromZone == ZoneType.Hand
                && e.ToZone == ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Delayed end-step sacrifice
    // -----------------------------------------------------------------------

    [Fact]
    public void EndStep_SacrificesPlacedCreature()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var emrakul = new Creature("Emrakul, the Aeons Torn", "{15}", 15, 15)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Hand,
        };
        _alice.Zones.Hand.AddCard(emrakul);

        var sneakAttack = SneakAttackFactory.Create(_alice, _zones, triggers);
        _alice.Zones.Battlefield.AddCard(sneakAttack);
        sneakAttack.SetZone(ZoneType.Battlefield);

        var ability = sneakAttack.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        emrakul.Zone.Should().Be(ZoneType.Battlefield,
            "creature is on the battlefield before the end step");

        // Fire the next End step — the delayed trigger matches and queues
        // itself onto the stack.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        // Resolve everything on the stack — the delayed trigger fires its
        // sacrifice effect.
        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        emrakul.Zone.Should().Be(ZoneType.Graveyard,
            "CR 603.7 / CR 701.16 — delayed end-step sacrifice fires (battlefield → graveyard)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(emrakul);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(emrakul);
    }

    // -----------------------------------------------------------------------
    // Multiple activations same turn → each placed creature is sacrificed
    // -----------------------------------------------------------------------

    [Fact]
    public void MultipleActivations_SameTurn_EachCreatureGetsSacrificed()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        // Two fatties in hand. The activation's deterministic
        // first-creature-in-hand pick activates them in hand-order.
        var emrakul = new Creature("Emrakul, the Aeons Torn", "{15}", 15, 15)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Hand,
        };
        var griselbrand = new Creature("Griselbrand", "{4}{B}{B}{B}{B}", 7, 7)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Hand,
        };
        _alice.Zones.Hand.AddCard(emrakul);
        _alice.Zones.Hand.AddCard(griselbrand);

        var sneakAttack = SneakAttackFactory.Create(_alice, _zones, triggers);
        _alice.Zones.Battlefield.AddCard(sneakAttack);
        sneakAttack.SetZone(ZoneType.Battlefield);

        var ability = sneakAttack.Abilities.OfType<ActivatedAbility>().Single();

        // First activation: cheats in Emrakul.
        foreach (var e in ability.Effects) e.Execute();
        emrakul.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(emrakul);

        // Second activation in the same turn: Emrakul is no longer in hand,
        // so the deterministic first-creature pick now lands on Griselbrand.
        foreach (var e in ability.Effects) e.Execute();
        griselbrand.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(griselbrand);

        // Both creatures on the battlefield before the end step.
        _alice.Zones.Battlefield.GetCards().Should().Contain(new[] { emrakul, griselbrand });

        // Fire the next End step — both delayed triggers match.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        // Every activation registered its own delayed sac, so both
        // creatures land in the graveyard at end of turn.
        emrakul.Zone.Should().Be(ZoneType.Graveyard,
            "each Sneak Attack activation registers its own delayed end-step sacrifice");
        griselbrand.Zone.Should().Be(ZoneType.Graveyard,
            "multiple activations in a single turn each fire their delayed sacrifice independently");
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { emrakul, griselbrand });
        _alice.Zones.Battlefield.GetCards().Should().NotContain(emrakul);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(griselbrand);
    }

    // -----------------------------------------------------------------------
    // No creature in hand → clean no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_NoCreatureInHand_IsCleanNoOp()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        // Hand contains only a non-creature — no eligible target for the
        // put-from-hand action (CR 117.x — "you may" with no valid target).
        var bolt = new Instant("Lightning Bolt", "{R}")
        {
            Owner = _alice,
            Zone = ZoneType.Hand,
        };
        _alice.Zones.Hand.AddCard(bolt);

        var sneakAttack = SneakAttackFactory.Create(_alice, _zones, triggers);
        _alice.Zones.Battlefield.AddCard(sneakAttack);
        sneakAttack.SetZone(ZoneType.Battlefield);

        var ability = sneakAttack.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var e in ability.Effects) e.Execute(); };
        act.Should().NotThrow(
            "no creature card in hand → resolve is a clean no-op");

        // Non-creature stays in hand; nothing extra on the battlefield.
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(sneakAttack);

        // No delayed trigger should have been registered (nothing to sac).
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(0,
            "no creature placed → no delayed end-step sacrifice registered");
    }
}

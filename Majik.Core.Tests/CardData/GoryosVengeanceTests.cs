using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoryosVengeanceFactory"/>.
///
/// Goryo's Vengeance — Instant {1}{B} (Champions of Kamigawa):
///   "Return target legendary creature card from your graveyard to the
///    battlefield. That creature gains haste. Exile it at the beginning
///    of the next end step. Splice onto Arcane {2}{B}." (Splice deferred.)
/// </summary>
public class GoryosVengeanceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public GoryosVengeanceTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void GoryosVengeance_HasExpectedShape()
    {
        var card = GoryosVengeanceFactory.Create(_alice);

        card.Name.Should().Be("Goryo's Vengeance");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoryosVengeance()
    {
        var card = NamedCardFactory.Create("Goryo's Vengeance", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Goryo's Vengeance");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
    }

    [Fact]
    public void Resolve_ReanimatesLegendaryCreature_WithHaste()
    {
        // Legendary creature in Alice's graveyard.
        var continuous = new ContinuousEffectsService();
        var griselbrand = new Creature(
            name: "Griselbrand",
            manaCost: "{4}{B}{B}{B}{B}",
            power: 7,
            toughness: 7,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Demon })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Graveyard,
            ActiveEffects = continuous,
            HasSummoningSickness = true,
        };
        _alice.Zones.Graveyard.AddCard(griselbrand);

        var effect = GoryosVengeanceFactory
            .BuildResolveEffect(_alice, _zones, triggers: null)
            .Single();
        effect.Execute();

        griselbrand.Zone.Should().Be(ZoneType.Battlefield,
            "Goryo's Vengeance reanimates the legendary creature");
        _alice.Zones.Battlefield.GetCards().Should().Contain(griselbrand);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(griselbrand);
        griselbrand.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasHaste(griselbrand).Should().BeTrue(
            "Goryo's Vengeance grants Haste to the reanimated creature");
        griselbrand.HasSummoningSickness.Should().BeFalse(
            "Haste clears summoning sickness (CR 702.10b)");
    }

    [Fact]
    public void Resolve_SkipsNonLegendaryCreatureInGraveyard()
    {
        // Non-legendary creature is not a legal target.
        var ravager = new Creature("Arcbound Ravager", "{2}", 0, 0)
        {
            Owner = _alice,
            Zone = ZoneType.Graveyard,
        };
        _alice.Zones.Graveyard.AddCard(ravager);

        var effect = GoryosVengeanceFactory
            .BuildResolveEffect(_alice, _zones, triggers: null)
            .Single();

        var act = () => effect.Execute();
        act.Should().NotThrow(
            "non-legendary in graveyard → no legal target → clean no-op (CR 117.x)");

        ravager.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_RegistersDelayedEndStepExile_ForReanimatedCreature()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var emrakul = new Creature(
            name: "Emrakul, the Aeons Torn",
            manaCost: "{15}",
            power: 15,
            toughness: 15,
            supertypes: new[] { CardSupertype.Legendary })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Graveyard,
        };
        _alice.Zones.Graveyard.AddCard(emrakul);

        var effect = GoryosVengeanceFactory
            .BuildResolveEffect(_alice, _zones, triggers)
            .Single();
        effect.Execute();

        emrakul.Zone.Should().Be(ZoneType.Battlefield);

        // Fire the next End step — the delayed trigger queues onto the
        // stack and resolves into Battlefield → Exile.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        emrakul.Zone.Should().Be(ZoneType.Exile,
            "CR 603.7 — delayed end-step exile fires (battlefield → exile)");
        _alice.Zones.Exile.GetCards().Should().Contain(emrakul);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(emrakul);
    }

    [Fact]
    public void Resolve_EmptyGraveyard_IsCleanNoOp()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var effect = GoryosVengeanceFactory
            .BuildResolveEffect(_alice, _zones, triggers)
            .Single();

        var act = () => effect.Execute();
        act.Should().NotThrow(
            "empty graveyard → no legal target → clean no-op");

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        // No delayed trigger registered (nothing to exile).
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(0);
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FootstepsOfTheGoryoFactory"/>.
///
/// Footsteps of the Goryo (Betrayers of Kamigawa, {2}{B}). Sorcery — Arcane.
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Return target creature card from your graveyard to the battlefield.
///    Sacrifice that creature at the beginning of the next end step."
///
/// Covers:
/// - Card identity (name, Sorcery type, {2}{B} mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Resolve: returns the targeted creature card from the CASTER's graveyard
///   to the caster's battlefield — any creature, not just legendary.
/// - Resolve illegal-target gate (CR 608.2b): non-creature / wrong-owner
///   targets are no-ops.
/// - Resolve routes through ZoneService when supplied (CR 603.6a — ETB).
/// - The delayed end-step sacrifice (CR 603.7 / 701.16) sends the reanimated
///   creature to its owner's graveyard.
/// </summary>
[Trait("Color", "B")]
public class FootstepsOfTheGoryoFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public FootstepsOfTheGoryoFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void FootstepsOfTheGoryo_Identity()
    {
        var c = FootstepsOfTheGoryoFactory.Create(_alice);

        c.Name.Should().Be("Footsteps of the Goryo");
        c.Should().BeOfType<Sorcery>();
        c.ManaCost.Should().Be("{2}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FootstepsOfTheGoryo()
    {
        var card = NamedCardFactory.Create("Footsteps of the Goryo", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Footsteps of the Goryo");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}");
    }

    [Fact]
    public void Resolve_ReturnsTargetCreature_AnyCreature_NotJustLegendary()
    {
        var alice = new Player("Alice", 20);

        // Grizzly Bears — a plain (non-legendary) creature. Footsteps returns
        // ANY creature card, unlike Goryo's Vengeance (legendary-only).
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bears);
        bears.SetZone(ZoneType.Graveyard);

        var def = FootstepsOfTheGoryoFactory.BuildSpellDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null,
            Targets: new[] { new object[] { bears } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }

        bears.Zone.Should().Be(ZoneType.Battlefield,
            "the target creature card was returned to the caster's battlefield");
        alice.Zones.Graveyard.GetCards().Should().NotContain(bears);
        alice.Zones.Battlefield.GetCards().Should().Contain(bears);
        bears.Controller.Should().BeSameAs(alice,
            "the returned permanent enters under the caster's control (CR 110.2)");
        alice.LifeTotal.Should().Be(20,
            "Footsteps of the Goryo has no life-loss clause — caster's life is unchanged");
    }

    [Fact]
    public void Resolve_IgnoresNonCreatureTarget()
    {
        var alice = new Player("Alice", 20);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var def = FootstepsOfTheGoryoFactory.BuildSpellDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null,
            Targets: new[] { new object[] { bolt } }, Mana: ManaPayment.Empty);
        var act = () =>
        {
            foreach (var effect in def.EffectFactory(chosen))
            {
                effect.Execute();
            }
        };

        act.Should().NotThrow(
            "a non-creature target is illegal — resolve no-ops (CR 608.2b)");
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "instants are not creature cards — must remain in graveyard");
    }

    [Fact]
    public void Resolve_IgnoresCreatureNotInCastersGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Creature card sits in BOB's graveyard — "your graveyard" means the
        // caster's only, so it is not a legal target (CR 608.2b).
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var def = FootstepsOfTheGoryoFactory.BuildSpellDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null,
            Targets: new[] { new object[] { giant } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }

        giant.Zone.Should().Be(ZoneType.Graveyard,
            "the creature belongs to another player's graveyard — not 'your graveyard'");
        alice.Zones.Battlefield.GetCards().Should().NotContain(giant);
    }

    [Fact]
    public void Resolve_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var def = FootstepsOfTheGoryoFactory.BuildSpellDefinition(alice, o => o, zoneService: zones);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null,
            Targets: new[] { new object[] { bear } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }

        bear.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, bear)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }

    [Fact]
    public void Resolve_RegistersDelayedEndStepSacrifice_ForReanimatedCreature()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bears);
        bears.SetZone(ZoneType.Graveyard);

        var def = FootstepsOfTheGoryoFactory.BuildSpellDefinition(
            _alice, o => o, zoneService: _zones, triggers: triggers);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null,
            Targets: new[] { new object[] { bears } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }

        bears.Zone.Should().Be(ZoneType.Battlefield);

        // Fire the next End step — the delayed trigger queues onto the stack
        // and resolves into Battlefield → Graveyard (sacrifice).
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        bears.Zone.Should().Be(ZoneType.Graveyard,
            "CR 603.7 / 701.16 — delayed end-step sacrifice fires (battlefield → owner's graveyard)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bears);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bears);
    }

    [Fact]
    public void Resolve_NoTriggerManager_IsCleanNoOpForSacrifice()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bears);
        bears.SetZone(ZoneType.Graveyard);

        var def = FootstepsOfTheGoryoFactory.BuildSpellDefinition(
            _alice, o => o, zoneService: _zones, triggers: null);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null,
            Targets: new[] { new object[] { bears } }, Mana: ManaPayment.Empty);
        var act = () =>
        {
            foreach (var effect in def.EffectFactory(chosen))
            {
                effect.Execute();
            }
        };

        act.Should().NotThrow(
            "shape-only callers (triggers: null) reanimate but skip the delayed sacrifice");
        bears.Zone.Should().Be(ZoneType.Battlefield,
            "the creature is still reanimated even without a trigger manager");
    }
}

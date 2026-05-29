using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VexingDevilFactory"/> (Avacyn Restored, {R}).
///
/// Creature — Devil 4/3. Oracle text:
///   "When this creature enters, any opponent may have it deal 4 damage to
///    them. If a player does, sacrifice this creature."
///
/// Covers:
///   - Identity (Devil 4/3 at {R}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch (JSON-backed base shape).
///   - ETB trigger attached structurally on shape-only path.
///   - Accept: opponent takes 4 damage AND the Devil is sacrificed.
///   - Decline: opponent untouched AND the Devil stays on the battlefield.
///   - No opponent resolver → whole ETB body no-ops.
/// </summary>
public class VexingDevilFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void VexingDevil_Identity()
    {
        var c = VexingDevilFactory.Create(_alice);

        c.Name.Should().Be("Vexing Devil");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Devil).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VexingDevil_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Vexing Devil", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Vexing Devil");
        c.HasSubtype(CardSubtype.Devil).Should().BeTrue();
        ((Creature)c).BasePower.Should().Be(4);
        ((Creature)c).BaseToughness.Should().Be(3);
    }

    [Fact]
    public void VexingDevil_HasOneEtbTriggeredAbility()
    {
        var c = VexingDevilFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the printed ETB trigger");
    }

    [Fact]
    public void EtbEffect_OpponentAccepts_TakesFourDamage_AndDevilIsSacrificed()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        // Bob accepts the prompt — "may have it deal 4 damage to them".
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(true);
        AgentRegistry.Set(_bob, bobAgent);

        try
        {
            var card = VexingDevilFactory.Create(
                _alice,
                triggers,
                zones,
                opponentResolver: () => new[] { _bob });

            _alice.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);

            var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
            foreach (var e in trigger.Effects) e.Execute();

            // Bob took 4 damage (CR 119.3 — damage to a player is life loss).
            _bob.LifeTotal.Should().Be(16,
                "the accepting opponent has Vexing Devil deal 4 damage to them");

            // "If a player does, sacrifice this creature." — Devil left the
            // battlefield to its owner's graveyard.
            card.Zone.Should().Be(ZoneType.Graveyard);
            _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
            _alice.Zones.Graveyard.GetCards().Should().Contain(card);
        }
        finally
        {
            AgentRegistry.Remove(_bob);
        }
    }

    [Fact]
    public void EtbEffect_OpponentDeclines_NoDamage_AndDevilStays()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        // Bob declines the prompt.
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(false);
        AgentRegistry.Set(_bob, bobAgent);

        try
        {
            var card = VexingDevilFactory.Create(
                _alice,
                triggers,
                zones,
                opponentResolver: () => new[] { _bob });

            _alice.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);

            var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
            foreach (var e in trigger.Effects) e.Execute();

            // No damage; the controller keeps a 4/3 for {R}.
            _bob.LifeTotal.Should().Be(20, "the opponent declined the damage");
            card.Zone.Should().Be(ZoneType.Battlefield);
            _alice.Zones.Battlefield.GetCards().Should().Contain(card);
        }
        finally
        {
            AgentRegistry.Remove(_bob);
        }
    }

    [Fact]
    public void EtbEffect_NoAgent_DefaultsToDecline_DevilStays()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        // No agent registered for Bob → default decline.
        var card = VexingDevilFactory.Create(
            _alice,
            triggers,
            zones,
            opponentResolver: () => new[] { _bob });

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20);
        card.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void EtbEffect_WithoutOpponentResolver_NoOp()
    {
        var card = VexingDevilFactory.Create(
            _alice,
            triggers: null,
            zoneService: null,
            opponentResolver: null);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20);
        card.Zone.Should().Be(ZoneType.Battlefield);
    }
}

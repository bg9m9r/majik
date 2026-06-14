using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Mortician Beetle (Conflux, {B}).
///
/// Oracle (Scryfall, verified):
///   "Whenever a player sacrifices a creature, you may put a +1/+1 counter on
///    Mortician Beetle."
///
/// This is the first card to consume the declarative <b>any-player</b> sacrifice
/// trigger surface (<c>whenever_a_player_sacrifices_permanent</c>) +
/// free-optional "you may" rider end-to-end over the
/// <see cref="PermanentSacrificedEvent"/> bus.
///
/// Covers:
///   - Card shape: name, type, Insect subtype, P/T 1/1, mana cost, owner /
///     controller wiring.
///   - NamedCardFactory dispatch.
///   - Trigger predicate: fires on ANY player's creature sacrifice (controller
///     OR opponent, token OR nontoken); does NOT fire on a non-creature
///     sacrifice.
///   - Free-optional resolution: "yes" places one +1/+1 counter; "no" places
///     none.
/// </summary>
[Trait("Color", "B")]
public class MorticianBeetleFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext(IPlayerAgent agent) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    private static ScriptedAgent YesNo(bool answer)
    {
        var agent = new ScriptedAgent();
        agent.QueueYesNo(answer);
        return agent;
    }

    [Fact]
    public void Beetle_Identity()
    {
        var c = MorticianBeetleFactory.Create(_alice);

        c.Name.Should().Be("Mortician Beetle");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Beetle_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Mortician Beetle", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Mortician Beetle");
    }

    [Fact]
    public void Beetle_OpponentSacrificesCreature_TriggerFires()
    {
        var beetle = MorticianBeetleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(beetle);
        beetle.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);

        var sacEvent = new PermanentSacrificedEvent(bear, _bob, wasToken: false);
        var trigger = beetle.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(sacEvent).Should().BeTrue(
            "an opponent sacrificing a creature fires Mortician Beetle (CR 700.6 'a player')");
    }

    [Fact]
    public void Beetle_ControllerSacrificesOwnCreatureToken_TriggerFires()
    {
        // "a player" includes the controller, and a sacrificed token creature
        // fires it just the same (no nontoken filter).
        var beetle = MorticianBeetleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(beetle);
        beetle.SetZone(ZoneType.Battlefield);

        var token = new Creature("Zombie", "{0}", 2, 2);
        token.SetOwner(_alice);
        token.SetController(_alice);

        var sacEvent = new PermanentSacrificedEvent(token, _alice, wasToken: true);
        var trigger = beetle.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(sacEvent).Should().BeTrue(
            "the controller's own creature-token sacrifice fires Mortician Beetle");
    }

    [Fact]
    public void Beetle_PlayerSacrificesNonCreature_DoesNotFire()
    {
        // CR 205.2 — the trigger is gated to a sacrificed CREATURE; sacrificing
        // a noncreature permanent (an artifact) does not fire it.
        var beetle = MorticianBeetleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(beetle);
        beetle.SetZone(ZoneType.Battlefield);

        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(_bob);
        rock.SetController(_bob);

        var sacEvent = new PermanentSacrificedEvent(rock, _bob, wasToken: false);
        var trigger = beetle.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(sacEvent).Should().BeFalse(
            "sacrificing a NON-creature does not fire Mortician Beetle");
    }

    [Fact]
    public async Task Beetle_OptionalYes_PlacesOnePlusOnePlusOneCounter()
    {
        var beetle = MorticianBeetleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(beetle);
        beetle.SetZone(ZoneType.Battlefield);

        var trigger = beetle.Abilities.OfType<TriggeredAbility>().Single();
        var agent = YesNo(true);
        await trigger.ResolveAsync(agent, game: NewContext(agent));

        beetle.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "accepting the 'you may' choice places one +1/+1 counter (CR 122.1)");
    }

    [Fact]
    public async Task Beetle_OptionalNo_PlacesNoCounter()
    {
        var beetle = MorticianBeetleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(beetle);
        beetle.SetZone(ZoneType.Battlefield);

        var trigger = beetle.Abilities.OfType<TriggeredAbility>().Single();
        var agent = YesNo(false);
        await trigger.ResolveAsync(agent, game: NewContext(agent));

        beetle.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "declining the 'you may' choice places no counter (CR 603.4)");
    }
}

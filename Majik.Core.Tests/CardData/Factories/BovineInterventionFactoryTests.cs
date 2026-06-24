using FluentAssertions;
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
/// Tests for Bovine Intervention (Modern Horizons 3, {1}{W}, Instant).
///
/// Oracle text: "Destroy target artifact or creature. Its controller
/// creates a 2/2 white Ox creature token."
///
/// Bovine Intervention combines Putrefy's "destroy target artifact or
/// creature" targeting with Generous Gift's "its controller gets a vanilla
/// token" tail (2/2 white Ox instead of a 3/3 green Elephant). Unlike
/// Putrefy it is a plain Destroy (regeneration honoured).
///
/// Covers (unique behaviour only — CardFactoryContractTests already asserts
/// dispatch + well-formedness):
///   - Card identity ({1}{W} Instant).
///   - Destroys a target creature; its controller gets a 2/2 white Ox token.
///   - Destroys a target artifact; its controller gets the Ox token
///     (artifact-or-creature targeting).
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class BovineInterventionFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public BovineInterventionFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow  = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public void BovineIntervention_IsInstant_AtCost1W()
    {
        var card = BovineInterventionFactory.Create(_alice);

        card.Name.Should().Be("Bovine Intervention");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public async Task BovineIntervention_DestroysCreature_ItsControllerGets2_2WhiteOxToken()
    {
        // Bob controls a Grizzly Bears.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _bob);

        await CastAndResolve(bears);

        // Bears destroyed.
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bears);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);

        // Bob receives a 2/2 white Ox token.
        var token = OxTokenOf(_bob);
        token.Should().NotBeNull("Bob should have an Ox token");
        token!.Power.Should().Be(2);
        token.Toughness.Should().Be(2);
        token.Owner.Should().BeSameAs(_bob);
        token.Controller.Should().BeSameAs(_bob);
        CardColors.GetColors(token).Should().BeEquivalentTo(new[] { ManaColor.White });
    }

    [Fact]
    public async Task BovineIntervention_DestroysArtifact_ItsControllerGetsOxToken()
    {
        // Bob controls a noncreature artifact — Bovine Intervention can
        // target "artifact or creature", not only creatures.
        var relic = new Artifact("Test Relic", "{2}");
        relic.SetOwner(_bob);
        relic.SetController(_bob);
        relic.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(relic);

        await CastAndResolve(relic);

        relic.Zone.Should().Be(ZoneType.Graveyard);
        OxTokenOf(_bob).Should().NotBeNull("artifact's controller gets the Ox token");
    }

    [Fact]
    public async Task BovineIntervention_OffBattlefieldTarget_NoOp()
    {
        // CR 608.2b — target illegal at resolution → spell does nothing.
        var bears = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, _bob);
        _bob.Zones.Battlefield.RemoveCard(bears);
        bears.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bears);

        await CastAndResolve(bears);

        OxTokenOf(_bob).Should().BeNull();
        OxTokenOf(_alice).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature? OxTokenOf(Player p) => p.Zones.Battlefield.GetCards()
        .OfType<Creature>()
        .FirstOrDefault(c => c.IsToken && c.HasSubtype(CardSubtype.Ox));

    private static Creature MakeCreature(string name, string cost, int power, int toughness, Player controller)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    private async Task CastAndResolve(object target)
    {
        var card = BovineInterventionFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, StepStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            BovineInterventionFactory.BuildSpellDefinition(t => t),
            agent, ctx);

        card.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();
    }
}

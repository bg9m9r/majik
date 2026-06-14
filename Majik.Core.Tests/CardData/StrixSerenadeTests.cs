using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Strix Serenade (Bloomburrow, {U}).
/// Oracle: "Counter target artifact, creature, or planeswalker spell. Its
/// controller creates a 2/2 blue Bird creature token with flying."
///
/// Coverage (UNIQUE behaviour only — CardFactoryContractTests covers dispatch
/// + well-formedness, and the *_Identity assert covers exact cost/colour):
///   * Identity: {U} Instant, blue, mana value 1.
///   * SpellDefinition shape (1 target artifact/creature/planeswalker request).
///   * Counter a creature spell → graveyard (CR 701.5) AND its controller gets
///     a 2/2 blue Bird token with flying (CR 111.4).
///   * Target is a noncreature/non-artifact/non-PW spell (an instant) → no-op
///     at resolution: not countered, no Bird minted (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class StrixSerenadeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public StrixSerenadeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_Identity_BlueInstant()
    {
        var serenade = StrixSerenadeFactory.Create(_alice);

        serenade.Name.Should().Be("Strix Serenade");
        serenade.HasType(CardType.Instant).Should().BeTrue();
        serenade.ManaCost.Should().Be("{U}");
        serenade.ManaCostValue.TotalValue.Should().Be(1);
        CardColors.GetColors(serenade).Should().Contain(ManaColor.Blue,
            "Strix Serenade has blue in its cost {U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleArtifactCreaturePlaneswalkerTargetRequest()
    {
        var def = StrixSerenadeFactory.BuildSpellDefinition(o => o, null, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].Description.Should().Contain("planeswalker");
    }

    [Fact]
    public async Task CountersCreatureSpell_AndGivesControllerBlueBirdFlyer()
    {
        var serenade = StrixSerenadeFactory.Create(_alice);
        serenade.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(serenade);

        // Bob casts a creature spell (Grizzly Bears {1}{G}).
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, serenade,
            StrixSerenadeFactory.BuildSpellDefinition(o => o, _stack, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            "Strix Serenade counters the creature spell (CR 701.5)");

        // CR 111.4 — the countered spell's CONTROLLER (Bob) gets a 2/2 blue Bird
        // with flying; the Serenade's caster (Alice) gets nothing.
        var bobBirds = _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Bird")
            .ToList();
        bobBirds.Should().HaveCount(1,
            "the countered spell's controller creates one Bird token");
        var bird = bobBirds[0];
        bird.Power.Should().Be(2);
        bird.Toughness.Should().Be(2);
        bird.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        CardColors.GetColors(bird).Should().BeEquivalentTo(new[] { ManaColor.Blue },
            "the Bird token is blue (CR 111.4)");
        bird.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword.Equals("Flying", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the Bird token has flying");

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "the Bird goes to the countered spell's controller, not the caster");
    }

    [Fact]
    public async Task DoesNotCounterInstantSpell_NoBird()
    {
        var serenade = StrixSerenadeFactory.Create(_alice);
        serenade.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(serenade);

        // Bob casts an instant (Lightning Bolt {R}) — not artifact/creature/PW.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, serenade,
            StrixSerenadeFactory.BuildSpellDefinition(o => o, _stack, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — an instant is an illegal target at resolution.
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            "Strix Serenade does not counter instant spells");
        _bob.Zones.Battlefield.GetCards().Where(c => c.Name == "Bird")
            .Should().BeEmpty("no counter means no Bird token (CR 608.2b)");
    }
}

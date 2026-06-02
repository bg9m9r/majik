using FluentAssertions;
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
/// End-to-end tests for Ceremonious Rejection (Aether Revolt, {U}).
/// Oracle: "Counter target colorless spell."
///
/// Mirror of <see cref="Majik.Core.Tests.CardData.DispelTests"/> with a
/// colorless-only target filter (CR 105.2c): counters only colorless spells;
/// a colored spell is an illegal target at resolution (CR 608.2b) and the
/// effect does nothing. The card itself is blue ({U}) — only its target must
/// be colorless.
/// </summary>
[Trait("Color", "U")]
public class CeremoniousRejectionTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CeremoniousRejectionTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var card = CeremoniousRejectionFactory.Create(_alice);

        card.Name.Should().Be("Ceremonious Rejection");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(1);
    }
    [Fact]
    public void SpellDefinition_DeclaresSingleTargetColorlessSpellRequest()
    {
        var def = CeremoniousRejectionFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("colorless");
    }

    [Fact]
    public async Task CountersColorlessSpell()
    {
        var card = CeremoniousRejectionFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob casts a colorless spell (an artifact with a generic-only cost).
        var bobArtifact = new Artifact("Ornithopter", "{0}") { Owner = _bob, Controller = _bob };
        CardColors.GetColors(bobArtifact).Should().BeEmpty(because: "the target must be colorless");
        var bobSpell = new Majik.Core.Spells.Spell(bobArtifact, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            CeremoniousRejectionFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobArtifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Ceremonious Rejection counters the colorless spell");
    }

    [Fact]
    public async Task DoesNotCounterColoredSpell()
    {
        var card = CeremoniousRejectionFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob casts a colored spell (red instant).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        CardColors.GetColors(bobBolt).Should().Contain(ManaColor.Red);
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            CeremoniousRejectionFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — a colored spell is an illegal target at resolution.
        // Ceremonious Rejection does nothing for it.
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Ceremonious Rejection does not counter colored spells");
    }
}

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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Annul (Urza's Saga / Magic Origins, {U}).
/// Oracle: "Counter target artifact or enchantment spell."
///
/// Coverage:
///   * Card shape + dispatch by name (Instant {U}, blue).
///   * SpellDefinition shape (1 target).
///   * Counters an artifact spell → graveyard (CR 701.5).
///   * Counters an enchantment spell → graveyard.
///   * Creature spell target → no-op at resolution (CR 608.2b).
/// </summary>
public class AnnulFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AnnulFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue_U()
    {
        var annul = AnnulFactory.Create(_alice);

        annul.Name.Should().Be("Annul");
        annul.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(annul).Should().Contain(ManaColor.Blue);
        annul.ManaCost.Should().Be("{U}");
        annul.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsAnnulShape()
    {
        var dispatched = NamedCardFactory.Create("Annul", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Annul");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetRequest()
    {
        var def = AnnulFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
        def.TargetRequests[0].Description.Should().Contain("enchantment");
    }

    [Fact]
    public async Task CountersArtifactSpell()
    {
        var annul = AnnulFactory.Create(_alice);
        annul.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(annul);

        // Bob casts an artifact spell.
        var bobArtifact = new Artifact("Sol Ring", "{1}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobArtifact, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, annul,
            AnnulFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobArtifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Annul counters artifact spells");
    }

    [Fact]
    public async Task CountersEnchantmentSpell()
    {
        var annul = AnnulFactory.Create(_alice);
        annul.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(annul);

        var bobEnchant = new Enchantment("Pacifism", "{1}{W}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobEnchant, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, annul,
            AnnulFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobEnchant.Zone.Should().Be(ZoneType.Graveyard,
            because: "Annul counters enchantment spells");
    }

    [Fact]
    public async Task DoesNotCounterCreatureSpell()
    {
        var annul = AnnulFactory.Create(_alice);
        annul.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(annul);

        // Bob casts a creature spell (Grizzly Bears).
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, annul,
            AnnulFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Annul does not counter creature spells (CR 608.2b)");
    }
}

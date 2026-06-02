using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
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
/// End-to-end tests for Dissolve (Theros / various reprints, {1}{U}{U}).
/// Oracle: "Counter target spell. Scry 1."
///
/// Dissolve is Cancel / Counterspell + Scry 1 for the caster after countering.
///
/// Coverage:
///   * Card shape + dispatch by name.
///   * SpellDefinition shape (1 target spell request, no type filter).
///   * Counters a noncreature spell → graveyard (CR 701.5).
///   * Counters a creature spell → graveyard (no filter; any spell).
///   * Scry 1 fires after counter resolves — default (no agent) bottoms the
///     peeked card; caster's library reordered correctly.
///   * Scry 1 with agent registered — agent keeps peeked card on top.
///   * Scry 1 on empty library — short-circuits gracefully; no throw.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "U")]
public class DissolveFactoryTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DissolveFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue_At1UU()
    {
        var card = DissolveFactory.Create(_alice);

        card.Name.Should().Be("Dissolve");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.ToString().Should().Be("{1}{U}{U}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest_NoTypeFilter()
    {
        var def = DissolveFactory.BuildSpellDefinition(o => o, null, _alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target spell");
    }

    // ── Counter effect ────────────────────────────────────────────────────────

    [Fact]
    public async Task CountersNoncreatureSpell_ToGraveyard()
    {
        var dissolve = DissolveFactory.Create(_alice);
        dissolve.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dissolve);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dissolve,
            DissolveFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Dissolve counters the target spell (CR 701.5)");
    }

    [Fact]
    public async Task CountersCreatureSpell_ToGraveyard()
    {
        var dissolve = DissolveFactory.Create(_alice);
        dissolve.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dissolve);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dissolve,
            DissolveFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "Dissolve has no type filter — counters creature spells too");
    }

    // ── Scry 1 after counter ──────────────────────────────────────────────────

    [Fact]
    public async Task AfterCounter_DefaultScry_BottomsTopCard()
    {
        // Alice's library: [top, next]. No agent registered → default sends
        // top to bottom. After resolve: library = [next, top].
        var top = SeedLibraryCard(_alice, "Top");
        var next = SeedLibraryCard(_alice, "Next");

        var dissolve = DissolveFactory.Create(_alice);
        dissolve.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dissolve);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dissolve,
            DissolveFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Countered spell → graveyard.
        bobBolt.Zone.Should().Be(ZoneType.Graveyard);
        // Scry 1: top was bottomed, next is now on top.
        _alice.Zones.Library.GetCards().Should().Equal(new[] { next, top },
            because: "default scry sends the peeked card to the bottom");
    }

    [Fact]
    public async Task AfterCounter_AgentKeepsTop_LibraryUnchanged()
    {
        // Alice's library: [top, next]. Agent keeps top on library.
        // After resolve: library = [top, next].
        var top = SeedLibraryCard(_alice, "Top");
        var next = SeedLibraryCard(_alice, "Next");

        var dissolve = DissolveFactory.Create(_alice);
        dissolve.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dissolve);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { top }));
        AgentRegistry.Set(_alice, agent);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dissolve,
            DissolveFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { top, next },
            because: "agent kept top card on top of library");
    }

    [Fact]
    public async Task AfterCounter_EmptyLibrary_ScryNoOp_NoThrow()
    {
        // Alice has no library cards — scry 1 should short-circuit gracefully.
        var dissolve = DissolveFactory.Create(_alice);
        dissolve.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dissolve);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dissolve,
            DissolveFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: null);

        Action act = () => _resolver.ResolveTop(_stack);
        act.Should().NotThrow(because: "scry on empty library must not throw");
        bobBolt.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}

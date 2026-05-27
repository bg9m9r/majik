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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Exclude (Judgment, {2}{U}).
/// Oracle: "Counter target creature spell. Draw a card."
///
/// Coverage:
///   * Card shape + dispatch by name (Instant {2}{U}, blue, CMC 3).
///   * SpellDefinition shape (1 target request).
///   * Counters a creature spell → graveyard (CR 701.5) AND caster draws a card.
///   * Does NOT counter a noncreature spell; no draw (CR 608.2b no-op).
///   * Draw: top-of-library goes to hand; empty library flags SBA loss (CR 704.5b).
/// </summary>
public class ExcludeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ExcludeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Blue_2U()
    {
        var exclude = ExcludeFactory.Create(_alice);

        exclude.Name.Should().Be("Exclude");
        exclude.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(exclude).Should().Contain(ManaColor.Blue);
        exclude.ManaCost.Should().Be("{2}{U}");
        exclude.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsExcludeShape()
    {
        var dispatched = NamedCardFactory.Create("Exclude", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Exclude");
        dispatched.ManaCost.Should().Be("{2}{U}");
    }

    // -------------------------------------------------------------------------
    // SpellDefinition shape
    // -------------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetRequest()
    {
        var def = ExcludeFactory.BuildSpellDefinition(o => o, null, _alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    // -------------------------------------------------------------------------
    // Counter + draw (happy path)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountersCreatureSpell_AndCasterDrawsCard()
    {
        var exclude = ExcludeFactory.Create(_alice);
        exclude.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(exclude);

        // Seed Alice's library so she can draw.
        var libraryCard = new Creature("Island Fish", "{5}{U}{U}", 6, 8) { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(libraryCard);
        libraryCard.SetZone(ZoneType.Library);

        // Bob casts a creature spell.
        var bobCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCreature, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, exclude,
            ExcludeFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Creature spell countered — goes to graveyard (CR 701.5).
        bobCreature.Zone.Should().Be(ZoneType.Graveyard,
            because: "Exclude counters creature spells");

        // Caster draws a card — library card moves to hand (CR 121.1).
        _alice.Zones.Hand.GetCards().Should().Contain(libraryCard,
            because: "Exclude's draw clause fires when the counter resolves");
        libraryCard.Zone.Should().Be(ZoneType.Hand);
    }

    // -------------------------------------------------------------------------
    // Noncreature spell — no-op (CR 608.2b)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounterNoncreatureSpell_AndDoesNotDraw()
    {
        var exclude = ExcludeFactory.Create(_alice);
        exclude.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(exclude);

        // Seed Alice's library — if a draw erroneously fires we'll detect it.
        var libraryCard = new Creature("Canary", "{0}", 1, 1) { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(libraryCard);
        libraryCard.SetZone(ZoneType.Library);

        // Bob casts a sorcery spell (non-creature).
        var bobSorcery = new Sorcery("Dark Ritual", "{B}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobSorcery, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, exclude,
            ExcludeFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Noncreature spell target → Exclude does nothing (CR 608.2b).
        bobSorcery.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Exclude only counters creature spells");

        // No draw when the effect does nothing (CR 608.2b entire-effect no-op).
        _alice.Zones.Hand.GetCards().Should().NotContain(libraryCard,
            because: "Exclude draws only when it successfully counters");
    }

    // -------------------------------------------------------------------------
    // Draw edge case — empty library
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountersCreatureSpell_EmptyLibrary_FlagsSbaLoss()
    {
        var exclude = ExcludeFactory.Create(_alice);
        exclude.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(exclude);

        // Alice has NO library cards.

        var bobCreature = new Creature("Goblin Scout", "{R}", 1, 1) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCreature, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, exclude,
            ExcludeFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobCreature.Zone.Should().Be(ZoneType.Graveyard,
            because: "Exclude still counters the creature even with an empty library");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            because: "drawing from an empty library flags the SBA-driven loss (CR 704.5b)");
    }
}

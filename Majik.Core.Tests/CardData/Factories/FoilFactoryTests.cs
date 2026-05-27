using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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
/// End-to-end tests for Foil (Prophecy, {1}{U}{U}).
/// Exercises:
///   * Card shape + dispatch.
///   * Pitch cast — exiles a blue card, discards a card, counters target.
///   * Pitch+discard cost rejected when discard pick is same as exile.
///   * Pitch+discard cost rejected when exile pick is wrong colour.
///   * Resolve "counter target spell" via printed mana — counters the spell.
///   * Illegal target (spell no longer on stack) → counter no-ops.
/// </summary>
public class FoilFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FoilFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var foil = FoilFactory.Create(_alice);

        foil.Name.Should().Be("Foil");
        foil.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(foil).Should().Contain(ManaColor.Blue);
        foil.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsFoilShape()
    {
        var dispatched = NamedCardFactory.Create("Foil", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Foil");
    }

    [Fact]
    public async Task CastViaPitch_ExilesBlueCard_DiscardsCard_CountersTarget()
    {
        var foil = FoilFactory.Create(_alice);
        foil.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(foil);

        var blueFuel = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        blueFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blueFuel);

        var discardFuel = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        discardFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(discardFuel);

        // Bob has Lightning Bolt on the stack — Foil targets it.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var pitchCost = new PitchAndDiscardAlternativeCost(ManaColor.Blue, blueFuel, discardFuel);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, foil,
            FoilFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        blueFuel.Zone.Should().Be(ZoneType.Exile,
            because: "pitched blue card is exiled (CR 117.11 + CR 701.21)");
        discardFuel.Zone.Should().Be(ZoneType.Graveyard,
            because: "discarded card moves hand → graveyard (CR 701.16a)");
        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Foil counters target spell — countered spell → graveyard (CR 701.5)");
        _stack.GetAll().Should().NotContain(s => ReferenceEquals(s, bobSpell));
    }

    [Fact]
    public void PitchAndDiscardCost_RejectsSameCardForExileAndDiscard()
    {
        var card = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        var act = () => new PitchAndDiscardAlternativeCost(ManaColor.Blue, card, card);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PitchAndDiscardCost_CanCastFor_RejectsWrongColourExile()
    {
        var foil = FoilFactory.Create(_alice);
        foil.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(foil);

        var redCard = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        redCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(redCard);

        var anyCard = new Instant("Shock", "{R}") { Owner = _alice };
        anyCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(anyCard);

        var pitchCost = new PitchAndDiscardAlternativeCost(ManaColor.Blue, redCard, anyCard);
        pitchCost.CanCastFor(foil, _alice).Should().BeFalse(
            because: "the exile pick must be a blue card");
    }

    [Fact]
    public async Task CastWithPrintedMana_CountersTargetSpell()
    {
        // Printed-mana cast — no alternative cost. Foil is castable at
        // instant speed any time (CR 117.1).
        var foil = FoilFactory.Create(_alice);
        foil.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(foil);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, foil,
            FoilFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_UncounterableSpell_IsNoOp()
    {
        // CR 701.5b — uncounterable spells survive the counter attempt.
        var bobBolt = new Instant("Boseiju, Who Shelters All — Bolt", "{R}")
            { Owner = _bob, Controller = _bob };
        bobBolt.SetZone(ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob) { CannotBeCountered = true };
        _stack.Push(bobSpell);

        var def = FoilFactory.BuildDefinition(o => o, _stack);
        var picks = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new object[] { bobSpell } },
            ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "uncounterable spell stays on the stack (CR 701.5b)");
        _stack.GetAll().Should().Contain(s => ReferenceEquals(s, bobSpell));
    }
}

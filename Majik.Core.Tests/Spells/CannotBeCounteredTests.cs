using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.Stack;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Spells;

/// <summary>
/// CR 701.5b — "An uncounterable spell can't be countered."
///
/// Covers the per-spell <see cref="Spell.CannotBeCountered"/> primitive +
/// its cast-time stamp (<see cref="SpellCastFlow"/>) and the
/// <see cref="OracleSpellBinder.RemoveFromStack"/> veto path:
///
///   1. Counter-attempt vs an uncounterable spell → stack unchanged.
///   2. Counter-attempt vs a normal spell → spell popped from the stack.
///   3. Non-spell stack-object target → veto guard does not apply
///      (control: a non-ISpell IStackObject is popped normally because
///      the flag is an ISpell-only property).
///   4. SpellCastFlow reads the KeywordAbility("Uncounterable") marker
///      off the card and stamps the resolving Spell's flag.
/// </summary>
public class CannotBeCounteredTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CannotBeCounteredTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public void RemoveFromStack_AgainstUncounterableSpell_VetoesPop_StackUnchanged()
    {
        var card = new Creature("Big", "{15}", 15, 15) { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(card, _alice) { CannotBeCountered = true };
        _stack.Push(spell);
        _stack.Count.Should().Be(1);

        // Direct call mirrors what counter templates / Fx.Counter perform.
        var removed = OracleSpellBinder_RemoveFromStackForTest(_stack, spell);

        removed.Should().BeFalse("CR 701.5b — the uncounterable veto returns false");
        _stack.Count.Should().Be(1, "the uncounterable spell stays on the stack");
        _stack.Top.Should().BeSameAs(spell);
    }

    [Fact]
    public void RemoveFromStack_AgainstNormalSpell_PopsAndReturnsTrue()
    {
        var card = new Instant("Bolt", "{R}") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(card, _alice);
        _stack.Push(spell);

        var removed = OracleSpellBinder_RemoveFromStackForTest(_stack, spell);

        removed.Should().BeTrue("non-uncounterable spells are popped normally");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Counter_AgainstUncounterableSpell_SkipsCardGraveyardTail()
    {
        // Fx.Counter is the canonical "pop + send card to graveyard"
        // primitive. The graveyard tail must be skipped when the veto
        // returns false; otherwise the card's tracking zone would drift
        // out of sync with the stack (spell still on stack but card
        // marked graveyard).
        var card = new Creature("Big", "{15}", 15, 15) { Owner = _alice };
        card.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(card, _alice) { CannotBeCountered = true };
        _stack.Push(spell);

        Majik.Core.Primitives.Fx.Counter(_stack, spell);

        _stack.Count.Should().Be(1, "the spell stays on the stack");
        card.Zone.Should().Be(ZoneType.Stack, "card tracking zone is not migrated to graveyard");
    }

    [Fact]
    public void Counter_AgainstNormalSpell_PopsAndMovesCardToGraveyard()
    {
        // Control case — no CannotBeCountered stamp.
        var card = new Instant("Bolt", "{R}") { Owner = _alice };
        card.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(card, _alice);
        _stack.Push(spell);

        Majik.Core.Primitives.Fx.Counter(_stack, spell);

        _stack.IsEmpty.Should().BeTrue();
        card.Zone.Should().Be(ZoneType.Graveyard,
            "the canonical counter tail moves the card to the graveyard (CR 701.5)");
    }

    [Fact]
    public void RemoveFromStack_AgainstNonSpellStackObject_VetoDoesNotApply()
    {
        // The veto reads ISpell.CannotBeCountered — a non-ISpell
        // IStackObject (an activated ability, a delayed trigger, …) is
        // never uncounterable and is popped normally. This is the "non-
        // stack target" control case mentioned in the spec: the flag is
        // a stack-object-shaped surface, not a generic targeting gate.
        var token = new DummyStackObject();
        _stack.Push(token);

        var removed = OracleSpellBinder_RemoveFromStackForTest(_stack, token);

        removed.Should().BeTrue("non-ISpell stack objects don't carry the uncounterable flag");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SpellCastFlow_StampsCannotBeCountered_WhenCardCarriesUncounterableMarker()
    {
        var card = new Instant("Pact", "{0}") { Owner = _alice, Zone = ZoneType.Hand };
        card.AddAbility(new KeywordAbility("Uncounterable", card, _alice));

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var cast = await _flow.CastAsync(
            _alice, card,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, NewContext());

        cast.CannotBeCountered.Should().BeTrue(
            "CR 701.5b — SpellCastFlow stamps the marker off the card's KeywordAbility");
    }

    [Fact]
    public async Task SpellCastFlow_LeavesCannotBeCounteredFalse_ForNormalSpells()
    {
        var card = new Instant("Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var cast = await _flow.CastAsync(
            _alice, card,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, NewContext());

        cast.CannotBeCountered.Should().BeFalse(
            "vanilla spells without an Uncounterable marker remain counterable");
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

    /// <summary>
    /// Reflection-free access to the internal RemoveFromStack — tests live
    /// in the same assembly so the bool return is directly visible via
    /// InternalsVisibleTo wiring already present in the test project.
    /// </summary>
    private static bool OracleSpellBinder_RemoveFromStackForTest(
        Majik.Core.Stack.Stack stack, IStackObject obj) =>
        OracleSpellBinder.RemoveFromStack(stack, obj);

    private sealed class DummyStackObject : IStackObject
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Player Controller { get; } = new("Carol", 20);
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public bool IsResolving => false;
        public void Resolve() { }
    }
}

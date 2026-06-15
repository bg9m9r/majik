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
/// Tests for Counterflux (Gatecrash, {U}{U}{R}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-14):
///   "This spell can't be countered.
///    Counter target spell you don't control.
///    Overload {1}{U}{U}{R} (You may cast this spell for its overload cost. If
///    you do, change "target" in its text to "each.")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Counter each spell you don't control."
///
/// Covers (the card's UNIQUE behaviour only — dispatch + well-formedness are
/// asserted for every implemented card by CardFactoryContractTests):
///   - Card identity (Instant, {U}{U}{R}, Blue + Red).
///   - "This spell can't be countered" (CR 701.5b) — the cast Counterflux
///     spell carries CannotBeCountered, so a rival counter is vetoed.
///   - SpellDefinition shape: single 1..1 "target spell you don't control"
///     request; candidate gatherer excludes the controller's own spells
///     (CR 109.5 — "you" = the spell's controller).
///   - Default (not overloaded) resolve → counters one targeted spell the
///     controller does NOT control (CR 701.5); the card goes to the graveyard.
///   - No-op against the controller's own spell (CR 109.5).
///   - No-op against an uncounterable target (CR 701.5b) — it stays on the stack.
///   - Structural overloaded branch → counters EACH spell the controller does
///     NOT control; the controller's own spell is untouched (CR 702.96b).
///
/// Overload (CR 702.96) is an alternative cost. Per the
/// <see cref="CyclonicRiftFactory"/> analogue, the
/// <see cref="Majik.Core.Costs.OverloadAlternativeCost"/> primitive is not yet
/// plumbed through <see cref="Majik.Core.Services.SpellCastFlow"/>, so
/// production casts ship not-overloaded. The overloaded branch is exercised
/// here by passing <c>wasOverloaded: true</c> through the spell-definition
/// builder directly (same posture as Cyclonic Rift / Vandalblast).
/// </summary>
[Trait("Color", "M")]
public class CounterfluxFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CounterfluxFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Counterflux_HasInstantShape_BlueRed_AtCostUUR()
    {
        var card = CounterfluxFactory.Create(_alice);

        card.Name.Should().Be("Counterflux");
        card.ManaCost.Should().Be("{U}{U}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape (default / not overloaded)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellYouDontControlRequest()
    {
        var def = CounterfluxFactory.BuildSpellDefinition(_alice, o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target spell you don't control");
    }

    [Fact]
    public void CandidateGatherer_ExcludesControllersOwnSpells()
    {
        // CR 109.5 / oracle "you don't control": only opponents' spells on the
        // stack are legal candidates.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var aliceArc = new Instant("Arcane Denial", "{1}{U}") { Owner = _alice, Controller = _alice };
        var aliceSpell = new Majik.Core.Spells.Spell(aliceArc, _alice);
        _stack.Push(aliceSpell);

        var def = CounterfluxFactory.BuildSpellDefinition(_alice, o => o, _stack);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, StepStateType.PreCombatMain, _stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(bobSpell);
        candidates.Should().NotContain(aliceSpell,
            because: "Counterflux counters a spell you DON'T control (CR 109.5)");
    }

    // -----------------------------------------------------------------------
    // "This spell can't be countered" (CR 701.5b)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CounterfluxItself_CannotBeCountered()
    {
        // Alice casts Counterflux targeting Bob's spell. Counterflux's own
        // "can't be countered" rider means a rival counter would be vetoed —
        // we assert the cast spell carries the CannotBeCountered sentinel.
        var counterflux = CounterfluxFactory.Create(_alice);
        counterflux.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(counterflux);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, counterflux,
            CounterfluxFactory.BuildSpellDefinition(_alice, o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        // The Counterflux spell is now on top of the stack — it must be flagged
        // uncounterable (CR 701.5b).
        var cfxSpell = _stack.GetAll().OfType<Majik.Core.Spells.ISpell>()
            .First(s => s.Card.Name == "Counterflux");
        cfxSpell.CannotBeCountered.Should().BeTrue(
            because: "Counterflux reads \"This spell can't be countered\" (CR 701.5b)");
    }

    // -----------------------------------------------------------------------
    // Default (not overloaded) resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void CountersTargetedOpponentSpell_ToGraveyard()
    {
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        ResolveAgainst(bobSpell, wasOverloaded: false);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Counterflux counters target spell you don't control (CR 701.5)");
        _stack.GetAll().Should().NotContain(bobSpell);
    }

    [Fact]
    public void DoesNotCounter_ControllersOwnSpell()
    {
        // CR 109.5 — re-checked at resolution: a spell the controller controls
        // is not a legal "spell you don't control".
        var aliceArc = new Instant("Arcane Denial", "{1}{U}") { Owner = _alice, Controller = _alice };
        var aliceSpell = new Majik.Core.Spells.Spell(aliceArc, _alice);
        _stack.Push(aliceSpell);

        ResolveAgainst(aliceSpell, wasOverloaded: false);

        aliceArc.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Counterflux cannot counter a spell you control (CR 109.5)");
        _stack.GetAll().Should().Contain(aliceSpell);
    }

    [Fact]
    public void DoesNotCounter_UncounterableTarget()
    {
        // CR 701.5b — an uncounterable opponent spell is a legal target but the
        // counter does nothing; the spell stays on the stack.
        var bobEmrakul = CounterfluxFactory.Create(_bob); // any uncounterable spell
        var bobSpell = new Majik.Core.Spells.Spell(bobEmrakul, _bob)
        {
            CannotBeCountered = true,
        };
        _stack.Push(bobSpell);

        ResolveAgainst(bobSpell, wasOverloaded: false);

        _stack.GetAll().Should().Contain(bobSpell,
            because: "an uncounterable spell can't be countered (CR 701.5b)");
        bobEmrakul.Zone.Should().NotBe(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Overloaded branch (structural — CR 702.96b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Overloaded_CountersEachSpell_YouDontControl()
    {
        // Two opponent spells — both countered.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobBoltSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobBoltSpell);

        var bobGiant = new Instant("Disrupting Shoal", "{X}{U}{U}") { Owner = _bob, Controller = _bob };
        var bobGiantSpell = new Majik.Core.Spells.Spell(bobGiant, _bob);
        _stack.Push(bobGiantSpell);

        // The controller's own spell — spared (CR 109.5 — "you don't control").
        var aliceArc = new Instant("Arcane Denial", "{1}{U}") { Owner = _alice, Controller = _alice };
        var aliceSpell = new Majik.Core.Spells.Spell(aliceArc, _alice);
        _stack.Push(aliceSpell);

        var def = CounterfluxFactory.BuildSpellDefinition(
            controller: _alice,
            targetResolver: o => o,
            stack: _stack,
            wasOverloaded: true);

        // No targets — overloaded branch carries no TargetRequests
        // (CR 702.96b — "target" is rewritten to "each").
        def.TargetRequests.Count.Should().Be(0);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard, "opponent spells are countered");
        bobGiant.Zone.Should().Be(ZoneType.Graveyard, "opponent spells are countered");
        _stack.GetAll().Should().Contain(aliceSpell,
            because: "the controller's own spell is spared (CR 109.5)");
        aliceArc.Zone.Should().NotBe(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ResolveAgainst(Majik.Core.Spells.ISpell target, bool wasOverloaded)
    {
        var def = CounterfluxFactory.BuildSpellDefinition(
            _alice, o => o, _stack, wasOverloaded);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }
}

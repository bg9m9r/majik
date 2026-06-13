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
/// Tests for Disrupting Shoal (Betrayers of Kamigawa, {X}{U}{U}, Instant — Arcane).
///
/// Oracle:
///   "You may exile a blue card with mana value X from your hand rather
///    than pay this spell's mana cost.
///    Counter target spell if its mana value is X."
///
/// Covers:
///   - Card identity: Instant — Arcane, {X}{U}{U}, blue, owner/controller.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: HasVariableX=true, one 1..1 "target spell" request.
///   - Pitch alt-cost (blue) CanCastFor validation (colour / MV / hand / owner / self).
///   - End-to-end pitch cast: exile a blue MV-N card, target a MV-N spell → countered.
///   - Pitch cast: target a MV-(N+1) spell → NOT countered (CR 608.2b).
///   - Full-mana cast with X declared: matching-MV spell → countered.
/// </summary>
[Trait("Color", "U")]
public class DisruptingShoalFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DisruptingShoalFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity / dispatch ─────────────────────────────────────────────────

    [Fact]
    public void Create_IsInstantArcane_AtXUU_Blue()
    {
        var shoal = DisruptingShoalFactory.Create(_alice);

        shoal.Name.Should().Be("Disrupting Shoal");
        shoal.HasType(CardType.Instant).Should().BeTrue();
        shoal.HasSubtype(CardSubtype.Arcane).Should().BeTrue(
            "Disrupting Shoal is Instant — Arcane (CR 205.3k)");
        shoal.ManaCost.Should().Be("{X}{U}{U}");
        CardColors.GetColors(shoal).Should().Contain(ManaColor.Blue);
        shoal.Owner.Should().BeSameAs(_alice);
        shoal.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDisruptingShoalShape()
    {
        var dispatched = NamedCardFactory.Create("Disrupting Shoal", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Disrupting Shoal");
        dispatched.ManaCost.Should().Be("{X}{U}{U}");
    }

    // ── SpellDefinition shape ───────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasVariableX_OneTargetSpellRequest()
    {
        var def = DisruptingShoalFactory.BuildSpellDefinition(o => o, _stack);

        def.HasVariableX.Should().BeTrue("X is declared at cast time");
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── Alt-cost CanCastFor (blue pitch) ────────────────────────────────────

    [Fact]
    public void AltCost_BlueCardInHand_CorrectMV_ReturnsTrue()
    {
        var shoal = ShoalInHand(_alice);
        // {2}{U} = MV 3. X = 3.
        var blueCard = BlueCardInHand(_alice, "{2}{U}");
        var cost = DisruptingShoalFactory.BuildAlternativeCost(x: 3, blueCard);

        cost.CanCastFor(shoal, _alice).Should().BeTrue();
        cost.AlternativeManaCost.Should().Be(ManaCost.Zero,
            "the exile is the entire cost — no mana is owed (CR 118.9)");
    }

    [Fact]
    public void AltCost_NonBlueCard_ReturnsFalse()
    {
        var shoal = ShoalInHand(_alice);
        var greenCard = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        greenCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(greenCard);

        var cost = DisruptingShoalFactory.BuildAlternativeCost(x: 2, greenCard);

        cost.CanCastFor(shoal, _alice).Should().BeFalse("exiled card must be blue");
    }

    [Fact]
    public void AltCost_BlueCardWithWrongMV_ReturnsFalse()
    {
        var shoal = ShoalInHand(_alice);
        // Card MV is 2 ({1}{U}), but X was declared as 3.
        var blueCard = BlueCardInHand(_alice, "{1}{U}");
        var cost = DisruptingShoalFactory.BuildAlternativeCost(x: 3, blueCard);

        cost.CanCastFor(shoal, _alice).Should().BeFalse(
            "exiled card MV must equal the declared X");
    }

    // ── End-to-end pitch cast — counter on MV match ─────────────────────────

    [Fact]
    public async Task PitchCast_TargetSpellWithMatchingMV_CountersIt()
    {
        // Alice pitches a blue MV-3 card ({2}{U}), X = 3, targets Bob's MV-3 spell.
        var shoal = ShoalInHand(_alice);
        var pitchCard = BlueCardInHand(_alice, "{2}{U}"); // MV 3
        const int x = 3;

        var pitchCost = DisruptingShoalFactory.BuildAlternativeCost(x, pitchCard);
        pitchCost.CanCastFor(shoal, _alice).Should().BeTrue(
            "blue card with MV 3 qualifies when X = 3");

        // Bob casts a mv-3 spell ({1}{U}{U} = mv 3).
        var bobSpellCard = new Instant("Cancel", "{1}{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobSpellCard, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueX(x);
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, shoal,
            DisruptingShoalFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        bobSpellCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "Disrupting Shoal counters a spell whose mana value equals X (= 3)");
        pitchCard.Zone.Should().Be(ZoneType.Exile,
            because: "the pitched blue card is exiled as the alternative cost (CR 118.9)");
    }

    // ── Pitch cast — no counter on MV mismatch ──────────────────────────────

    [Fact]
    public async Task PitchCast_TargetSpellWithDifferentMV_DoesNotCounter()
    {
        // Alice pitches a blue MV-3 card, X = 3, but targets a MV-4 spell.
        var shoal = ShoalInHand(_alice);
        var pitchCard = BlueCardInHand(_alice, "{2}{U}"); // MV 3
        const int x = 3;

        var pitchCost = DisruptingShoalFactory.BuildAlternativeCost(x, pitchCard);

        // Bob casts a mv-4 spell ({2}{U}{U} = mv 4).
        var bobSpellCard = new Instant("Mystic Confluence", "{2}{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobSpellCard, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueX(x);
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, shoal,
            DisruptingShoalFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        bobSpellCard.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Disrupting Shoal does NOT counter a spell whose mana value (4) differs from X (3) — CR 608.2b");
        // The pitched card is still exiled — the cost was paid even though the
        // counter fizzled on an illegal-at-resolution target.
        pitchCard.Zone.Should().Be(ZoneType.Exile);
    }

    // ── Full-mana cast with X declared ──────────────────────────────────────

    [Fact]
    public async Task RegularManaCast_WithXDeclared_CountersMatchingMVSpell()
    {
        // Casting via mana (not pitch): declare X = 2, counter a mv-2 spell.
        var shoal = ShoalInHand(_alice);
        const int x = 2;

        // Bob casts a mv-2 spell ({1}{U} = mv 2).
        var bobSpellCard = new Instant("Negate", "{1}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobSpellCard, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueX(x);
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, shoal,
            DisruptingShoalFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx);

        _resolver.ResolveTop(_stack);

        bobSpellCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "casting for X = 2 (paid in mana) counters a mana-value-2 spell");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Instant ShoalInHand(Player owner)
    {
        var c = DisruptingShoalFactory.Create(owner);
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }

    private Instant BlueCardInHand(Player owner, string manaCost)
    {
        var c = new Instant("Blue Card", manaCost) { Owner = owner };
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }
}

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
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Soul Spike (Coldsnap, {3}{B}{B}, Instant).
///
/// Oracle:
///   "You may exile two black cards from your hand rather than pay this
///    spell's mana cost.
///    Soul Spike deals 4 damage to any target and you gain 4 life."
///
/// Covers:
///   - Card identity (Instant, {3}{B}{B}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve targeting opponent → 4 damage + 4 life to controller.
///   - Resolve targeting creature → 4 damage + 4 life to controller.
///   - Resolve targeting planeswalker → 4 loyalty removed + 4 life.
///   - Pitch alt-cost (two black cards) exiles both cards on resolve.
///   - SpellDefinition shape (1..1 any target).
/// </summary>
public class SoulSpikeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SoulSpikeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void SoulSpike_IsInstant_At3BB()
    {
        var ss = SoulSpikeFactory.Create(_alice);

        ss.Name.Should().Be("Soul Spike");
        ss.ManaCost.Should().Be("{3}{B}{B}");
        ss.HasType(CardType.Instant).Should().BeTrue();
        ss.Owner.Should().BeSameAs(_alice);
        ss.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesSoulSpike()
    {
        var card = NamedCardFactory.Create("Soul Spike", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Soul Spike");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{B}{B}");
    }

    // ── Resolution — damage + lifegain ───────────────────────────────────

    [Fact]
    public async Task SoulSpike_TargetingOpponent_Deals4Damage_AndController_Gains4Life()
    {
        var bobStart = _bob.LifeTotal;
        var aliceStart = _alice.LifeTotal;

        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStart - 4, "4 damage to the targeted player");
        _alice.LifeTotal.Should().Be(aliceStart + 4, "controller gains 4 life");
    }

    [Fact]
    public async Task SoulSpike_TargetingCreature_Deals4Damage_AndController_Gains4Life()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);

        var aliceStart = _alice.LifeTotal;

        await CastAndResolveTargeting(bear);

        bear.Damage.Should().Be(4, "4 damage to the targeted creature");
        _alice.LifeTotal.Should().Be(aliceStart + 4, "controller gains 4 life");
    }

    [Fact]
    public async Task SoulSpike_TargetingPlaneswalker_RemovesLoyalty_AndController_Gains4Life()
    {
        // Damage to a planeswalker → loyalty removal (CR 119.3 / 306.7).
        var pw = new Planeswalker(
            "Liliana of the Veil",
            "{1}{B}{B}",
            startingLoyalty: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Liliana });
        pw.SetOwner(_alice);
        pw.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(pw);

        var aliceStart = _alice.LifeTotal;

        await CastAndResolveTargeting(pw);

        // Loyalty floors at 0 — starting 3 - 4 damage = 0.
        pw.Loyalty.Should().Be(0, "4 loyalty removed from a 3-loyalty planeswalker (floors at 0)");
        _alice.LifeTotal.Should().Be(aliceStart + 4, "controller gains 4 life");
    }

    // ── Pitch alt-cost ───────────────────────────────────────────────────

    [Fact]
    public async Task SoulSpike_PitchTwoBlackCards_ExilesBoth_StillDealsDamageAndGainsLife()
    {
        // Pitch cost: exile two black cards from hand instead of {3}{B}{B}.
        var ss = SoulSpikeInHand(_alice);

        var pitch1 = new Creature("Phyrexian Negator", "{2}{B}", 5, 5);
        pitch1.SetOwner(_alice);
        pitch1.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch1);

        var pitch2 = new Sorcery("Duress", "{B}");
        pitch2.SetOwner(_alice);
        pitch2.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch2);

        var pitchCost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Black, pitch1, pitch2);

        var bobStart = _bob.LifeTotal;
        var aliceStart = _alice.LifeTotal;

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, ss,
            SoulSpikeFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx,
            alternativeCost: pitchCost);

        ss.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();

        // Both pitched cards in exile.
        pitch1.Zone.Should().Be(ZoneType.Exile);
        pitch2.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(new ICard[] { pitch1, pitch2 });

        // Damage + life still applied.
        _bob.LifeTotal.Should().Be(bobStart - 4);
        _alice.LifeTotal.Should().Be(aliceStart + 4);
    }

    [Fact]
    public void PitchCost_RejectsSameCardTwice()
    {
        var pitch = new Sorcery("Duress", "{B}");
        pitch.SetOwner(_alice);
        pitch.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch);

        var cost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Black, pitch, pitch);

        var ss = SoulSpikeFactory.Create(_alice);
        cost.CanCastFor(ss, _alice).Should().BeFalse(
            "the two pitched cards must be distinct references");
    }

    [Fact]
    public void PitchCost_RejectsNonBlackCards()
    {
        var blackCard = new Sorcery("Duress", "{B}");
        blackCard.SetOwner(_alice);
        blackCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blackCard);

        var redCard = new Instant("Lightning Bolt", "{R}");
        redCard.SetOwner(_alice);
        redCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(redCard);

        var cost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Black, blackCard, redCard);

        var ss = SoulSpikeFactory.Create(_alice);
        cost.CanCastFor(ss, _alice).Should().BeFalse(
            "both pitched cards must be the required colour");
    }

    [Fact]
    public void PitchCost_RejectsSpellItselfAsPitch()
    {
        // The spell being cast cannot also serve as one of the pitched cards.
        var ss = SoulSpikeFactory.Create(_alice);
        ss.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ss);

        var blackCard = new Sorcery("Duress", "{B}");
        blackCard.SetOwner(_alice);
        blackCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blackCard);

        var cost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Black, ss, blackCard);

        cost.CanCastFor(ss, _alice).Should().BeFalse(
            "the spell being cast is not a legal pitch candidate");
    }

    // ── SpellDefinition shape ───────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_DeclaresSingleAnyTargetRequest()
    {
        var def = SoulSpikeFactory.BuildSpellDefinition(_alice, t => t);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private Instant SoulSpikeInHand(Player owner)
    {
        var ss = SoulSpikeFactory.Create(owner);
        ss.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(ss);
        return ss;
    }

    private async Task CastAndResolveTargeting(object target)
    {
        var ss = SoulSpikeInHand(_alice);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, ss,
            SoulSpikeFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        ss.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="NightshadeAssassinFactory"/> — Creature — Human Assassin
/// {2}{B}{B} 2/1 (Time Spiral) with First strike + Madness {1}{B} and a single
/// ETB trigger:
///   "When this creature enters, you may reveal X black cards in your hand. If
///    you do, target creature gets -X/-X until end of turn."
///
/// Covers the reveal-count-X pay-down: X = the number of black cards the
/// controller reveals from hand, fed as the (negated) delta into the −X/−X
/// pump.
/// </summary>
[Trait("Color", "B")]
public class NightshadeAssassinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Permanent card, Player owner)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        // Wire a continuous-effects service so PumpUntilEndOfTurnEffect applies.
        card.ActiveEffects = new Majik.Core.Effects.ContinuousEffectsService();
    }

    private static Creature BlackCard(string name) =>
        new(name, "{B}", 1, 1);

    private static Creature WhiteCard(string name) =>
        new(name, "{W}", 1, 1);

    [Fact]
    public void Identity_Creature_HumanAssassin_2_1_At2BB_FirstStrike()
    {
        var assassin = NightshadeAssassinFactory.Create(_alice);

        assassin.Name.Should().Be("Nightshade Assassin");
        assassin.ManaCost.Should().Be("{2}{B}{B}");
        assassin.HasType(CardType.Creature).Should().BeTrue();
        assassin.HasSubtype(CardSubtype.Human).Should().BeTrue();
        assassin.HasSubtype(CardSubtype.Assassin).Should().BeTrue();
        assassin.BasePower.Should().Be(2);
        assassin.BaseToughness.Should().Be(1);
        assassin.Owner.Should().BeSameAs(_alice);

        // CR 702.7 — First strike from the JSON keywords array.
        CombatAbilities.HasFirstStrike(assassin).Should().BeTrue();
    }

    [Fact]
    public void HasSingleEtbTrigger_WithOneCreatureTarget()
    {
        var assassin = NightshadeAssassinFactory.Create(_alice);

        var triggers = assassin.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
        etb.TargetRequests[0].Description.Should().Contain("creature");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public async Task Resolve_RevealTwoBlackCards_TargetGetsMinus2Minus2()
    {
        // Alice's hand: 2 black cards + 1 white card. Reveal both black ⇒ X = 2.
        _alice.Zones.Hand.AddCard(BlackCard("Swamp Walker"));
        _alice.Zones.Hand.AddCard(BlackCard("Dark Ritualist"));
        _alice.Zones.Hand.AddCard(WhiteCard("Cleric")); // not revealable

        var bear = new Creature("Grizzly Bears", "{1}{G}", 3, 3);
        PutOnBattlefield(bear, _bob);

        var agent = new ScriptedAgent();
        // Reveal ALL offered (black) candidates.
        agent.QueueChoice(cands => cands);
        AgentRegistry.Set(_alice, agent);

        var assassin = NightshadeAssassinFactory.Create(_alice);
        PutOnBattlefield(assassin, _alice);

        var etb = assassin.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        var rc = ResolutionContext.For(
            controller: _alice, agent: agent, game: null, chosenTargets: null);
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(rc);

        // −2/−2 on the 3/3 → 1/1, still alive.
        bear.Power.Should().Be(1);
        bear.Toughness.Should().Be(1);

        AgentRegistry.Clear();
    }

    [Fact]
    public async Task Resolve_RevealOneBlackCard_TargetGetsMinus1Minus1()
    {
        _alice.Zones.Hand.AddCard(BlackCard("A"));
        _alice.Zones.Hand.AddCard(BlackCard("B"));

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(bear, _bob);

        var agent = new ScriptedAgent();
        // Reveal exactly ONE black card (the first offered).
        agent.QueueChoice(cands => cands.Take(1).ToList());
        AgentRegistry.Set(_alice, agent);

        var assassin = NightshadeAssassinFactory.Create(_alice);
        PutOnBattlefield(assassin, _alice);

        var etb = assassin.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        var rc = ResolutionContext.For(
            controller: _alice, agent: agent, game: null, chosenTargets: null);
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(rc);

        bear.Power.Should().Be(1);
        bear.Toughness.Should().Be(1);

        AgentRegistry.Clear();
    }

    [Fact]
    public async Task Resolve_RevealNothing_NoEffect()
    {
        _alice.Zones.Hand.AddCard(BlackCard("A"));

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(bear, _bob);

        var agent = new ScriptedAgent();
        // Decline — reveal zero black cards (the "may"/X=0 path).
        agent.QueueChoice(_ => System.Array.Empty<object>());
        AgentRegistry.Set(_alice, agent);

        var assassin = NightshadeAssassinFactory.Create(_alice);
        PutOnBattlefield(assassin, _alice);

        var etb = assassin.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        var rc = ResolutionContext.For(
            controller: _alice, agent: agent, game: null, chosenTargets: null);
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(rc);

        // X = 0 ⇒ no −X/−X.
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);

        AgentRegistry.Clear();
    }

    [Fact]
    public async Task Resolve_LethalDebuff_ReducesToughnessToZeroOrBelow()
    {
        // 3 black cards revealed ⇒ X = 3, a 2/2 becomes -1/-1 (dies to SBAs in a
        // real game; here we assert the modifier drives toughness ≤ 0).
        _alice.Zones.Hand.AddCard(BlackCard("A"));
        _alice.Zones.Hand.AddCard(BlackCard("B"));
        _alice.Zones.Hand.AddCard(BlackCard("C"));

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(bear, _bob);

        var agent = new ScriptedAgent();
        agent.QueueChoice(cands => cands);
        AgentRegistry.Set(_alice, agent);

        var assassin = NightshadeAssassinFactory.Create(_alice);
        PutOnBattlefield(assassin, _alice);

        var etb = assassin.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        var rc = ResolutionContext.For(
            controller: _alice, agent: agent, game: null, chosenTargets: null);
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(rc);

        bear.Toughness.Should().BeLessThanOrEqualTo(0);

        AgentRegistry.Clear();
    }

    [Fact]
    public async Task Resolve_NoAgent_RevealsAllBlackCards_MaxX()
    {
        // Single-arg dispatcher / no-agent posture: reveal ALL black cards.
        _alice.Zones.Hand.AddCard(BlackCard("A"));
        _alice.Zones.Hand.AddCard(BlackCard("B"));
        _alice.Zones.Hand.AddCard(WhiteCard("W"));

        var bear = new Creature("Grizzly Bears", "{1}{G}", 4, 4);
        PutOnBattlefield(bear, _bob);

        var assassin = NightshadeAssassinFactory.Create(_alice);
        PutOnBattlefield(assassin, _alice);

        var etb = assassin.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        // Legacy synchronous Execute() ⇒ ResolutionContext.Legacy (no agent) ⇒
        // reveals all 2 black cards ⇒ X = 2.
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
    }

    [Fact]
    public async Task Resolve_RevealedCards_PublishCardRevealedEvents()
    {
        _alice.Zones.Hand.AddCard(BlackCard("A"));
        _alice.Zones.Hand.AddCard(BlackCard("B"));

        var bear = new Creature("Grizzly Bears", "{1}{G}", 3, 3);
        PutOnBattlefield(bear, _bob);

        var bus = new EventBus();
        var reveals = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(reveals.Add);

        var agent = new ScriptedAgent();
        agent.QueueChoice(cands => cands);
        AgentRegistry.Set(_alice, agent);

        var assassin = NightshadeAssassinFactory.Create(_alice, bus, triggers: null);
        PutOnBattlefield(assassin, _alice);

        var etb = assassin.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        var rc = ResolutionContext.For(
            controller: _alice, agent: agent, game: null, chosenTargets: null);
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(rc);

        reveals.Should().HaveCount(2);

        AgentRegistry.Clear();
    }

    [Fact]
    public async Task Resolve_TargetLeftBattlefield_NoPump_DespiteReveal()
    {
        _alice.Zones.Hand.AddCard(BlackCard("A"));

        var bear = new Creature("Grizzly Bears", "{1}{G}", 3, 3);
        PutOnBattlefield(bear, _bob);

        var bus = new EventBus();
        var reveals = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(reveals.Add);
        var agent = new ScriptedAgent();
        agent.QueueChoice(cands => cands);
        AgentRegistry.Set(_alice, agent);

        var assassin = NightshadeAssassinFactory.Create(_alice, bus, triggers: null);
        PutOnBattlefield(assassin, _alice);

        var etb = assassin.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        // Target leaves the battlefield between trigger pick and resolution.
        _bob.Zones.Battlefield.RemoveCard(bear);
        _bob.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        var rc = ResolutionContext.For(
            controller: _alice, agent: agent, game: null, chosenTargets: null);
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(rc);

        // CR 608.2b — the −X/−X fizzles even though the reveal happened.
        reveals.Should().HaveCount(1);

        AgentRegistry.Clear();
    }
}

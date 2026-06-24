using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KutzilsFlankerFactory"/>.
///
/// Kutzil's Flanker ({2}{W}) — Creature — Cat Warrior 3/1. Oracle text
/// (verified against Scryfall):
///   "Flash
///    When this creature enters, choose one —
///    • Put a +1/+1 counter on this creature for each creature that left the
///      battlefield under your control this turn.
///    • You gain 2 life and scry 2.
///    • Exile target player's graveyard."
///
/// CR 700.2d — modal "Choose one —" ETB trigger with three modes.
/// Covers ONLY the card's unique behaviour (the three modes) plus a single
/// identity assert; CardFactoryContractTests covers dispatch + well-formedness.
/// </summary>
[Trait("Color", "W")]
public class KutzilsFlankerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // ─── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void KutzilsFlanker_Identity()
    {
        var c = KutzilsFlankerFactory.Create(_alice);

        c.Name.Should().Be("Kutzil's Flanker");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue("Kutzil's Flanker is a Cat");
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue("Kutzil's Flanker is a Warrior");
        c.ManaCost.Should().Be("{2}{W}");
        c.ManaCostValue.TotalValue.Should().Be(3, "CR 202.3 — {2}{W} has mana value 3");
        CardColors.GetColors(c).Should().Contain(ManaColor.White);

        // Flash (CR 702.8) — declarative JSON keyword.
        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flash");
    }

    // ─── ETB trigger shape ────────────────────────────────────────────────────

    [Fact]
    public void KutzilsFlanker_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = KutzilsFlankerFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one modal ETB trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.ModeRequest.Should().NotBeNull("modal 'choose one —' ETB (CR 700.2d)");
        etb.ModeRequest!.Modes.Should().HaveCount(3);
    }

    // ─── Mode 0 — +1/+1 counter per creature that left your control this turn ──

    [Fact]
    public async Task KutzilsFlanker_Mode0_AddsCounterPerCreatureThatLeftYourControl()
    {
        // Two creatures left under Alice's control this turn.
        var turnState = new TurnState();
        turnState.RecordCreatureDied(_alice);
        turnState.RecordCreatureDied(_alice);
        // One left under Bob's control — must NOT count for Alice.
        turnState.RecordCreatureDied(_bob);

        var flanker = KutzilsFlankerFactory.Create(_alice, KutzilsFlankerFactory.ModeCounters);

        var etb = flanker.Abilities.OfType<TriggeredAbility>().Single();
        var ctx = BuildContext(_alice, turnState);
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(ctx);

        flanker.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "two creatures left the battlefield under Alice's control this turn");
    }

    [Fact]
    public async Task KutzilsFlanker_Mode0_NoCreaturesLeft_AddsNoCounters()
    {
        var turnState = new TurnState(); // nothing left this turn

        var flanker = KutzilsFlankerFactory.Create(_alice, KutzilsFlankerFactory.ModeCounters);

        var etb = flanker.Abilities.OfType<TriggeredAbility>().Single();
        var ctx = BuildContext(_alice, turnState);
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(ctx);

        flanker.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no creature left Alice's control this turn — no counters");
    }

    // ─── Mode 1 — gain 2 life and scry 2 ──────────────────────────────────────

    [Fact]
    public async Task KutzilsFlanker_Mode1_GainsTwoLife_AndScries()
    {
        var alice = new Player("Alice", 20);

        var cardA = new Creature("CardA", "{W}", 1, 1);
        var cardB = new Creature("CardB", "{G}", 1, 1);
        alice.Zones.Library.AddCard(cardA); // top
        alice.Zones.Library.AddCard(cardB); // second

        // Scry decision: keep cardA on top, send cardB to bottom.
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { cardB },
            TopOrder: new[] { cardA }));
        AgentRegistry.Set(alice, agent);

        var flanker = KutzilsFlankerFactory.Create(alice, KutzilsFlankerFactory.ModeLifeAndScry);

        var etb = flanker.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(BuildContext(alice, null));

        alice.LifeTotal.Should().Be(22, "mode 1 gains controller exactly 2 life (CR 119.3)");

        var lib = alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(2, "scry 2 only reorders; no card leaves the library");
        lib.First().Should().BeSameAs(cardA, "cardA was kept on top by the scry");
    }

    // ─── Mode 2 — exile target player's graveyard ─────────────────────────────

    [Fact]
    public async Task KutzilsFlanker_Mode2_ExilesTargetPlayersGraveyard()
    {
        // Bob has 3 cards in his graveyard.
        var c1 = SeedGraveyard("Lightning Bolt", "{R}", _bob);
        var c2 = SeedGraveyard("Thoughtseize", "{B}", _bob);
        var c3 = SeedGraveyard("Path to Exile", "{W}", _bob);

        // Alice's graveyard must be untouched when Bob is targeted.
        var aliceCard = SeedGraveyard("Brainstorm", "{U}", _alice);

        var flanker = KutzilsFlankerFactory.Create(_alice, KutzilsFlankerFactory.ModeExileGraveyard);

        var etb = flanker.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(BuildContext(_alice, null));

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "mode 2 exiles all cards from the target player's graveyard");
        _bob.Zones.Exile.GetCards().Should().HaveCount(3,
            "all 3 of Bob's graveyard cards moved to exile");
        new[] { c1, c2, c3 }.Should().OnlyContain(c => c.Zone == ZoneType.Exile);

        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCard,
            "Alice's graveyard is untouched when Bob is targeted");
    }

    [Fact]
    public async Task KutzilsFlanker_Mode2_EmptyGraveyard_IsCleanNoOp()
    {
        var flanker = KutzilsFlankerFactory.Create(_alice, KutzilsFlankerFactory.ModeExileGraveyard);

        var etb = flanker.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        // Empty graveyard — clean no-op (CR 608.2b), no throw.
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(BuildContext(_alice, null));

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static ResolutionContext BuildContext(Player controller, TurnState? turnState)
    {
        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var game = new GameContext(
            self: controller,
            allPlayers: new[] { controller },
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: stack,
            landPlayAvailable: true,
            turnState: turnState);
        return ResolutionContext.For(controller, agent: null, game, chosenTargets: null);
    }

    private static Card SeedGraveyard(string name, string cost, Player owner)
    {
        var card = new Instant(name, cost);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }
}

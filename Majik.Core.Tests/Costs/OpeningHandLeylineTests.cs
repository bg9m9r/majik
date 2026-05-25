using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Tests for the opening-hand alt-cost surface (CR 702.95) shared by
/// every Leyline-cycle card:
///   - <see cref="OpeningHandCheckEvent"/> publishing on game start.
///   - <see cref="OpeningHandLeylineAlternativeCost"/> prompting / move
///     semantics.
///   - <see cref="LeylineOfTheVoidFactory"/> +
///     <see cref="LeylineOfSanctityFactory"/> +
///     <see cref="LeylineOfAnguishFactory"/> +
///     <see cref="LeylineOfLightningFactory"/> +
///     <see cref="LeylineOfCombustionFactory"/> all marked with the
///     <c>OpeningHandLeyline</c> keyword.
/// </summary>
public class OpeningHandLeylineTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---- Marker tests ----

    [Fact]
    public void EveryLeyline_CarriesOpeningHandKeywordMarker()
    {
        AssertHasLeylineMarker(LeylineOfTheVoidFactory.Create(_alice));
        AssertHasLeylineMarker(LeylineOfSanctityFactory.Create(_alice));
        AssertHasLeylineMarker(LeylineOfAnguishFactory.Create(_alice));
        AssertHasLeylineMarker(LeylineOfLightningFactory.Create(_alice));
        AssertHasLeylineMarker(LeylineOfCombustionFactory.Create(_alice));
    }

    [Fact]
    public void LeylineOfSanctity_HasIdentity()
    {
        var c = LeylineOfSanctityFactory.Create(_alice);
        c.Name.Should().Be("Leyline of Sanctity");
        c.ManaCost.Should().Be("{2}{W}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LeylineOfAnguish_HasIdentity()
    {
        var c = LeylineOfAnguishFactory.Create(_alice);
        c.Name.Should().Be("Leyline of Anguish");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void LeylineOfLightning_HasIdentity()
    {
        var c = LeylineOfLightningFactory.Create(_alice);
        c.Name.Should().Be("Leyline of Lightning");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void LeylineOfCombustion_HasIdentity()
    {
        var c = LeylineOfCombustionFactory.Create(_alice);
        c.Name.Should().Be("Leyline of Combustion");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_DispatchesAllLeylines()
    {
        NamedCardFactory.Create("Leyline of Sanctity", _alice).Name.Should().Be("Leyline of Sanctity");
        NamedCardFactory.Create("Leyline of Anguish", _alice).Name.Should().Be("Leyline of Anguish");
        NamedCardFactory.Create("Leyline of Lightning", _alice).Name.Should().Be("Leyline of Lightning");
        NamedCardFactory.Create("Leyline of Combustion", _alice).Name.Should().Be("Leyline of Combustion");
    }

    // ---- Subscriber behaviour ----

    [Fact]
    public async Task Subscriber_AcceptedPrompt_MovesCardHandToBattlefield()
    {
        var leyline = LeylineOfTheVoidFactory.Create(_alice);
        PlaceInHand(leyline, _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        var (subscriber, _) = BuildSubscriber(_alice, agent);
        var evt = new OpeningHandCheckEvent(_alice, _alice.Zones.Hand.GetCards().ToList());

        await subscriber.HandleAsync(evt);

        leyline.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Hand.GetCards().Should().NotContain(leyline);
        _alice.Zones.Battlefield.GetCards().Should().Contain(leyline);
    }

    [Fact]
    public async Task Subscriber_DeclinedPrompt_LeavesCardInHand()
    {
        var leyline = LeylineOfTheVoidFactory.Create(_alice);
        PlaceInHand(leyline, _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        var (subscriber, _) = BuildSubscriber(_alice, agent);
        var evt = new OpeningHandCheckEvent(_alice, _alice.Zones.Hand.GetCards().ToList());

        await subscriber.HandleAsync(evt);

        leyline.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(leyline);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(leyline);
    }

    [Fact]
    public async Task Subscriber_MultipleLeylinesInHand_PromptsForEach()
    {
        var void1 = LeylineOfTheVoidFactory.Create(_alice);
        var sanctity = LeylineOfSanctityFactory.Create(_alice);
        var lightning = LeylineOfLightningFactory.Create(_alice);
        PlaceInHand(void1, _alice);
        PlaceInHand(sanctity, _alice);
        PlaceInHand(lightning, _alice);

        var agent = new ScriptedAgent();
        // Yes, no, yes — Void & Lightning to battlefield, Sanctity stays.
        agent.QueueYesNo(true);
        agent.QueueYesNo(false);
        agent.QueueYesNo(true);

        var (subscriber, _) = BuildSubscriber(_alice, agent);
        var evt = new OpeningHandCheckEvent(_alice, _alice.Zones.Hand.GetCards().ToList());

        await subscriber.HandleAsync(evt);

        void1.Zone.Should().Be(ZoneType.Battlefield);
        sanctity.Zone.Should().Be(ZoneType.Hand);
        lightning.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task Subscriber_IgnoresNonLeylineCards()
    {
        // Non-Leyline cards in hand are not prompted on. A vanilla
        // creature in the opening hand alongside a Leyline must not
        // consume a yes/no from the queue.
        var leyline = LeylineOfTheVoidFactory.Create(_alice);
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        PlaceInHand(leyline, _alice);
        PlaceInHand(bear, _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // Just one — only Leyline should be prompted.

        var (subscriber, _) = BuildSubscriber(_alice, agent);
        var evt = new OpeningHandCheckEvent(_alice, _alice.Zones.Hand.GetCards().ToList());

        await subscriber.HandleAsync(evt);

        leyline.Zone.Should().Be(ZoneType.Battlefield);
        bear.Zone.Should().Be(ZoneType.Hand,
            "vanilla cards aren't tagged with the Leyline keyword and shouldn't be prompted");
    }

    [Fact]
    public async Task Subscriber_StampsWasCastFalse_OnPutToBattlefield()
    {
        // CR 113.5 / Containment Priest interaction — Leylines that
        // begin the game on the battlefield are NOT cast. The
        // subscriber must stamp WasCast=false so cast-gated replacements
        // see the correct posture.
        var leyline = LeylineOfTheVoidFactory.Create(_alice);
        PlaceInHand(leyline, _alice);

        // Pre-stamp WasCast=true to make sure the subscriber actively
        // clears it (rather than just relying on the default).
        leyline.SetWasCast(true);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        var (subscriber, _) = BuildSubscriber(_alice, agent);
        var evt = new OpeningHandCheckEvent(_alice, _alice.Zones.Hand.GetCards().ToList());

        await subscriber.HandleAsync(evt);

        leyline.WasCast.Should().BeFalse(
            "Leyline 'put onto the battlefield' is not a cast");
    }

    // ---- Event publication via bus ----

    [Fact]
    public async Task EventBus_DispatchesOpeningHandCheck_ToSubscriber()
    {
        var leyline = LeylineOfTheVoidFactory.Create(_alice);
        PlaceInHand(leyline, _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        var bus = new EventBus();
        var (subscriber, _) = BuildSubscriber(_alice, agent);
        subscriber.Attach(bus);

        await bus.PublishAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        leyline.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ---- GameDriver-level integration ----

    [Fact]
    public async Task GameDriver_PublishesOpeningHandCheck_OncePerPlayer_AfterMulligan()
    {
        var bus = new EventBus();
        var checks = new List<OpeningHandCheckEvent>();
        bus.Subscribe<OpeningHandCheckEvent>(evt => checks.Add(evt));

        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var priority = new PriorityManager(new List<Player> { _alice, _bob }, stack, bus, triggers);

        Seed(_alice, 30);
        Seed(_bob, 30);

        var driver = new GameDriver(
            new[] { _alice, _bob },
            new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = new DeterministicBotAgent(),
                [_bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            new GameRandom(7),
            eventBus: bus);

        await driver.RunGameAsync(maxTurns: 1);

        // One check per player — no duplicates, no skips.
        checks.Should().HaveCount(2);
        checks.Select(c => c.Player).Should().Contain(new[] { _alice, _bob });
        // Each carries a 7-card opening hand (no mulligans taken by the
        // deterministic bot in this fixture; the snapshot reflects the
        // hand size at event-publish time, after London-mulligan
        // resolution).
        checks.Should().AllSatisfy(c => c.OpeningHand.Count.Should().Be(7));
    }

    [Fact]
    public async Task GameDriver_LeylineInOpeningHand_LandsOnBattlefieldBeforeTurnOne()
    {
        // Real game-start flow: Alice's entire library is Leylines (so
        // post-shuffle, post-draw the opening hand is guaranteed to be
        // 7 Leylines); the scripted agent answers yes when prompted;
        // by turn 0 every Leyline is on her battlefield.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var priority = new PriorityManager(new List<Player> { _alice, _bob }, stack, bus, triggers);

        // Seed Alice with 30 Leylines — every library slot is a Leyline
        // so the post-shuffle opening hand is guaranteed to contain 7.
        for (var i = 0; i < 30; i++)
        {
            var leyline = LeylineOfTheVoidFactory.Create(_alice);
            _alice.Zones.Library.AddCard(leyline);
            leyline.SetZone(ZoneType.Library);
        }
        Seed(_bob, 30);

        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Keep);
        for (var i = 0; i < 7; i++) agent.QueueYesNo(true);

        var driver = new GameDriver(
            new[] { _alice, _bob },
            new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = agent,
                [_bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            new GameRandom(11),
            eventBus: bus);

        await driver.RunGameAsync(maxTurns: 0);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Leyline of the Void")
            .Should().Be(7, "every accepted Leyline in the opening hand should land on the battlefield at game start");
    }

    [Fact]
    public async Task GameDriver_LeylineAfterMulligan_StillPromptedFromPostMulliganHand()
    {
        // CR 103.5 — the opening-hand check fires AFTER mulligan
        // resolution. Use an all-Leyline library so we don't depend on
        // shuffle ordering: mulligan once, then keep — every card in
        // every drawn hand is a Leyline, and the check fires once
        // against the post-mulligan hand (size 6 after bottoming 1).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var priority = new PriorityManager(new List<Player> { _alice, _bob }, stack, bus, triggers);

        var checks = new List<OpeningHandCheckEvent>();
        bus.Subscribe<OpeningHandCheckEvent>(evt => checks.Add(evt));

        for (var i = 0; i < 30; i++)
        {
            var leyline = LeylineOfSanctityFactory.Create(_alice);
            _alice.Zones.Library.AddCard(leyline);
            leyline.SetZone(ZoneType.Library);
        }
        Seed(_bob, 30);

        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Mulligan); // first hand → mulligan
        agent.QueueMulligan(MulliganDecision.Keep);     // second hand → keep
        agent.QueueCardsToBottom(hand => new[] { hand[0] }); // bottom 1
        // 6 yes-answers (one per Leyline post-bottom).
        for (var i = 0; i < 6; i++) agent.QueueYesNo(true);

        var driver = new GameDriver(
            new[] { _alice, _bob },
            new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = agent,
                [_bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            new GameRandom(13),
            eventBus: bus);

        await driver.RunGameAsync(maxTurns: 0);

        // Exactly one check fires per player, AFTER mulligan resolution.
        var aliceCheck = checks.Single(c => c.Player == _alice);
        aliceCheck.OpeningHand.Count.Should().Be(6,
            "post-mulligan hand was 6 cards (drew 7, bottomed 1) — event fires AFTER bottom");

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Leyline of Sanctity")
            .Should().Be(6, "opening-hand check fires AFTER mulligan resolves");
    }

    // ---- Helpers ----

    private static void Seed(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }


    private (OpeningHandLeylineAlternativeCost, ZoneService) BuildSubscriber(
        Player player, IPlayerAgent agent)
    {
        var zoneService = new ZoneService();
        var agents = new Dictionary<Player, IPlayerAgent> { [player] = agent };
        return (new OpeningHandLeylineAlternativeCost(zoneService, agents), zoneService);
    }

    private static void PlaceInHand(ICard card, Player owner)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
    }

    private static void AssertHasLeylineMarker(ICard card)
    {
        card.Abilities.OfType<Majik.Core.Abilities.KeywordAbility>()
            .Any(k => k.Keyword == OpeningHandLeylineAlternativeCost.LeylineKeyword)
            .Should().BeTrue($"{card.Name} should carry the OpeningHandLeyline marker");
    }
}

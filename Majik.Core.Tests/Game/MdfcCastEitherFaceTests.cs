using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 712.3 / 712.4 — real "cast either face" for Modal Double-Faced Cards
/// (deferral #3). Sink into Stupor // Soporific Springs is the canonical
/// spell-front + land-back MDFC. The controller CHOOSES which face to play
/// at cast time; the chosen face's cost / type / effect applies; the
/// permanent enters as the chosen face; NO transform happens (CR 712.4).
/// </summary>
public class MdfcCastEitherFaceTests : IDisposable
{
    public MdfcCastEitherFaceTests() => AgentRegistry.Clear();
    public void Dispose() => AgentRegistry.Clear();

    private static GameContext Ctx(Player self, params Player[] all) =>
        new(self, all.Length > 0 ? all : new[] { self }, self, 1,
            PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack(new EventBus()));

    // =====================================================================
    // Card model — the front face carries a castable back-face descriptor
    // =====================================================================

    [Fact]
    public void SinkIntoStupor_FrontCard_OffersCastableBackFace()
    {
        var alice = new Player("Alice", 20);
        var sink = SinkIntoStuporFactory.Create(alice);

        sink.MdfcState.Should().NotBeNull();
        sink.MdfcState!.CanCastEitherFace.Should().BeTrue(
            "the front face must expose a castable back-face descriptor (CR 712.3)");
        sink.MdfcState!.CastableBackFace.Should().NotBeNull();
        sink.MdfcState!.CastableBackFace!.IsLand.Should().BeTrue(
            "Soporific Springs is a land back face");
        sink.MdfcState!.CastableBackFace!.Name.Should().Be("Soporific Springs");
    }

    [Fact]
    public void BackLandFaceCard_DoesNotOfferAnotherFace()
    {
        // The materialized back-face land is already the chosen face — it must
        // not itself offer a further cast-either-face choice.
        var alice = new Player("Alice", 20);
        var land = SoporificSpringsFactory.Create(alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.IsBackFace.Should().BeTrue();
        land.MdfcState!.CanCastEitherFace.Should().BeFalse();
    }

    // =====================================================================
    // ResolveFaceAsync — the face prompt (CR 712.3)
    // =====================================================================

    [Fact]
    public async Task ResolveFace_ChoosingFront_ReturnsNull_CastFrontNormally()
    {
        var alice = new Player("Alice", 20);
        var sink = SinkIntoStuporFactory.Create(alice);
        var agent = new ScriptedAgent();
        agent.QueueChoiceIndex(0); // front

        var chosen = await MdfcCastFlow.ResolveFaceAsync(sink, alice, agent, Ctx(alice));

        chosen.Should().BeNull("front face → cast the front card normally");
    }

    [Fact]
    public async Task ResolveFace_ChoosingBack_ReturnsBackLandFace()
    {
        var alice = new Player("Alice", 20);
        var sink = SinkIntoStuporFactory.Create(alice);
        var agent = new ScriptedAgent();
        agent.QueueChoiceIndex(1); // back

        var chosen = await MdfcCastFlow.ResolveFaceAsync(sink, alice, agent, Ctx(alice));

        chosen.Should().NotBeNull();
        chosen!.IsLand.Should().BeTrue();
        chosen.Name.Should().Be("Soporific Springs");
    }

    [Fact]
    public async Task ResolveFace_NonMdfc_ReturnsNull()
    {
        var alice = new Player("Alice", 20);
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = alice };
        var agent = new ScriptedAgent();

        var chosen = await MdfcCastFlow.ResolveFaceAsync(bolt, alice, agent, Ctx(alice));

        chosen.Should().BeNull("a single-faced card is never an MDFC face choice");
    }

    [Fact]
    public async Task ResolveFace_PromptOffersBothFaces()
    {
        var alice = new Player("Alice", 20);
        var sink = SinkIntoStuporFactory.Create(alice);
        var agent = new ScriptedAgent();
        IReadOnlyList<object>? seen = null;
        agent.QueueChoice(candidates => { seen = candidates; return new[] { candidates[0] }; });

        await MdfcCastFlow.ResolveFaceAsync(sink, alice, agent, Ctx(alice));

        seen.Should().NotBeNull();
        seen!.Should().HaveCount(2, "both faces are offered (CR 712.3)");
        seen!.OfType<MdfcFaceChoice>().Select(f => f.FaceName)
            .Should().BeEquivalentTo(new[] { "Sink into Stupor", "Soporific Springs" });
    }

    // =====================================================================
    // PlayBackLandFace — the land enters as the chosen face, no transform
    // =====================================================================

    [Fact]
    public void PlayBackLand_RemovesFrontFromHand_LandEntersBattlefield_NoTransform()
    {
        var bus = new EventBus();
        var replacements = new ReplacementBus();
        var zones = new ZoneService(bus, replacements);
        var tracker = new LandDropTracker();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // pay 3 life → enters untapped
        AgentRegistry.Set(alice, agent);

        var sink = SinkIntoStuporFactory.Create(alice);
        sink.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(sink);
        var backFace = sink.MdfcState!.CastableBackFace!;

        var played = MdfcCastFlow.PlayBackLandFace(
            frontCard: sink, backFace: backFace, caster: alice,
            zones: zones, replacements: replacements, landDropTracker: tracker,
            activePlayer: alice, phase: PhaseStateType.PreCombatMain, stackEmpty: true);

        played.Should().BeTrue();

        // The front (Sink) card never enters — only the chosen back land does.
        alice.Zones.Hand.GetCards().Should().NotContain(sink);
        var landsOnField = alice.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Land)).ToList();
        landsOnField.Should().ContainSingle();
        landsOnField[0].Name.Should().Be("Soporific Springs");
        landsOnField[0].Should().BeOfType<Land>();
        landsOnField[0].HasType(CardType.Instant).Should().BeFalse(
            "the battlefield permanent is the LAND face, not the instant front");

        // CR 712.4 — no transform; the land is its own object, not a flipped
        // Sink. Its MdfcState reads as the back face (Soporific Springs).
        var land = (Card)landsOnField[0];
        land.MdfcState!.ActiveFaceName.Should().Be("Soporific Springs");

        // Land-for-turn consumed (CR 305.2).
        tracker.DropsUsedThisTurn(alice).Should().Be(1);
    }

    [Fact]
    public void PlayBackLand_EntersUntapped_WhenPayingThreeLife()
    {
        var bus = new EventBus();
        var replacements = new ReplacementBus();
        var zones = new ZoneService(bus, replacements);
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var sink = SinkIntoStuporFactory.Create(alice);
        sink.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(sink);

        MdfcCastFlow.PlayBackLandFace(
            sink, sink.MdfcState!.CastableBackFace!, alice, zones, replacements,
            landDropTracker: null, alice, PhaseStateType.PreCombatMain, true);

        var land = alice.Zones.Battlefield.GetCards().OfType<Land>().Single();
        land.IsTapped.Should().BeFalse("paid 3 life → enters untapped");
        alice.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void PlayBackLand_EntersTapped_WhenDecliningThreeLife()
    {
        var bus = new EventBus();
        var replacements = new ReplacementBus();
        var zones = new ZoneService(bus, replacements);
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var sink = SinkIntoStuporFactory.Create(alice);
        sink.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(sink);

        MdfcCastFlow.PlayBackLandFace(
            sink, sink.MdfcState!.CastableBackFace!, alice, zones, replacements,
            landDropTracker: null, alice, PhaseStateType.PreCombatMain, true);

        var land = alice.Zones.Battlefield.GetCards().OfType<Land>().Single();
        land.IsTapped.Should().BeTrue("declined → enters tapped");
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void PlayBackLand_TapsForBlue()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var alice = new Player("Alice", 20);

        var sink = SinkIntoStuporFactory.Create(alice);
        sink.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(sink);

        MdfcCastFlow.PlayBackLandFace(
            sink, sink.MdfcState!.CastableBackFace!, alice, zones, replacements: null,
            landDropTracker: null, alice, PhaseStateType.PreCombatMain, true);

        var land = alice.Zones.Battlefield.GetCards().OfType<Land>().Single();
        var mana = land.Abilities.OfType<ManaAbility>().Single();
        mana.ManaGenerated.Blue.Should().BeGreaterThan(0, "Soporific Springs taps for {U}");
    }

    [Fact]
    public void PlayBackLand_RejectedWhenLandAlreadyPlayedThisTurn()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var tracker = new LandDropTracker();
        var alice = new Player("Alice", 20);
        tracker.RecordLandPlayed(alice); // already used this turn's land drop

        var sink = SinkIntoStuporFactory.Create(alice);
        sink.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(sink);

        var played = MdfcCastFlow.PlayBackLandFace(
            sink, sink.MdfcState!.CastableBackFace!, alice, zones, replacements: null,
            landDropTracker: tracker, alice, PhaseStateType.PreCombatMain, true);

        played.Should().BeFalse("CR 305.2 — second land drop is illegal");
        alice.Zones.Hand.GetCards().Should().Contain(sink, "front card stays in hand");
        alice.Zones.Battlefield.GetCards().OfType<Land>().Should().BeEmpty();
    }

    // =====================================================================
    // Integration — full cast through TurnDriver.DispatchCast
    // =====================================================================

    [Fact]
    public async Task TurnDriver_CastSink_ChoosingBack_PlaysSoporificSpringsAsLand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var replacements = new ReplacementBus();
        var zones = new ZoneService(bus, replacements);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);
        var tracker = new LandDropTracker();

        var sink = SinkIntoStuporFactory.Create(alice);
        sink.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(sink);

        foreach (var p in players)
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Island", p);
                c.SetZone(ZoneType.Library);
                p.Zones.Library.AddCard(c);
            }

        // Cast Sink only at Alice's main phase (a land back face can only be
        // played there); choose the BACK face, pay 3 life so it enters
        // untapped. Pass on every other window.
        var inner = new ScriptedAgent();
        inner.QueueChoiceIndex(1);  // back face (Soporific Springs)
        inner.QueueYesNo(true);     // pay 3 life
        var aliceAgent = new MainPhaseCastAgent(inner, sink, alice);
        AgentRegistry.Set(alice, aliceAgent);

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 20; i++) bobAgent.QueuePriority(PriorityAction.Pass);
        AgentRegistry.Set(bob, bobAgent);

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent> { [alice] = aliceAgent, [bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            replacements: replacements,
            landDropTracker: tracker);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        // The land entered; the Sink instant never hit the stack.
        var land = alice.Zones.Battlefield.GetCards().OfType<Land>()
            .SingleOrDefault(c => c.Name == "Soporific Springs");
        land.Should().NotBeNull("choosing the back face plays Soporific Springs as a land");
        land!.IsTapped.Should().BeFalse("paid 3 life → untapped");
        alice.LifeTotal.Should().Be(17);
        stack.Count.Should().Be(0, "a land play uses no stack (CR 305.1)");
        tracker.DropsUsedThisTurn(alice).Should().Be(1);
        alice.Zones.Hand.GetCards().Should().NotContain(sink,
            "the front Sink card was consumed when the back land was played");
    }

    [Fact]
    public async Task TurnDriver_CastSink_ChoosingFront_CastsBounceSpellOntoStack()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var replacements = new ReplacementBus();
        var zones = new ZoneService(bus, replacements);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);
        var tracker = new LandDropTracker();

        var sink = SinkIntoStuporFactory.Create(alice);
        sink.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(sink);

        // Bob's nonland permanent — the front-face bounce target.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        // Float {1}{U}{U} for Alice so the front cost is payable.
        alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("{1}{U}{U}"));

        foreach (var p in players)
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Island", p);
                c.SetZone(ZoneType.Library);
                p.Zones.Library.AddCard(c);
            }

        // Front-face SpellDefinition resolver (mirrors the production
        // ScryfallCardFactory.LookupSpellDefinition wiring for Sink).
        Func<ICard, Player, Majik.Core.Stack.Stack?, SpellDefinition?> defResolver =
            (card, caster, stk) => card.Name == "Sink into Stupor"
                ? SinkIntoStuporFactory.BuildDefinition(caster, raw => raw, stk, zones)
                : null;

        var inner = new ScriptedAgent();
        inner.QueueChoiceIndex(0);                 // FRONT face
        inner.QueueTargets(new object[] { bobBear }); // bounce target
        var aliceAgent = new MainPhaseCastAgent(inner, sink, alice);
        AgentRegistry.Set(alice, aliceAgent);

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 20; i++) bobAgent.QueuePriority(PriorityAction.Pass);
        AgentRegistry.Set(bob, bobAgent);

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent> { [alice] = aliceAgent, [bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            spellDefinitionResolver: defResolver,
            replacements: replacements,
            landDropTracker: tracker);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        // Front face cast + resolved: Bob's bear was bounced to his hand.
        bob.Zones.Hand.GetCards().Should().Contain(bobBear,
            "choosing the front face casts the bounce spell (CR 712.3)");
        bob.Zones.Battlefield.GetCards().Should().NotContain(bobBear);
        // No land was played (the front face is a spell, not a land).
        alice.Zones.Battlefield.GetCards().OfType<Land>()
            .Should().NotContain(c => c.Name == "Soporific Springs");
        tracker.DropsUsedThisTurn(alice).Should().Be(0);
    }
}

/// <summary>
/// Test agent that proposes a single <see cref="PriorityAction.CastSpell"/>
/// of the given card exactly once, only when its player has priority on its
/// own main phase with an empty stack (so an MDFC land back face is legal to
/// play), and passes on every other window. All other prompts delegate to an
/// inner <see cref="ScriptedAgent"/>.
/// </summary>
internal sealed class MainPhaseCastAgent : IPlayerAgent
{
    private readonly ScriptedAgent _inner;
    private readonly ICard _card;
    private readonly Player _self;
    private bool _cast;

    public MainPhaseCastAgent(ScriptedAgent inner, ICard card, Player self)
    {
        _inner = inner;
        _card = card;
        _self = self;
    }

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
    {
        if (!_cast
            && ReferenceEquals(ctx.ActivePlayer, _self)
            && ctx.CurrentPhase is { } phase && phase.IsMain())
        {
            _cast = true;
            return Task.FromResult<PriorityAction>(
                new PriorityAction.CastSpell(_card, Array.Empty<object>()));
        }
        return Task.FromResult(PriorityAction.Pass);
    }

    public Task<IReadOnlyList<object>> ChooseAsync(GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        => _inner.ChooseAsync(ctx, req, ct);
    public Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
        => _inner.ChooseYesNoAsync(question, intent, ct);
    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => _inner.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);
    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => _inner.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);
    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => _inner.ChooseTargetsAsync(ctx, request, ct);
    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => _inner.ChooseXAsync(ctx, source, ct);
    public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
        => _inner.ChooseModeAsync(ctx, modes, modeIntents, ct);
    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => _inner.OrderTriggersAsync(ctx, mine, ct);
    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
        => _inner.ChooseManaSourcesAsync(ctx, cost, ct);
    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => _inner.DeclareAttackersAsync(ctx, eligibleAttackers, ct);
    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        => _inner.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct);
    public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => _inner.ChooseScryDecisionAsync(ctx, peeked, ct);
    public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => _inner.ChooseSurveilDecisionAsync(ctx, peeked, ct);
}

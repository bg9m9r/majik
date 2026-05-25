using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Regression coverage for the <c>ChooseYesNoAsync</c> +
/// <c>ChooseFromHandAsync</c> agent-prompt MVP. Verifies:
///
///   1. The default interface implementation (no overrides) returns sane
///      values keyed off <see cref="BotIntent"/>.
///   2. <see cref="HeuristicBotAgent"/>'s overrides apply intent-aware
///      heuristics (Discard → highest-MV; CheatIntoPlay → biggest creature;
///      LoseLife / DiscardCost / CostToDecline → decline).
///   3. <see cref="ScriptedAgent"/> emits queued responses and falls back
///      to deterministic defaults when no entry is queued.
///   4. Eight retrofitted factory pilots consult the agent at resolution
///      time (vs. the legacy deterministic fallback when no agent is
///      supplied).
///
/// Spec: <c>feat/yesno-fromhand-prompts</c>.
/// </summary>
public class AgentPromptYesNoFromHandTests
{
    // =======================================================================
    // 1. Default-implementation interface methods (no overrides).
    //
    // A bare IPlayerAgent that only implements ChoosePriorityActionAsync
    // (everything else falling through to default methods) must accept
    // upside intents, decline downside intents, and return the first
    // candidate from ChooseFromHandAsync.
    // =======================================================================

    /// <summary>Bare-bones agent that omits everything the default
    /// implementations cover.</summary>
    private sealed class BareAgent : IPlayerAgent
    {
        public Task<PriorityAction> ChoosePriorityActionAsync(
            Majik.Core.Game.GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);

        public Task<MulliganDecision> ChooseMulliganAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken,
            CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);

        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            Majik.Core.Game.GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());

        public Task<int> ChooseXAsync(
            Majik.Core.Game.GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> ChooseModeAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>>(mine.ToList());

        public Task<ManaPayment> ChooseManaSourcesAsync(
            Majik.Core.Game.GameContext ctx, Majik.Core.ValueObjects.ManaCost cost,
            CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);

        public Task<Majik.Core.Players.Agents.CombatPlan> DeclareAttackersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> eligibleAttackers,
            CancellationToken ct = default)
            => Task.FromResult(Majik.Core.Players.Agents.CombatPlan.None);

        public Task<BlockPlan> DeclareBlockersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> attackers,
            IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);

        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(
            Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>()));

        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
            Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                ToGraveyard: peeked.ToList(),
                TopOrder: Array.Empty<ICard>()));
    }

    [Fact]
    public async Task DefaultImpl_YesNo_UpsideIntent_ReturnsTrue()
    {
        IPlayerAgent agent = new BareAgent();
        (await agent.ChooseYesNoAsync("draw 1?", BotIntent.CardAdvantage)).Should().BeTrue();
        (await agent.ChooseYesNoAsync("buff?", BotIntent.Buff)).Should().BeTrue();
        (await agent.ChooseYesNoAsync("tutor?", BotIntent.Tutor)).Should().BeTrue();
        (await agent.ChooseYesNoAsync("cheat into play?", BotIntent.CheatIntoPlay)).Should().BeTrue();
    }

    [Fact]
    public async Task DefaultImpl_YesNo_DownsideIntent_ReturnsFalse()
    {
        IPlayerAgent agent = new BareAgent();
        (await agent.ChooseYesNoAsync("lose 3 life?", BotIntent.LoseLife)).Should().BeFalse();
        (await agent.ChooseYesNoAsync("discard a card?", BotIntent.DiscardCost)).Should().BeFalse();
        (await agent.ChooseYesNoAsync("pay {2}?", BotIntent.CostToDecline)).Should().BeFalse();
    }

    [Fact]
    public async Task DefaultImpl_YesNo_Neutral_AutoAccepts()
    {
        // Legacy posture: factories pre-dating the prompt auto-accepted.
        IPlayerAgent agent = new BareAgent();
        (await agent.ChooseYesNoAsync("do the thing?", BotIntent.None)).Should().BeTrue();
    }

    [Fact]
    public async Task DefaultImpl_FromHand_ReturnsFirstCandidate()
    {
        IPlayerAgent agent = new BareAgent();
        var p = new Player("P", 20);
        var a = new Creature("A", "{1}", 1, 1);
        var b = new Creature("B", "{2}{R}", 2, 2);
        var pick = await agent.ChooseFromHandAsync(
            p, new ICard[] { a, b }, BotIntent.CheatIntoPlay);
        pick.Should().BeSameAs(a);
    }

    [Fact]
    public async Task DefaultImpl_FromHand_EmptyCandidates_ReturnsNull()
    {
        IPlayerAgent agent = new BareAgent();
        var p = new Player("P", 20);
        var pick = await agent.ChooseFromHandAsync(
            p, Array.Empty<ICard>(), BotIntent.Discard);
        pick.Should().BeNull();
    }

    // =======================================================================
    // 2. HeuristicBotAgent overrides.
    // =======================================================================

    [Fact]
    public async Task HeuristicBot_FromHand_Discard_PitchesHighestManaValue()
    {
        var bot = new HeuristicBotAgent();
        var p = new Player("P", 20);
        var cheap = new Creature("Cheap", "{1}", 1, 1);
        var expensive = new Creature("Expensive", "{4}{B}{B}", 6, 6);
        var mid = new Creature("Mid", "{2}{R}", 3, 3);

        var pick = await bot.ChooseFromHandAsync(
            p, new ICard[] { cheap, expensive, mid }, BotIntent.Discard);

        pick.Should().BeSameAs(expensive);
    }

    [Fact]
    public async Task HeuristicBot_FromHand_CheatIntoPlay_PicksBiggest()
    {
        var bot = new HeuristicBotAgent();
        var p = new Player("P", 20);
        var small = new Creature("Small", "{1}", 1, 1);
        var fatty = new Creature("Fatty", "{7}", 10, 10);

        var pick = await bot.ChooseFromHandAsync(
            p, new ICard[] { small, fatty }, BotIntent.CheatIntoPlay);

        pick.Should().BeSameAs(fatty);
    }

    [Fact]
    public async Task HeuristicBot_YesNo_CostToDecline_Declines()
    {
        var bot = new HeuristicBotAgent();
        // Esper Sentinel "unless you pay X" shape.
        (await bot.ChooseYesNoAsync("Pay {2} to suppress draw?", BotIntent.CostToDecline))
            .Should().BeFalse();
    }

    [Fact]
    public async Task HeuristicBot_YesNo_CheatIntoPlay_Accepts()
    {
        var bot = new HeuristicBotAgent();
        (await bot.ChooseYesNoAsync("Put creature from hand?", BotIntent.CheatIntoPlay))
            .Should().BeTrue();
    }

    // =======================================================================
    // 3. ScriptedAgent queue + fallback.
    // =======================================================================

    [Fact]
    public async Task ScriptedAgent_YesNo_QueuedAnswerReturned()
    {
        var s = new ScriptedAgent();
        s.QueueYesNo(false);
        s.QueueYesNo(true);
        (await s.ChooseYesNoAsync("q1", BotIntent.None)).Should().BeFalse();
        (await s.ChooseYesNoAsync("q2", BotIntent.None)).Should().BeTrue();
    }

    [Fact]
    public async Task ScriptedAgent_YesNo_UnqueuedThrows()
    {
        var s = new ScriptedAgent();
        Func<Task> act = async () => await s.ChooseYesNoAsync("q", BotIntent.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ScriptedAgent_FromHand_QueuedChooserReturned()
    {
        var s = new ScriptedAgent();
        var a = new Instant("A", "{R}");
        var b = new Instant("B", "{U}");
        s.QueueFromHand(b);
        var pick = await s.ChooseFromHandAsync(
            new Player("P", 20), new ICard[] { a, b }, BotIntent.Discard);
        pick.Should().BeSameAs(b);
    }

    [Fact]
    public async Task ScriptedAgent_FromHand_NoQueue_FallsBackToFirst()
    {
        var s = new ScriptedAgent();
        var a = new Instant("A", "{R}");
        var b = new Instant("B", "{U}");
        var pick = await s.ChooseFromHandAsync(
            new Player("P", 20), new ICard[] { a, b }, BotIntent.CheatIntoPlay);
        pick.Should().BeSameAs(a);
    }

    [Fact]
    public async Task ScriptedAgent_FromHand_DeclineReturnsNull()
    {
        var s = new ScriptedAgent();
        s.QueueFromHand((ICard?)null);
        var pick = await s.ChooseFromHandAsync(
            new Player("P", 20),
            new ICard[] { new Instant("A", "{R}") },
            BotIntent.CheatIntoPlay);
        pick.Should().BeNull();
    }

    // =======================================================================
    // 4. Factory retrofit regressions.
    //
    // Each touched factory has one regression test confirming the prompt is
    // consulted when an IPlayerAgent is wired. Pre-existing factory tests
    // (which pass agent=null) cover the deterministic v1 fallback.
    // =======================================================================

    // -- Sneak Attack -------------------------------------------------------

    [Fact]
    public void SneakAttack_AgentDeclinesYesNo_NoPutFromHand()
    {
        var alice = new Player("A", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);

        var grizzly = new Creature("Grizzly", "{1}{G}", 2, 2);
        grizzly.SetOwner(alice);
        alice.Zones.Hand.AddCard(grizzly);

        var sneak = SneakAttackFactory.Create(alice, zones, triggers, BotForYesNo(false));
        alice.Zones.Battlefield.AddCard(sneak);

        var ab = sneak.Abilities.OfType<Majik.Core.Abilities.ActivatedAbility>().First();
        foreach (var ef in ab.Effects) ef.Execute();

        grizzly.Zone.Should().Be(ZoneType.Hand,
            "declined ChooseYesNoAsync must keep the creature in hand");
    }

    [Fact]
    public void SneakAttack_AgentPicksSpecificCreature_PutFromHandPicksThat()
    {
        var alice = new Player("A", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);

        var small = new Creature("Small", "{G}", 1, 1);
        var fatty = new Creature("Fatty", "{7}", 9, 9);
        small.SetOwner(alice); fatty.SetOwner(alice);
        alice.Zones.Hand.AddCard(small);
        alice.Zones.Hand.AddCard(fatty);

        // Agent says yes, then picks fatty.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        agent.QueueFromHand(fatty);

        var sneak = SneakAttackFactory.Create(alice, zones, triggers, agent);
        alice.Zones.Battlefield.AddCard(sneak);

        var ab = sneak.Abilities.OfType<Majik.Core.Abilities.ActivatedAbility>().First();
        foreach (var ef in ab.Effects) ef.Execute();

        fatty.Zone.Should().Be(ZoneType.Battlefield);
        small.Zone.Should().Be(ZoneType.Hand);
    }

    // -- Through the Breach -------------------------------------------------

    [Fact]
    public void ThroughTheBreach_AgentDeclines_NoCreaturePutIn()
    {
        var alice = new Player("A", 20);
        var creature = new Creature("Fatty", "{8}", 9, 9);
        creature.SetOwner(alice);
        alice.Zones.Hand.AddCard(creature);

        var effects = ThroughTheBreachFactory.BuildResolveEffect(
            alice, zoneService: null, triggers: null, agent: BotForYesNo(false));
        foreach (var ef in effects) ef.Execute();

        creature.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void ThroughTheBreach_AgentAccepts_PutsCreatureOntoBattlefield()
    {
        var alice = new Player("A", 20);
        var creature = new Creature("Fatty", "{8}", 9, 9);
        creature.SetOwner(alice);
        alice.Zones.Hand.AddCard(creature);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        agent.QueueFromHand(creature);

        var effects = ThroughTheBreachFactory.BuildResolveEffect(
            alice, zoneService: null, triggers: null, agent: agent);
        foreach (var ef in effects) ef.Execute();

        creature.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -- Faithless Looting --------------------------------------------------

    [Fact]
    public void FaithlessLooting_AgentChoosesDiscardPicks()
    {
        var alice = new Player("A", 20);
        // Seed library and hand. Draw will pull 2 from library; agent
        // picks specific cards to discard.
        var lib1 = new Instant("Lib1", "{R}");
        var lib2 = new Instant("Lib2", "{R}");
        lib1.SetOwner(alice); lib2.SetOwner(alice);
        alice.Zones.Library.AddCard(lib1);
        alice.Zones.Library.AddCard(lib2);

        var keepThis = new Creature("Keep", "{1}{W}", 2, 2);
        var pitchA = new Instant("PitchA", "{R}");
        var pitchB = new Instant("PitchB", "{U}");
        foreach (var c in new ICard[] { keepThis, pitchA, pitchB })
        {
            c.SetOwner(alice);
            alice.Zones.Hand.AddCard(c);
        }

        var agent = new ScriptedAgent();
        // After 2 draws: hand has Keep, PitchA, PitchB, Lib1, Lib2.
        // Discard #1 → PitchA. Discard #2 → PitchB.
        agent.QueueFromHand(pitchA);
        agent.QueueFromHand(pitchB);

        var effects = FaithlessLootingFactory.BuildResolveEffect(alice, agent);
        foreach (var ef in effects) ef.Execute();

        pitchA.Zone.Should().Be(ZoneType.Graveyard);
        pitchB.Zone.Should().Be(ZoneType.Graveyard);
        keepThis.Zone.Should().Be(ZoneType.Hand);
    }

    // -- Liliana of the Veil ------------------------------------------------

    [Fact]
    public void LilianaPlus1_AgentChoosesDiscardPick()
    {
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);

        var aliceFirst = new Instant("AFirst", "{R}");
        var alicePitch = new Instant("APitch", "{4}{R}{R}");
        var bobFirst = new Instant("BFirst", "{U}");
        var bobPitch = new Instant("BPitch", "{5}{U}{U}");

        aliceFirst.SetOwner(alice); alicePitch.SetOwner(alice);
        bobFirst.SetOwner(bob); bobPitch.SetOwner(bob);

        alice.Zones.Hand.AddCard(aliceFirst);
        alice.Zones.Hand.AddCard(alicePitch);
        bob.Zones.Hand.AddCard(bobFirst);
        bob.Zones.Hand.AddCard(bobPitch);

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueFromHand(alicePitch);   // alice picks her expensive card
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueFromHand(bobPitch);       // bob picks his

        IPlayerAgent? Selector(Player p) =>
            ReferenceEquals(p, alice) ? aliceAgent :
            ReferenceEquals(p, bob) ? bobAgent : null;

        var liliana = LilianaOfTheVeilFactory.Create(
            alice,
            allPlayersResolver: () => new[] { alice, bob },
            agentSelector: Selector);

        var plus1 = liliana.Abilities
            .OfType<Majik.Core.Abilities.LoyaltyAbility>()
            .First(a => a.LoyaltyChange == +1);
        plus1.Activate();

        alicePitch.Zone.Should().Be(ZoneType.Graveyard);
        bobPitch.Zone.Should().Be(ZoneType.Graveyard);
        aliceFirst.Zone.Should().Be(ZoneType.Hand);
        bobFirst.Zone.Should().Be(ZoneType.Hand);
    }

    // -- Show and Tell ------------------------------------------------------

    [Fact]
    public void ShowAndTell_AgentDeclinesYesNo_PlayerDoesNotPutInPermanent()
    {
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);
        var alicePerm = new Creature("AliceFatty", "{8}", 9, 9);
        var bobPerm = new Creature("BobFatty", "{7}", 8, 8);
        alicePerm.SetOwner(alice); bobPerm.SetOwner(bob);
        alice.Zones.Hand.AddCard(alicePerm);
        bob.Zones.Hand.AddCard(bobPerm);

        // Alice's agent declines; Bob's agent accepts.
        IPlayerAgent? Selector(Player p) =>
            ReferenceEquals(p, alice) ? BotForYesNo(false) : BotForYesNo(true);

        var effects = ShowAndTellFactory.BuildResolveEffect(
            new[] { alice, bob }, zoneService: null, picker: null,
            agentSelector: Selector);
        foreach (var ef in effects) ef.Execute();

        alicePerm.Zone.Should().Be(ZoneType.Hand);
        bobPerm.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -- Aether Gust --------------------------------------------------------

    [Fact]
    public void AetherGust_AgentChoosesTopOrBottom()
    {
        var alice = new Player("A", 20);
        // Permanent target on battlefield.
        var target = new Creature("Goblin", "{R}", 1, 1);
        target.SetOwner(alice);
        target.SetController(alice);
        target.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(target);

        // Library has an existing card so top vs bottom is observable.
        var lib0 = new Instant("Lib0", "{U}");
        lib0.SetOwner(alice);
        alice.Zones.Library.AddCard(lib0);

        // Agent says "top".
        var def = AetherGustFactory.BuildDefinition(
            targetResolver: o => o,
            stack: null,
            topChooser: null,
            agentSelector: _ => BotForYesNo(true));

        var bound = new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: 0,
            X: 0,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        var effects = def.EffectFactory(bound);
        foreach (var ef in effects) ef.Execute();

        // Target should land on top (index 0).
        alice.Zones.Library.GetCards().First().Should().BeSameAs(target);
    }

    // -- Esper Sentinel -----------------------------------------------------

    [Fact]
    public void EsperSentinel_OpponentDeclinesTax_ControllerDraws()
    {
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);

        // Library card so the draw is observable.
        var lib = new Instant("Drawn", "{R}");
        lib.SetOwner(alice);
        alice.Zones.Library.AddCard(lib);

        // Bob declines paying the tax.
        var bobAgent = BotForYesNo(false);
        IPlayerAgent? Selector(Player p) =>
            ReferenceEquals(p, bob) ? bobAgent : null;

        var sentinel = EsperSentinelFactory.Create(alice, bus, triggers, Selector);
        // Place sentinel + give alice 1 creature on board so X = 1.
        sentinel.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(sentinel);

        // Drive a noncreature cast by Bob to trip the trigger.
        var bobSpellCard = new Instant("Bolt", "{R}");
        bobSpellCard.SetOwner(bob);
        var spell = new Majik.Core.Spells.Spell(
            card: bobSpellCard, controller: bob,
            effects: Array.Empty<Majik.Core.Abilities.IEffect>());
        bus.Publish(new Majik.Core.Domain.DomainEvents.SpellCastEvent(spell));

        // Trip the trigger's resolve effect directly.
        var trigger = sentinel.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .First();
        foreach (var ef in trigger.Effects) ef.Execute();

        lib.Zone.Should().Be(ZoneType.Hand,
            "declined CostToDecline prompt → controller draws 1");
    }

    // -- Arclight Phoenix ---------------------------------------------------

    [Fact]
    public void ArclightPhoenix_AgentDeclinesReturn_StaysInGraveyard()
    {
        var alice = new Player("A", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);

        var phx = ArclightPhoenixFactory.Create(alice, bus, triggers, BotForYesNo(false));
        phx.SetZone(ZoneType.Graveyard);
        alice.Zones.Graveyard.AddCard(phx);

        // Bypass the cast-count gate by driving the effect directly under a
        // controlled-condition assumption. Push three spell-cast events so
        // the closure trips ≥3.
        for (var i = 0; i < 3; i++)
        {
            var card = new Instant("Cantrip", "{R}");
            card.SetOwner(alice);
            var spell = new Majik.Core.Spells.Spell(
                card: card, controller: alice,
                effects: Array.Empty<Majik.Core.Abilities.IEffect>());
            bus.Publish(new Majik.Core.Domain.DomainEvents.SpellCastEvent(spell));
        }

        var trigger = phx.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .First();
        foreach (var ef in trigger.Effects) ef.Execute();

        phx.Zone.Should().Be(ZoneType.Graveyard,
            "declined ChooseYesNoAsync(Reanimate) must not return the Phoenix");
    }

    // -- Bloodghast ---------------------------------------------------------

    [Fact]
    public void Bloodghast_AgentDeclinesLandfallReturn_StaysInGraveyard()
    {
        var alice = new Player("A", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);

        var gh = BloodghastFactory.Create(
            alice,
            effects: null, zoneService: null, triggers: triggers,
            opponentLifeProvider: null,
            agent: BotForYesNo(false));
        gh.SetZone(ZoneType.Graveyard);
        alice.Zones.Graveyard.AddCard(gh);

        var trigger = gh.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .First();
        foreach (var ef in trigger.Effects) ef.Execute();

        gh.Zone.Should().Be(ZoneType.Graveyard);
    }

    // =======================================================================
    // Helpers.
    // =======================================================================

    /// <summary>One-shot agent that answers ChooseYesNoAsync with the given
    /// fixed value (with an unbounded supply for any prompt count) and
    /// delegates ChooseFromHandAsync to the deterministic default.</summary>
    private static IPlayerAgent BotForYesNo(bool answer) => new FixedYesNoAgent(answer);

    private sealed class FixedYesNoAgent : IPlayerAgent
    {
        private readonly bool _answer;
        public FixedYesNoAgent(bool answer) { _answer = answer; }

        public Task<bool> ChooseYesNoAsync(
            string question, BotIntent intent, CancellationToken ct = default)
            => Task.FromResult(_answer);

        // Everything else falls through to the IPlayerAgent default
        // implementations or returns no-op shapes.
        public Task<PriorityAction> ChoosePriorityActionAsync(
            Majik.Core.Game.GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken,
            CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            Majik.Core.Game.GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public Task<int> ChooseXAsync(
            Majik.Core.Game.GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<int> ChooseModeAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>>(mine.ToList());
        public Task<ManaPayment> ChooseManaSourcesAsync(
            Majik.Core.Game.GameContext ctx, Majik.Core.ValueObjects.ManaCost cost,
            CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);
        public Task<Majik.Core.Players.Agents.CombatPlan> DeclareAttackersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> eligibleAttackers,
            CancellationToken ct = default)
            => Task.FromResult(Majik.Core.Players.Agents.CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> attackers,
            IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(
            Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked,
            CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
            Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked,
            CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                ToGraveyard: peeked.ToList(),
                TopOrder: Array.Empty<ICard>()));
    }
}

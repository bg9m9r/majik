using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 601.2b / 601.2e / 601.2f / 601.2h — when a variable-X PERMANENT spell is
/// cast via the legacy single-round dispatch path (<c>GameFacade.DispatchCast</c>,
/// reached through <see cref="GameFacade.StartAsync"/>), the {X} must be FOLDED
/// INTO the dispatcher's mana prompt + payment, mirroring the already-fixed
/// <c>TurnDriver.DispatchCast</c> twin.
///
/// <para>Deferral cast-pipeline-601-ordering (b), GameFacade twin: pre-fix this
/// facade path computed the effective cost WITHOUT prompting/folding X (the
/// Vanilla SpellDefinition carries <c>HasVariableX=false</c>), so the agent was
/// prompted for — and the payManaCost callback paid — the X-free printed cost
/// while <see cref="SpellCastFlow.CastAsync"/> folded X into its own
/// totalCost. An X-permanent (Walking Ballista / Hangarback Walker / Endless
/// One) therefore underpaid — X was effectively free on this loop. The fix keys
/// off the card's effective <c>ManaCost.HasX</c>, prompts ChooseXAsync ahead of
/// the mana-source prompt, folds X into the dispatcher cost, and forwards the
/// chosen X to CastAsync so it threads into the resolving effect without a
/// double-prompt.</para>
/// </summary>
public class GameFacadeCastXPaymentTests
{
    [Fact]
    public async Task XPermanent_FacadeDispatchPath_PromptsAndPays_XInclusiveCost_CR601()
    {
        // Endless-One-style "X{R}" creature. Empty decks — the board is wired
        // directly below so we exercise only the cast dispatch.
        var facade = GameFacade.Create(
            "Alice", "Bob",
            aliceDeck: System.Array.Empty<ICard>(),
            bobDeck: System.Array.Empty<ICard>());

        var alice = facade.Alice;
        var bob = facade.Bob;

        // Four Mountains — exactly the X=3 + {R} = 4-mana bill.
        var mountains = new System.Collections.Generic.List<Permanent>();
        for (var i = 0; i < 4; i++)
        {
            var m = (Permanent)NamedCardFactory.Create("Mountain", alice);
            m.SetZone(ZoneType.Battlefield);
            alice.Zones.Battlefield.AddCard(m);
            mountains.Add(m);
        }

        var ballista = new Creature("Ballista", "X{R}", 0, 0) { Owner = alice };
        ballista.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(ballista);

        var inner = new ScriptedAgent();
        inner.QueueX(3);
        inner.QueueMana(new ManaPayment(mountains.Cast<ICard>().ToList()));
        var recorder = new CostRecordingFacadeCastAgent(inner, ballista, alice);
        facade.ReplaceAliceAgent(recorder);

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 30; i++) bobAgent.QueuePriority(PriorityAction.Pass);
        facade.ReplaceBobAgent(bobAgent);

        await facade.StartAsync();

        // CR 601.2f — the cost the agent was prompted for must include X.
        recorder.PromptedCost.Should().NotBeNull();
        recorder.PromptedCost!.Red.Should().Be(1);
        recorder.PromptedCost!.Generic.Should().Be(3,
            "X=3 is folded into the generic portion of the facade dispatcher's " +
            "mana prompt (CR 601.2b/f) — pre-fix the agent was prompted for just {R}");
        recorder.PromptedCost!.TotalValue.Should().Be(4);

        // CR 601.2h — the cast actually happened (left the hand) and all four
        // Mountains paid the X-inclusive cost.
        ballista.Zone.Should().NotBe(ZoneType.Hand,
            "the X-permanent was actually cast, not stranded in hand");
        mountains.Should().OnlyContain(m => m.IsTapped,
            "all four lands pay the X-inclusive cost (CR 601.2h) — X is not free");

        // The agent was asked for X exactly once (no double-prompt across the
        // dispatcher + flow).
        recorder.XPromptCount.Should().Be(1);
    }

    /// <summary>
    /// Wraps the cast-on-main-phase behaviour and records the cost handed to the
    /// mana-source prompt + how many times X was asked, so the test can assert
    /// the facade dispatcher prompts the X-inclusive cost exactly once.
    /// </summary>
    private sealed class CostRecordingFacadeCastAgent : IPlayerAgent
    {
        private readonly MainPhaseCastAgent _inner;

        public Majik.Core.ValueObjects.ManaCost? PromptedCost { get; private set; }
        public int XPromptCount { get; private set; }

        public CostRecordingFacadeCastAgent(ScriptedAgent inner, ICard card, Player self)
            => _inner = new MainPhaseCastAgent(inner, card, self);

        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
        {
            PromptedCost = cost;
            return _inner.ChooseManaSourcesAsync(ctx, cost, ct);
        }

        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        {
            XPromptCount++;
            return _inner.ChooseXAsync(ctx, source, ct);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => _inner.ChoosePriorityActionAsync(ctx, ct);
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
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => _inner.ChooseModeAsync(ctx, modes, modeIntents, ct);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> abilities, CancellationToken ct = default)
            => _inner.OrderTriggersAsync(ctx, abilities, ct);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligible, CancellationToken ct = default)
            => _inner.DeclareAttackersAsync(ctx, eligible, ct);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligible, CancellationToken ct = default)
            => _inner.DeclareBlockersAsync(ctx, attackers, eligible, ct);
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseScryDecisionAsync(ctx, peeked, ct);
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseSurveilDecisionAsync(ctx, peeked, ct);
    }
}

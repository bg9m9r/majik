using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 601.2b / 601.2e / 601.2f / 601.2h — when a variable-X spell is cast via
/// the live dispatch path (TurnDriver.DispatchCast), the {X} must be FOLDED
/// INTO the dispatcher's mana prompt + payment: CR 601.2b announces X, the
/// total cost (CR 601.2f) is the printed cost with X folded into generic, and
/// the mana paid at CR 601.2h must cover that total.
///
/// Deferral (fold-X-into-spell-cast-dispatcher-payment): pre-fix,
/// DispatchCast prompted/paid the printed cost with NO X (X folded to
/// generic 0), while SpellCastFlow.CastAsync computed the X-inclusive
/// totalCost AFTER the dispatcher's prompt and the payManaCost callback
/// deliberately ignored it. The bot's X-spells therefore underpaid — X was
/// effectively free on the dispatch path (Fireball/Hydra-class burn cast
/// for {R}). This pins the X choice AHEAD of the dispatcher mana prompt so
/// the agent is prompted for, and pays, the X-inclusive cost.
/// </summary>
public class TurnDriverCastXPaymentTests
{
    /// <summary>
    /// Fireball-style "X{R}": the bot picks X=3, so the total cost is
    /// {3}{R} (CR 601.2f). The dispatcher must prompt the agent for that
    /// 4-mana cost (not the bare {R}), and pay it — four Mountains tapped.
    /// </summary>
    [Fact]
    public async Task XSpell_DispatchPath_PromptsAndPays_XInclusiveCost_CR601()
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

        // Four Mountains — exactly the X=3 + {R} = 4-mana bill.
        var mountains = new List<Permanent>();
        for (var i = 0; i < 4; i++)
        {
            var m = (Permanent)NamedCardFactory.Create("Mountain", alice);
            m.SetZone(ZoneType.Battlefield);
            alice.Zones.Battlefield.AddCard(m);
            mountains.Add(m);
        }

        var fireball = new Instant("Fireball", "X{R}") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(fireball);

        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Island", p);
                c.SetZone(ZoneType.Library);
                p.Zones.Library.AddCard(c);
            }
        }

        int? effectX = null;
        Func<ICard, Player, Majik.Core.Stack.Stack?, SpellDefinition?> defResolver =
            (card, caster, stk) => card.Name == "Fireball"
                ? new SpellDefinition(
                    Modes: Array.Empty<string>(),
                    HasVariableX: true,
                    TargetRequests: Array.Empty<TargetRequest>(),
                    EffectFactory: p => { effectX = p.X; return Array.Empty<IEffect>(); })
                : null;

        var inner = new ScriptedAgent();
        inner.QueueX(3);
        // The agent taps all four Mountains to cover {3}{R}.
        inner.QueueMana(new ManaPayment(mountains.Cast<ICard>().ToList()));
        var recorder = new CostRecordingCastAgent(inner, fireball, alice);

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 30; i++) bobAgent.QueuePriority(PriorityAction.Pass);

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent> { [alice] = recorder, [bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            spellDefinitionResolver: defResolver,
            replacements: replacements,
            landDropTracker: new LandDropTracker(),
            eventBus: bus);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        // CR 601.2f — the cost the agent was prompted for must include X.
        recorder.PromptedCost.Should().NotBeNull();
        recorder.PromptedCost!.Red.Should().Be(1);
        recorder.PromptedCost!.Generic.Should().Be(3,
            "X=3 is folded into the generic portion of the dispatcher's mana " +
            "prompt (CR 601.2b/f) — pre-fix the bot was prompted for just {R}");
        recorder.PromptedCost!.TotalValue.Should().Be(4);

        // CR 601.2h — the cast actually happened (left the hand) and all four
        // Mountains paid the X-inclusive cost. The spell resolves before the
        // turn ends, so we assert payment + the threaded X rather than a live
        // stack object.
        fireball.Zone.Should().NotBe(ZoneType.Hand,
            "the X-spell was actually cast, not stranded in hand");
        mountains.Should().OnlyContain(m => m.IsTapped,
            "all four lands pay the X-inclusive cost (CR 601.2h) — X is not free");

        // CR 601.2e — the X choice is threaded into the resolving effect.
        effectX.Should().Be(3,
            "X=3 reaches ChosenSpellParams.X at resolution (no double-prompt " +
            "reset it to a different value)");

        // The agent was asked for X exactly once (no double-prompt across the
        // dispatcher + flow).
        recorder.XPromptCount.Should().Be(1);
    }

    /// <summary>
    /// Wraps the cast-on-main-phase behaviour and records the cost handed to
    /// the mana-source prompt + how many times X was asked, so the test can
    /// assert the dispatcher prompts the X-inclusive cost exactly once.
    /// </summary>
    private sealed class CostRecordingCastAgent : IPlayerAgent
    {
        private readonly MainPhaseCastAgent _inner;

        public Majik.Core.ValueObjects.ManaCost? PromptedCost { get; private set; }
        public int XPromptCount { get; private set; }

        public CostRecordingCastAgent(ScriptedAgent inner, ICard card, Player self)
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

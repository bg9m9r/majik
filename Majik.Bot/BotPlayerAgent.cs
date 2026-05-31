using Majik.Bot.Heuristic;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Bot;

/// <summary>
/// IPlayerAgent implementation that dispatches every prompt through an
/// IBotStrategy. v1 ships HeuristicStrategy chosen via BotConfig.Strategy.
/// </summary>
public sealed class BotPlayerAgent : IPlayerAgent
{
    private readonly Player _self;
    private readonly IBotStrategy _strategy;
    private readonly Action<bool>? _onThinking;

    public BotPlayerAgent(Player self, BotConfig config, Action<bool>? onThinking = null)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _onThinking = onThinking;
        _strategy = config.Strategy switch
        {
            "heuristic" => new HeuristicStrategy(config),
            "mcts"      => throw new NotImplementedException("MCTS strategy reserved for v2."),
            _ => throw new ArgumentException($"Unknown strategy: {config.Strategy}", nameof(config)),
        };
    }

    /// <summary>
    /// Wraps a synchronous policy call with the optional thinking callback.
    /// Fires <c>onThinking(true)</c> before, <c>onThinking(false)</c> after.
    /// Observer exceptions are swallowed so a faulty subscriber cannot abort
    /// the engine.
    /// </summary>
    private Task<T> WrapAsync<T>(Func<T> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { _onThinking?.Invoke(true); }
        catch { /* observer fault must not abort engine */ }
        try
        {
            return Task.FromResult(work());
        }
        finally
        {
            try { _onThinking?.Invoke(false); }
            catch { /* observer fault must not abort engine */ }
        }
    }

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickPriorityAction(ctx, _self), ct);

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMulligan(hand, mulligansTaken), ct);

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickCardsToBottom(hand, countToBottom), ct);

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickTargets(ctx, _self, request), ct);

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickX(ctx, _self), ct);

    public Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMode(ctx, _self, modes), ct);

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => WrapAsync(() => _strategy.OrderTriggers(ctx, mine), ct);

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMana(ctx, _self, cost), ct);

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickAttackers(ctx, _self, eligibleAttackers), ct);

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickBlockers(ctx, _self, attackers, eligibleBlockers), ct);

    public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickScry(ctx, _self, peeked), ct);

    public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickSurveil(ctx, _self, peeked), ct);

    public Task<ICard?> ChooseLibraryPickAsync(GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickLibraryCard(ctx, _self, candidates, kindLabel), ct);

    /// <summary>
    /// CR 117.x / 605.1 — wire-shaped Yes/No prompt. Heuristic posture:
    /// always accept. Shock-land "pay 2 life to enter untapped?" is the
    /// only current caller and bots want to curve out, so paying is the
    /// strictly better choice in nearly every game state (the alternative
    /// is a tapped land, which delays the next-turn curve). Smarter
    /// per-context overrides can land later (e.g. decline at low life
    /// or under specific aggro pressure); the simple "yes" baseline keeps
    /// the bot's mana on schedule and matches the way real ladder players
    /// play untapped shocks by default.
    /// </summary>
    public Task<bool> ChooseYesNoAsync(
        GameContext? ctx,
        string question,
        string? sourceCardName,
        CancellationToken ct = default)
        => WrapAsync(() => true, ct);

    /// <summary>
    /// PLAN 01 (Slice C) — declarative choice sink. Yes/No routes through this
    /// bot's wire Yes/No posture (always accept); PickOne/PickN return the
    /// first candidate(s) (or decline when optional with no candidates),
    /// matching the bot's first-pick posture on bespoke pick prompts.
    /// </summary>
    public Task<IReadOnlyList<object>> ChooseAsync(
        GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        => WrapAsync<IReadOnlyList<object>>(() =>
        {
            var candidates = req.Candidates ?? Array.Empty<object>();
            if (req.Kind == ChoiceKind.YesNo)
            {
                // Bot always accepts (mirrors the wire Yes/No posture above).
                return candidates.Count > 0 ? new[] { candidates[0] } : new object[] { true };
            }
            if (req.Optional && candidates.Count == 0)
                return Array.Empty<object>();
            var take = Math.Max(req.Min, candidates.Count > 0 ? 1 : 0);
            return candidates.Take(Math.Min(take, candidates.Count)).ToList();
        }, ct);
}

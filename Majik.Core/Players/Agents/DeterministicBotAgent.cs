using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.ValueObjects;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Smoke-test bot. Passes priority always, never mulligans, picks the
/// minimum number of targets in the order presented. Use for integration
/// tests that just need a game loop to terminate without exceptions.
/// </summary>
public sealed class DeterministicBotAgent : IPlayerAgent
{
    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => Task.FromResult(PriorityAction.Pass);

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => Task.FromResult(MulliganDecision.Keep);

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ICard>>(hand.Take(countToBottom).ToList());

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
    {
        var picked = request.LegalCandidates.Take(request.MinTargets).ToList();
        return Task.FromResult<IReadOnlyList<object>>(picked);
    }

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine.ToList());

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => Task.FromResult(ManaPayment.Empty);

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => Task.FromResult(CombatPlan.None);

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        => Task.FromResult(BlockPlan.None);

    /// <summary>
    /// Default all-to-bottom: most aggressive "keep nothing on top" semantic.
    /// Matches pre-agent default behaviour in <c>OracleSpellBinder.ScryNSpell</c>.
    /// </summary>
    public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => Task.FromResult(new ScryAction.ScryDecision(
            ToBottom: peeked.ToList(),
            TopOrder: Array.Empty<ICard>()));

    /// <summary>
    /// Default all-to-graveyard: matches pre-agent default behaviour in
    /// <c>OracleSpellBinder.SurveilSelfSpell</c> and <c>UndergroundMortuaryFactory</c>.
    /// </summary>
    public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => Task.FromResult(new SurveilAction.SurveilDecision(
            ToGraveyard: peeked.ToList(),
            TopOrder: Array.Empty<ICard>()));

    /// <summary>
    /// Deterministic library pick: first candidate, or null when the
    /// candidate list is empty. Matches the pre-agent default behavior of
    /// <c>SearchSpellFactory.SearchLibrarySpell</c>.
    /// </summary>
    public Task<ICard?> ChooseLibraryPickAsync(
        GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel, CancellationToken ct = default)
        => Task.FromResult<ICard?>(candidates.FirstOrDefault());
}

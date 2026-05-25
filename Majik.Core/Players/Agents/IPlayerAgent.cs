using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.ValueObjects;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Single async sink for every player decision. Bots, scripted tests, and
/// remote (web) players all implement this. The engine never deals with
/// "is this a human?" — it just awaits.
/// </summary>
public interface IPlayerAgent
{
    /// <summary>
    /// Player has priority. Pass, cast, activate, or play a land.
    /// </summary>
    Task<PriorityAction> ChoosePriorityActionAsync(
        GameContext ctx, CancellationToken ct = default);

    /// <summary>
    /// London mulligan (Rule 103.4) — keep or shuffle and redraw.
    /// </summary>
    Task<MulliganDecision> ChooseMulliganAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default);

    /// <summary>
    /// CR 103.4d — after keeping a mulliganed hand, choose which N cards
    /// to place on the bottom of the library (N = mulligans taken).
    /// Implementations must return exactly N cards from the hand.
    /// </summary>
    Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default);

    /// <summary>
    /// Pick targets satisfying the request (cardinality + legality).
    /// </summary>
    Task<IReadOnlyList<object>> ChooseTargetsAsync(
        GameContext ctx, TargetRequest request, CancellationToken ct = default);

    /// <summary>
    /// Choose the value of X for a variable cost.
    /// </summary>
    Task<int> ChooseXAsync(
        GameContext ctx, ICard source, CancellationToken ct = default);

    /// <summary>
    /// Pick a mode index for a modal spell or ability.
    /// <paramref name="modeIntents"/> is parallel to <paramref name="modes"/>
    /// when populated, carrying each mode's
    /// <see cref="Majik.Core.Cards.BotIntent"/> from the bound
    /// <c>SpellDefinition</c>. Empty / mismatched length means the binder
    /// did not classify per-mode intent; intent-aware agents fall back to
    /// legacy label scoring in that case.
    /// </summary>
    Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sub-order the player's own triggers when multiple fired at once
    /// (Rule 603.3b — APNAP, then controller chooses within their group).
    /// </summary>
    Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
        GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default);

    /// <summary>
    /// Pick which mana sources to tap to pay a cost.
    /// </summary>
    Task<ManaPayment> ChooseManaSourcesAsync(
        GameContext ctx, ManaCost cost, CancellationToken ct = default);

    /// <summary>
    /// Declare attackers (Rule 508). Empty plan = attack with nothing.
    /// </summary>
    Task<CombatPlan> DeclareAttackersAsync(
        GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default);

    /// <summary>
    /// Declare blockers (Rule 509). Each blocker assigned to one attacker.
    /// </summary>
    Task<BlockPlan> DeclareBlockersAsync(
        GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default);

    /// <summary>
    /// CR 701.20 — Scry N: decide which of the peeked cards go to the bottom
    /// of the library (<see cref="ScryAction.ScryDecision.ToBottom"/>) and
    /// which return to the top in player-chosen order
    /// (<see cref="ScryAction.ScryDecision.TopOrder"/>).
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect closures
    /// that don't have a GameContext available (sync-over-async wart; TODO: pass
    /// ctx once effects become async).
    /// </para>
    /// </summary>
    Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> peeked,
        CancellationToken ct = default);

    /// <summary>
    /// CR 701.42 — Surveil N: decide which of the peeked cards go to the
    /// graveyard (<see cref="SurveilAction.SurveilDecision.ToGraveyard"/>) and
    /// which return to the top in player-chosen order
    /// (<see cref="SurveilAction.SurveilDecision.TopOrder"/>).
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect closures
    /// (same sync-over-async constraint as <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// </summary>
    Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> peeked,
        CancellationToken ct = default);

    /// <summary>
    /// CR 701.19a — library search. The engine pre-filters
    /// <paramref name="candidates"/> down to the cards that satisfy the
    /// search predicate (kind, color, mana value, etc.); the agent picks
    /// zero or one. Returning <see langword="null"/> models "find nothing"
    /// (legal under CR 701.19a — searches are an action a player may
    /// decline to resolve to a chosen card).
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect
    /// closures (same sync-over-async wart as <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// <paramref name="kindLabel"/> is human-readable ("creature",
    /// "instant or sorcery card", "basic land card") for prompt UIs.
    /// </summary>
    Task<ICard?> ChooseLibraryPickAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        CancellationToken ct = default)
        // Default: pick the first candidate (legacy pre-agent behavior).
        // Smart bots override with heuristics; remote agents prompt the UI.
        => Task.FromResult<ICard?>(candidates.Count > 0 ? candidates[0] : null);

    /// <summary>
    /// CR 701.59 — Bloomburrow "Gift" cast-time prompt. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the spell being
    /// cast implements <see cref="Majik.Core.Spells.IGiftClause"/>. The
    /// agent may decline (return <see langword="null"/>) or pick exactly
    /// one of the supplied <paramref name="opponents"/> as the gift
    /// recipient.
    /// <para>
    /// <paramref name="giftDescription"/> is the human-readable gift
    /// label sourced from <see cref="Majik.Core.Spells.IGiftClause.Description"/>
    /// ("a tapped 1/1 blue Fish creature token"). Surfaced verbatim by
    /// remote-agent UIs in the prompt ("Promise <em>{description}</em>
    /// to an opponent?"); ignored by deterministic / scripted agents.
    /// </para>
    /// <para>
    /// Default: decline the gift (returns <see langword="null"/>) — the
    /// most conservative posture for legacy agents that pre-date this
    /// prompt. Smart bots override with heuristics (HeuristicBotAgent
    /// promises by default when the gift unlocks a strictly better
    /// effect — same most-aggressive posture as the Ascend / Spectacle
    /// alt-cost prompts); scripted-test agents return their queued pick.
    /// </para>
    /// </summary>
    Task<Player?> ChooseGiftRecipientAsync(
        GameContext ctx,
        ICard source,
        string giftDescription,
        IReadOnlyList<Player> opponents,
        CancellationToken ct = default)
        => Task.FromResult<Player?>(null);

    /// <summary>
    /// Generic Yes/No prompt for optional "may" clauses (CR 117.x / 605.1).
    /// Returns <see langword="true"/> to take the action, <see langword="false"/>
    /// to decline.
    /// <para>
    /// <paramref name="question"/> is a human-readable label surfaced verbatim
    /// by remote-agent UIs ("Promise the gift?", "Pay {2} to keep your
    /// graveyard?"). Deterministic / scripted agents ignore it.
    /// </para>
    /// <para>
    /// <paramref name="intent"/> is the heuristic classification the prompt
    /// represents (CardAdvantage / LoseLife / DiscardCost / CostToDecline /
    /// CheatIntoPlay etc.) — see <see cref="BotIntent"/>. Smart bots key
    /// their default posture off this; remote agents ignore it.
    /// </para>
    /// <para>
    /// Default implementation (used by ScriptedAgent / DeterministicBotAgent
    /// when not overridden): a tiny three-way heuristic over intent —
    /// accept upside-tagged prompts (Buff / CardAdvantage / Heal / Tutor /
    /// Draw / Reanimate), decline downside-tagged prompts (LoseLife /
    /// DiscardCost / CostToDecline) and accept everything else. This
    /// preserves the legacy "auto-accept may-clauses" posture used by every
    /// factory written before this prompt shipped.
    /// </para>
    /// </summary>
    Task<bool> ChooseYesNoAsync(
        string question,
        BotIntent intent,
        CancellationToken ct = default)
    {
        // Upside intents — always yes.
        if (intent.HasAny(BotIntent.CardAdvantage
                          | BotIntent.Buff
                          | BotIntent.Heal
                          | BotIntent.Tutor
                          | BotIntent.Draw
                          | BotIntent.Reanimate
                          | BotIntent.CheatIntoPlay
                          | BotIntent.Token))
        {
            return Task.FromResult(true);
        }
        // Downside intents — always no.
        if (intent.HasAny(BotIntent.LoseLife
                          | BotIntent.DiscardCost
                          | BotIntent.CostToDecline))
        {
            return Task.FromResult(false);
        }
        // Neutral / unclassified — match the legacy "auto-accept may"
        // posture used by every factory written before this prompt shipped
        // (Sneak Attack / Through the Breach / Arclight Phoenix / Aether
        // Vial / Bloodghast etc.).
        return Task.FromResult(true);
    }

    /// <summary>
    /// Pick exactly one card from a candidate set in
    /// <paramref name="chooser"/>'s hand, or return <see langword="null"/>
    /// to decline (only legal when the calling effect treats the choice as
    /// "may" — see CR 117.x). Used by:
    ///   - Discard prompts (Liliana of the Veil +1, Faithless Looting,
    ///     Yawgmoth) — <see cref="BotIntent.Discard"/>.
    ///   - Cheat-into-play prompts (Sneak Attack, Through the Breach,
    ///     Show and Tell) — <see cref="BotIntent.CheatIntoPlay"/>.
    /// <para>
    /// <paramref name="candidates"/> is pre-filtered by the calling effect
    /// to legal picks only (e.g. creatures in hand for Sneak Attack,
    /// permanents in hand for Show and Tell, any card for Liliana's
    /// discard). The agent picks zero or one.
    /// </para>
    /// <para>
    /// Default implementation: return the first candidate (deterministic
    /// pre-agent behaviour). When the candidate list is empty, returns
    /// <see langword="null"/> — every retrofitted factory treats null as
    /// "no eligible card in hand, no-op".
    /// </para>
    /// </summary>
    Task<ICard?> ChooseFromHandAsync(
        Player chooser,
        IReadOnlyList<ICard> candidates,
        BotIntent intent,
        CancellationToken ct = default)
        => Task.FromResult<ICard?>(candidates.Count > 0 ? candidates[0] : null);

    /// <summary>
    /// Pick exactly one permanent from a candidate set on the
    /// <paramref name="chooser"/>'s battlefield (or any pre-filtered
    /// battlefield subset the calling effect produced). Used by:
    ///   - Sacrifice prompts (Annihilator N — CR 702.86, Smallpox,
    ///     Innocent Blood, Plaguecrafter) — <see cref="BotIntent.Removal"/> /
    ///     <see cref="BotIntent.DiscardCost"/>.
    /// <para>
    /// <paramref name="candidates"/> is pre-filtered by the calling effect
    /// to legal picks only (e.g. permanents the chooser controls for an
    /// Annihilator sacrifice). The agent picks zero or one. Returning
    /// <see langword="null"/> is only legal when the calling effect treats
    /// the choice as "may" (CR 117.x). Annihilator-style mandatory
    /// sacrifices treat <see langword="null"/> as "no eligible permanents,
    /// no-op" — the candidate list is empty in that case anyway.
    /// </para>
    /// <para>
    /// Default implementation: return the first candidate (deterministic
    /// pre-agent behaviour, matching <see cref="ChooseFromHandAsync"/>).
    /// Empty candidate list returns <see langword="null"/>.
    /// </para>
    /// </summary>
    Task<ICard?> ChooseFromBattlefieldAsync(
        Player chooser,
        IReadOnlyList<ICard> candidates,
        BotIntent intent,
        CancellationToken ct = default)
        => Task.FromResult<ICard?>(candidates.Count > 0 ? candidates[0] : null);
}

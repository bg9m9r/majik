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
    /// PLAN 01 (Slice C) — the single declarative non-targeting choice sink.
    /// Every bespoke <c>ChooseXxxAsync</c> prompt (Yes/No, pick-one-from-hand,
    /// library search, reveal-and-choose, gift recipient, …) is expressible as
    /// a <see cref="ChoiceRequest"/> handed to this method; the legacy methods
    /// are now default-implemented shims over it (preserving their historical
    /// <c>candidates[0]</c> / decline defaults).
    ///
    /// <para>
    /// Returns the chosen candidates (a subset of
    /// <see cref="ChoiceRequest.Candidates"/>). For <see cref="ChoiceKind.YesNo"/>
    /// a single-element non-empty list means "yes" and an empty list means "no".
    /// For <see cref="ChoiceKind.PickOne"/> an empty list means "decline" (only
    /// legal when <see cref="ChoiceRequest.Optional"/>).
    /// </para>
    ///
    /// <para>
    /// Default implementation preserves the pre-agent posture: Yes/No applies
    /// the same intent heuristic as
    /// <see cref="ChooseYesNoAsync(string,BotIntent,CancellationToken)"/>;
    /// non-optional picks return the first <see cref="ChoiceRequest.Min"/>
    /// candidates; optional picks decline. Smart bots / remote agents override
    /// to route through their decision policy / wire channel.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<object>> ChooseAsync(
        GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
    {
        var candidates = req.Candidates ?? Array.Empty<object>();

        if (req.Kind == ChoiceKind.YesNo)
        {
            // Reuse the legacy intent heuristic. "Yes" → one sentinel
            // candidate (the candidate list itself, when supplied, or a
            // boxed true); "no" → empty.
            var yes = ChooseYesNoAsync(req.Description, req.Intent, ct).GetAwaiter().GetResult();
            if (!yes) return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
            IReadOnlyList<object> yesResult = candidates.Count > 0
                ? new[] { candidates[0] }
                : new object[] { true };
            return Task.FromResult(yesResult);
        }

        if (req.Optional)
        {
            // Decline by default (gift-recipient posture).
            return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        }

        // Non-optional PickOne / PickN / Order — return the first Min
        // candidates (deterministic pre-agent first-pick).
        var take = Math.Max(0, req.Min);
        if (take == 0 || candidates.Count == 0)
        {
            // PickOne with Min==1 but treated leniently: fall back to the
            // first candidate so "put one of them" mandatory clauses pick.
            IReadOnlyList<object> firstOrNone = candidates.Count > 0
                ? new[] { candidates[0] }
                : Array.Empty<object>();
            return Task.FromResult(firstOrNone);
        }

        IReadOnlyList<object> picked = candidates.Take(take).ToList();
        return Task.FromResult(picked);
    }

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
    async Task<ICard?> ChooseLibraryPickAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — express as a declarative PickOne and route
        // through ChooseAsync. Default ChooseAsync returns the first candidate
        // (legacy pre-agent behaviour). Smart bots / remote agents that
        // override this method keep their bespoke logic.
        var req = new ChoiceRequest(
            ChoiceKind.PickOne, kindLabel, Min: 1, Max: 1,
            Candidates: candidates, Intent: BotIntent.Tutor, Optional: false);
        var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0 ? (ICard)chosen[0] : null;
    }

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
    async Task<Player?> ChooseGiftRecipientAsync(
        GameContext ctx,
        ICard source,
        string giftDescription,
        IReadOnlyList<Player> opponents,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — declarative optional PickOne. Default
        // ChooseAsync declines (Optional: true ⇒ empty), preserving the
        // legacy "decline the gift" default. Smart / scripted agents that
        // override this method keep their bespoke pick.
        var req = new ChoiceRequest(
            ChoiceKind.PickOne, giftDescription, Min: 0, Max: 1,
            Candidates: opponents.Cast<object>().ToList(),
            Intent: BotIntent.None, Optional: true);
        var chosen = await ChooseAsync(ctx, req, ct).ConfigureAwait(false);
        return chosen.Count > 0 ? (Player)chosen[0] : null;
    }

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
    /// <summary>
    /// Wire-shaped Yes/No prompt for optional "may" clauses (CR 117.x /
    /// 605.1) that need a source-card label on the prompt envelope (so
    /// remote UIs can render "Overgrown Tomb: pay 2 life?"). Default
    /// implementation routes to the legacy
    /// <see cref="ChooseYesNoAsync(string,BotIntent,CancellationToken)"/>
    /// with a conservative <see cref="BotIntent.LoseLife"/> | <see cref="BotIntent.CostToDecline"/>
    /// classifier (matches the shock-land prompt — every current caller is
    /// the production binder-chain shock-land replacement).
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect
    /// closures (same sync-over-async wart as
    /// <see cref="ChooseScryDecisionAsync"/>). <paramref name="sourceCardName"/>
    /// is optional but, when present, lets remote-agent UIs name the prompt
    /// after the triggering permanent / spell.
    /// </para>
    /// </summary>
    async Task<bool> ChooseYesNoAsync(
        GameContext? ctx,
        string question,
        string? sourceCardName,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — declarative YesNo. Default ChooseAsync
        // applies the legacy intent heuristic (here the conservative
        // shock-land classifier). Remote agents override this method to
        // prompt the UI.
        var req = new ChoiceRequest(
            ChoiceKind.YesNo, question, Min: 0, Max: 1,
            Candidates: Array.Empty<object>(),
            Intent: BotIntent.LoseLife | BotIntent.CostToDecline,
            Optional: true);
        var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0;
    }

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
                          | BotIntent.Token
                          | BotIntent.OpeningHandLeyline))
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
    async Task<ICard?> ChooseFromHandAsync(
        Player chooser,
        IReadOnlyList<ICard> candidates,
        BotIntent intent,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — declarative PickOne. Default ChooseAsync
        // returns the first candidate (legacy pre-agent behaviour). No
        // GameContext on this prompt surface, so pass null — the default
        // ChooseAsync never dereferences it, and every agent that overrides
        // this method (scripted / heuristic) keeps its bespoke pick.
        var req = new ChoiceRequest(
            ChoiceKind.PickOne, "choose from hand", Min: 1, Max: 1,
            Candidates: candidates, Intent: intent, Optional: false);
        var chosen = await ChooseAsync(null!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0 ? (ICard)chosen[0] : null;
    }

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
    async Task<ICard?> ChooseFromBattlefieldAsync(
        Player chooser,
        IReadOnlyList<ICard> candidates,
        BotIntent intent,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — declarative PickOne, first-candidate default.
        var req = new ChoiceRequest(
            ChoiceKind.PickOne, "choose from battlefield", Min: 1, Max: 1,
            Candidates: candidates, Intent: intent, Optional: false);
        var chosen = await ChooseAsync(null!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0 ? (ICard)chosen[0] : null;
    }

    /// <summary>
    /// Pick exactly one card from a generic "pile" of candidates that does
    /// not live in one of the engine-tracked hand / battlefield / library
    /// zones — most notably the wishboard (sideboard treated as the
    /// "outside the game" pool that wish-tutor effects draw from per
    /// CR 408 / CR 100.4). Used by:
    ///   - <see cref="Majik.Core.Effects.WishTutorEffect"/> (Burning Wish /
    ///     Cunning Wish / Glittering Wish / Living Wish / Mastermind's
    ///     Acquisition mode 2 / Karn, the Great Creator's -2).
    /// <para>
    /// <paramref name="candidates"/> is pre-filtered by the calling effect
    /// to legal picks only (e.g. artifact cards in sideboard for Karn's -2,
    /// any owned card in sideboard for Mastermind's Acquisition). The agent
    /// picks zero or one. Returning <see langword="null"/> is treated as
    /// "find nothing" — legal whenever the calling effect treats the choice
    /// as optional (CR 117.x / CR 408 — wish effects let you "reveal a
    /// card you own from outside the game", which is itself a may-style
    /// gesture once you have legal candidates).
    /// </para>
    /// <para>
    /// <paramref name="pileLabel"/> is human-readable ("your sideboard",
    /// "your wishboard", "an artifact card from outside the game") for
    /// prompt UIs.
    /// </para>
    /// <para>
    /// Default implementation: return the first candidate (deterministic
    /// pre-agent behaviour, matching <see cref="ChooseFromHandAsync"/> /
    /// <see cref="ChooseFromBattlefieldAsync"/>). Empty candidate list
    /// returns <see langword="null"/>.
    /// </para>
    /// </summary>
    async Task<ICard?> ChooseFromPileAsync(
        Player chooser,
        IReadOnlyList<ICard> candidates,
        string pileLabel,
        BotIntent intent,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — declarative PickOne, first-candidate default.
        var req = new ChoiceRequest(
            ChoiceKind.PickOne, pileLabel, Min: 1, Max: 1,
            Candidates: candidates, Intent: intent, Optional: false);
        var chosen = await ChooseAsync(null!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0 ? (ICard)chosen[0] : null;
    }

    /// <summary>
    /// CR 701.15 — "reveal top N, you may put one matching card into [zone],
    /// rest go to [zone]" prompt (Malevolent Rumble, Impulse, Sleight of
    /// Hand, See the Unwritten and friends). Distinct from
    /// <see cref="ChooseLibraryPickAsync"/> in two ways:
    /// <list type="bullet">
    /// <item>The agent receives the FULL <paramref name="revealed"/> list so
    /// the UI can render every revealed card (CR 701.15 — revealed cards
    /// are publicly visible). <paramref name="eligible"/> is the subset
    /// the player may pick from (filtered by the calling effect, e.g.
    /// "permanent card", "colorless card", "creature card").</item>
    /// <item><paramref name="optional"/> controls whether the player may
    /// decline. When <see langword="true"/> the agent may return
    /// <see langword="null"/> to leave every revealed card in the rest
    /// pile (matching "you may"). When <see langword="false"/> the agent
    /// must return a card from <paramref name="eligible"/> if any exist
    /// (matching "put one of them" mandatory clauses); a null return
    /// when eligible is non-empty falls back to the first eligible (the
    /// engine treats it as an agent misbehaviour, not a legal decline).
    /// When eligible is empty the agent must return <see langword="null"/>.
    /// </item>
    /// </list>
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect
    /// closures (same sync-over-async wart as <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// <para>
    /// <paramref name="label"/> is human-readable describing the choice
    /// ("Permanent to put into hand", "Colorless card to reveal and put
    /// into hand"); surfaced verbatim by remote-agent UIs.
    /// </para>
    /// <para>
    /// Default implementation picks the first eligible card (deterministic
    /// pre-agent behaviour matching the legacy <c>FirstOrDefault</c> in
    /// every retrofitted factory). Empty <paramref name="eligible"/> returns
    /// <see langword="null"/>. Smart bots override with value heuristics;
    /// remote agents prompt the UI.
    /// </para>
    /// </summary>
    async Task<ICard?> ChooseFromRevealedAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> revealed,
        IReadOnlyList<ICard> eligible,
        bool optional,
        string label,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — declarative PickOne over the ELIGIBLE
        // subset. Default ChooseAsync returns the first eligible card
        // (legacy FirstOrDefault auto-pick). Optional is surfaced on the
        // request for remote/UI agents that override; the default ignores
        // it and still first-picks so mandatory "put one of them" clauses
        // resolve. Remote agents override this method with the full-reveal
        // wire prompt.
        var req = new ChoiceRequest(
            ChoiceKind.PickOne, label, Min: optional ? 0 : 1, Max: 1,
            Candidates: eligible, Intent: BotIntent.None, Optional: false);
        var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0 ? (ICard)chosen[0] : null;
    }
}

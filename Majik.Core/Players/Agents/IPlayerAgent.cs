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
    /// CR 115 — whether this agent wants the engine to SYNTHESIZE a complete
    /// legal candidate pool (via <c>TargetCandidateService</c>) for a targets
    /// request that ships no machine-readable pool. Human / remote agents need
    /// it (the portal can only render an explicit candidate list). Bots opt OUT
    /// (default false) — their <c>TargetPolicy</c> already does label-driven
    /// synthesis off an EMPTY pool (burn → face, removal → biggest creature),
    /// and pre-filling the pool would silently change those picks (a sampled
    /// in-sim burn would target a creature instead of the face). Keeping bots on
    /// the empty-pool path preserves their behaviour exactly.
    /// </summary>
    bool WantsSynthesizedTargetCandidates => false;

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
    /// CR 601.2d / CR 119.4 — divide a fixed amount of damage among the
    /// already-chosen targets of a "~ deals N damage divided as you choose
    /// among …" spell or ability. The division is announced at cast/activation
    /// time (CR 601.2d), recorded alongside the chosen targets, and read by the
    /// deal-damage effect at resolution.
    /// <para>
    /// <paramref name="targets"/> is the ordered list of chosen target tokens
    /// (each non-empty). The result is a per-target amount list, index-aligned
    /// with <paramref name="targets"/>, that MUST:
    /// <list type="bullet">
    /// <item>contain exactly <c>targets.Count</c> entries,</item>
    /// <item>assign each chosen target AT LEAST 1 (CR 119.4 — you must divide
    /// the damage so each target gets at least 1; you can't choose a target and
    /// assign it 0), and</item>
    /// <item>sum to exactly <paramref name="totalDamage"/> (CR 119.4).</item>
    /// </list>
    /// </para>
    /// <para>
    /// Default implementation: an even split with the remainder front-loaded
    /// onto the earliest targets (e.g. 3 damage among two targets → [2, 1]).
    /// This is the deterministic pre-agent posture every divided-damage card
    /// shipped with (the old captured <c>distribute</c> Func / template
    /// even-split). Smart bots / remote agents override to route through their
    /// decision policy / wire channel. The engine still defensively normalises
    /// the returned split (clamps each to ≥1, reconciles the total) so a
    /// misbehaving agent can never deal the wrong amount (CR 119.4).
    /// </para>
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect /
    /// dispatch closures that don't have a <see cref="GameContext"/> handy
    /// (same sync-over-async wart as <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<int>> ChooseDamageDivisionAsync(
        GameContext? ctx,
        ICard source,
        int totalDamage,
        IReadOnlyList<object> targets,
        CancellationToken ct = default)
    {
        return Task.FromResult(DamageDivisionDefaults.EvenSplit(totalDamage, targets.Count));
    }

    /// <summary>
    /// CR 614.12 / CR 601.2c — "as this enters / as you cast this, choose a
    /// color" (Sunken Citadel, Temple of the Dragon Queen, Coldsteel Heart,
    /// Utopia Sprawl, …). Picks one of the five mana colours — colourless is
    /// not a colour (CR 105.1 / 105.2c). Mirrors the bespoke
    /// <c>ChooseColorAsync</c> Sungold Sentinel's Coven grant already routes
    /// through <see cref="ChooseAsync"/> as a <see cref="ChoiceKind.PickOne"/>;
    /// promoted onto the interface so the binder-chain ETB choose-color
    /// replacement can prompt without a bespoke per-card closure.
    /// <para>
    /// Default implementation routes through the declarative
    /// <see cref="ChooseAsync"/> sink as a <see cref="ChoiceKind.PickOne"/> over
    /// the five colours, classified <see cref="BotIntent.Ramp"/> (a
    /// mana-fixing decision). Falls back to <paramref name="fallback"/> when no
    /// agent / game answers (the deterministic pre-agent posture — strictly one
    /// producible colour, never the old over-permissive five-WUBRG binding).
    /// </para>
    /// </summary>
    async Task<ManaColor> ChooseColorAsync(
        GameContext? ctx,
        string sourceLabel,
        ManaColor fallback = ManaColor.White,
        CancellationToken ct = default)
    {
        var colors = new object[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Black, ManaColor.Red, ManaColor.Green,
        };
        var req = new ChoiceRequest(
            ChoiceKind.PickOne,
            sourceLabel,
            Min: 1, Max: 1,
            Candidates: colors,
            Intent: BotIntent.Ramp,
            Optional: false);
        var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0 && chosen[0] is ManaColor c ? c : fallback;
    }

    /// <summary>
    /// CR 614.10 / CR 701.16 — "as this enters, you may reveal a [match] card
    /// from your hand" decision (Temple of the Dragon Queen — "you may reveal a
    /// Dragon card from your hand", and the wider conditional-tapped reveal
    /// family). The engine has already filtered the chooser's hand down to the
    /// <paramref name="matching"/> cards that satisfy the reveal predicate (the
    /// named subtype / type), so the prompt is only raised when at least one
    /// legal card exists. Returns the revealed card (which the caller may show
    /// publicly per CR 701.16a and feed into the gating condition), or
    /// <see langword="null"/> to decline — revealing is a "may" (CR 614.10).
    /// <para>
    /// <paramref name="matchLabel"/> is human-readable ("a Dragon card") for
    /// remote-agent prompt UIs. Mirrors the up-front choose-a-color ETB prompt
    /// (<see cref="ChooseColorAsync"/>) — both are binder-reachable "as this
    /// enters" agent surfaces.
    /// </para>
    /// <para>
    /// Default implementation routes through the declarative
    /// <see cref="ChooseAsync"/> sink as a <see cref="ChoiceKind.YesNo"/>
    /// ("reveal a [match] card?"), classified <see cref="BotIntent.CardAdvantage"/>
    /// — revealing has no downside and lets the land enter untapped, so the
    /// default heuristic answers "yes" and reveals the first matching card. A
    /// "no" (empty result) returns <see langword="null"/> (declined). Smart bots
    /// / remote agents override <see cref="ChooseAsync"/> (or this method) to
    /// decide whether to reveal.
    /// </para>
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect closures
    /// (same sync-over-async wart as <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// </summary>
    async Task<ICard?> ChooseRevealCardFromHandAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> matching,
        string matchLabel,
        CancellationToken ct = default)
    {
        if (matching is null || matching.Count == 0) return null;

        var req = new ChoiceRequest(
            ChoiceKind.YesNo,
            $"Reveal {matchLabel} from your hand?",
            Min: 0, Max: 1,
            Candidates: Array.Empty<object>(),
            Intent: BotIntent.CardAdvantage,
            Optional: true);
        var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);
        // "Yes" (non-empty) reveals the first matching card; "no" (empty)
        // declines (CR 614.10 — revealing is a "may").
        return chosen.Count > 0 ? matching[0] : null;
    }

    /// <summary>
    /// CR 614.12 / CR 201.4 — "as this enters / as you cast this, choose a
    /// card name" (Meddling Mage, Pithing Needle, Sorcerous Spyglass, Sanctum
    /// Prelate, The Stone Brain, Cavern of Souls, Phyrexian Revoker, …). The
    /// chooser names ANY card (CR 201.4 — a player names a card by stating a
    /// name printed on a Magic card; the name needn't correspond to a card any
    /// player owns or that is in any zone), so unlike a target / library pick
    /// there is no engine-enumerable legal pool — the choice is a free-form
    /// string.
    /// <para>
    /// <paramref name="suggested"/> is an OPTIONAL hint pool the engine surveys
    /// at prompt time (typically the names of cards the chooser can currently
    /// see on opponents' sides — battlefield, stack, revealed hands — i.e. the
    /// "known threats" a sensible name would shut off). It is NOT a legality
    /// restriction (the chooser may still name something not in the list); it
    /// exists so the bot default and a remote UI have a ranked starting set.
    /// May be empty.
    /// </para>
    /// <para>
    /// <paramref name="constraintLabel"/> is human-readable ("a nonland card
    /// name", "a card name") for prompt UIs and documents any printed
    /// restriction the calling card imposes (Meddling Mage's "nonland",
    /// Sanctum Prelate's mana-value rider is handled separately). Enforcement
    /// of the restriction is the calling effect's concern — this surface
    /// returns whatever name the agent chose.
    /// </para>
    /// <para>
    /// Default implementation routes through the declarative
    /// <see cref="ChooseAsync"/> sink as a <see cref="ChoiceKind.PickOne"/> over
    /// the boxed <paramref name="suggested"/> names, classified
    /// <see cref="BotIntent.Counter"/> (naming a card to shut it off is a
    /// disruptive / hate play). When the agent returns a usable string that is what's
    /// chosen; otherwise it falls back to the first suggested name, then to
    /// <paramref name="fallback"/>. This mirrors <see cref="ChooseColorAsync"/>:
    /// boxed non-card candidates don't round-trip through the
    /// <see cref="ChoiceCommand"/> id map, so a remote (human) agent that hasn't
    /// shipped a dedicated name-entry command lands on the deterministic
    /// suggested-name default — strictly better than the pre-surface no-op
    /// (which named nothing and left the static inert). Smart bots / remote
    /// agents override to name by value / wire entry.
    /// </para>
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect /
    /// replacement closures that don't have a <see cref="GameContext"/> handy
    /// (same sync-over-async wart as <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// </summary>
    async Task<string> ChooseCardNameAsync(
        GameContext? ctx,
        IReadOnlyList<string> suggested,
        string constraintLabel,
        string fallback = "",
        CancellationToken ct = default)
    {
        var pool = suggested ?? Array.Empty<string>();
        if (pool.Count > 0)
        {
            var req = new ChoiceRequest(
                ChoiceKind.PickOne,
                $"Choose {constraintLabel}",
                Min: 1, Max: 1,
                Candidates: pool.Cast<object>().ToList(),
                Intent: BotIntent.Counter,
                Optional: false);
            var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);
            if (chosen.Count > 0 && chosen[0] is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
            // Agent returned nothing usable (e.g. a remote agent whose boxed
            // string didn't round-trip the ChoiceCommand id map) — fall back to
            // the top-ranked suggested name rather than the inert empty default.
            return pool[0];
        }

        // No suggestion pool at all — the deterministic pre-agent posture is the
        // supplied fallback (empty string = "name nothing", which leaves the
        // static inert; callers that always want a non-empty name pass one).
        return fallback;
    }

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
    /// CR 700.2d / CR 702.121 — pick the set of modes for a "choose one or
    /// more" (or "choose two", "choose two or three") modal spell. Returns
    /// the chosen mode indices, in the order the caster wants them announced
    /// (resolution always reorders to printed order per CR 608.2c — the
    /// EffectFactory enforces that — so order here only affects which extra
    /// modes pay escalate). The result must:
    /// <list type="bullet">
    /// <item>contain between <paramref name="minModes"/> and
    /// <paramref name="maxModes"/> entries (CR 700.2e),</item>
    /// <item>contain no duplicate indices (CR 700.2d — each mode at most
    /// once, absent a "you may choose the same mode more than once" rider),</item>
    /// <item>contain only indices in <c>[0, modes.Count)</c>.</item>
    /// </list>
    /// <paramref name="modeIntents"/> is parallel to <paramref name="modes"/>
    /// when populated (same contract as <see cref="ChooseModeAsync"/>).
    /// <para>
    /// Default implementation routes through the declarative
    /// <see cref="ChooseAsync"/> sink as a <see cref="ChoiceKind.PickN"/> over
    /// boxed mode indices, then sanitizes the result (dedupe, clamp to range,
    /// enforce <paramref name="minModes"/>..<paramref name="maxModes"/>). When
    /// the agent supplies nothing usable it falls back to the first
    /// <paramref name="minModes"/> modes — the deterministic pre-agent posture
    /// matching every other prompt's first-candidate default.
    /// </para>
    /// </summary>
    async Task<IReadOnlyList<int>> ChooseModesAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        int minModes,
        int maxModes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default)
    {
        var indices = Enumerable.Range(0, modes.Count).Cast<object>().ToList();
        var req = new ChoiceRequest(
            ChoiceKind.PickN,
            "Choose modes",
            Min: Math.Max(1, minModes),
            Max: Math.Min(maxModes, modes.Count),
            Candidates: indices,
            Intent: BotIntent.None,
            Optional: false);
        var chosen = await ChooseAsync(ctx, req, ct).ConfigureAwait(false);

        // Sanitize: keep only valid, distinct indices in encounter order.
        var seen = new HashSet<int>();
        var clean = new List<int>();
        foreach (var o in chosen)
        {
            if (o is not int idx) continue;
            if (idx < 0 || idx >= modes.Count) continue;
            if (!seen.Add(idx)) continue;
            clean.Add(idx);
            if (clean.Count >= maxModes) break;
        }

        // CR 700.2e — enforce the minimum. Backfill from the first unused
        // modes so a misbehaving / first-candidate agent still produces a
        // legal pick.
        if (clean.Count < minModes)
        {
            for (var i = 0; i < modes.Count && clean.Count < minModes; i++)
            {
                if (seen.Add(i)) clean.Add(i);
            }
        }
        return clean;
    }

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
    /// Declare attackers (Rule 508). Empty plan = attack with nothing. The
    /// eligible list is typed <see cref="Permanent"/> so an animated NON-creature
    /// combatant (a manland — deferral
    /// <c>animated-noncreature-as-combatant</c>, 4B) is offered as an attacker;
    /// a real <see cref="Creature"/> is a <see cref="Permanent"/>, so agents
    /// that pattern-match <c>is Creature</c> are unaffected.
    /// </summary>
    Task<CombatPlan> DeclareAttackersAsync(
        GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default);

    /// <summary>
    /// Declare blockers (Rule 509). Each blocker assigned to one attacker. Both
    /// lists are typed <see cref="Permanent"/> so an animated manland can be
    /// offered as a blocker and an animated land can be among the attackers
    /// (deferral <c>animated-noncreature-as-combatant</c>, 4B).
    /// </summary>
    Task<BlockPlan> DeclareBlockersAsync(
        GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default);

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
    /// CR 701.40c — explore put-back decision. After a non-land card is
    /// revealed off the top of the library and a +1/+1 counter is placed on
    /// the exploring permanent, its controller chooses to leave the revealed
    /// <paramref name="revealedCard"/> on top of their library or put it into
    /// their graveyard. Returns <see langword="true"/> to keep the card on top
    /// of the library, <see langword="false"/> to put it into the graveyard.
    /// <para>
    /// Only invoked when a NON-land card was revealed and the library was
    /// non-empty (CR 701.40b — a revealed land goes straight to hand and the
    /// controller makes no choice; CR 701.40d — an empty library reveals
    /// nothing and there is no card to keep or bin).
    /// </para>
    /// <para>
    /// Default implementation routes through the declarative
    /// <see cref="ChooseAsync"/> sink as a Yes/No prompt ("keep the revealed
    /// card on top of your library?"), classified <see cref="BotIntent.None"/>
    /// so the default heuristic answers "yes" — keep the card on top. This is
    /// the library-preserving default (mirrors Scry's all-on-top /
    /// Surveil's keep-on-top postures), so factories written before a smart
    /// agent ships don't silently mill the revealed card. Smart bots / remote
    /// agents override <see cref="ChooseAsync"/> (or this method) to decide
    /// per card value.
    /// </para>
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect
    /// closures (same sync-over-async wart as
    /// <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// </summary>
    async Task<bool> ChooseExploreKeepOnTopAsync(
        GameContext? ctx,
        ICard exploringCreature,
        ICard revealedCard,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — declarative YesNo "keep on top?". A
        // non-empty result ("yes") keeps the card on top; an empty result
        // ("no") sends it to the graveyard. Default ChooseAsync applies the
        // intent heuristic; BotIntent.None falls through to the legacy
        // "auto-accept may" posture => keep on top (library-preserving).
        var label = revealedCard?.Name is { Length: > 0 } name
            ? $"Keep {name} on top of your library?"
            : "Keep the revealed card on top of your library?";
        var req = new ChoiceRequest(
            ChoiceKind.YesNo, label, Min: 0, Max: 1,
            Candidates: Array.Empty<object>(),
            Intent: BotIntent.None,
            Optional: true);
        var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0;
    }

    /// <summary>
    /// CR 701.32c — clash top-or-bottom decision. During a clash (CR 701.32),
    /// each participating player reveals the top card of their library, then
    /// chooses to leave that card on top of their library or put it on the
    /// bottom. Returns <see langword="true"/> to keep the card on top,
    /// <see langword="false"/> to put it on the bottom.
    /// <para>
    /// Only invoked when the player's library is non-empty (CR 701.32a — an
    /// empty library reveals no card and the player makes no choice). The two
    /// clashing players choose independently and the choices are made before
    /// any card moves (CR 701.32b — "they're made simultaneously"); the engine
    /// resolves both reveals, then prompts each chooser, then applies the
    /// moves.
    /// </para>
    /// <para>
    /// Default implementation routes through the declarative
    /// <see cref="ChooseAsync"/> sink as a Yes/No prompt ("keep the revealed
    /// card on top of your library?"), classified <see cref="BotIntent.None"/>
    /// so the default heuristic answers "yes" — keep the card on top. This is
    /// the library-preserving default (mirrors
    /// <see cref="ChooseExploreKeepOnTopAsync"/> and Scry's all-on-top
    /// posture), so a clashing card is not silently sent to the bottom.
    /// </para>
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect
    /// closures (same sync-over-async wart as
    /// <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// </summary>
    async Task<bool> ChooseClashTopOrBottomAsync(
        GameContext? ctx,
        ICard revealedCard,
        CancellationToken ct = default)
    {
        // PLAN 01 (Slice C) shim — declarative YesNo "keep on top?". A
        // non-empty result ("yes") keeps the card on top; an empty result
        // ("no") puts it on the bottom. Mirrors ChooseExploreKeepOnTopAsync.
        var label = revealedCard?.Name is { Length: > 0 } name
            ? $"Keep {name} on top of your library? (clash)"
            : "Keep the revealed card on top of your library? (clash)";
        var req = new ChoiceRequest(
            ChoiceKind.YesNo, label, Min: 0, Max: 1,
            Candidates: Array.Empty<object>(),
            Intent: BotIntent.None,
            Optional: true);
        var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);
        return chosen.Count > 0;
    }

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
    /// Pick exactly <paramref name="count"/> cards from a candidate set in
    /// <paramref name="chooser"/>'s hand AND choose their order, in a SINGLE
    /// joint decision (CR 701.x library-top reorder — Brainstorm's "put two
    /// cards from your hand on top of your library in any order"). Returns the
    /// chosen cards as an ORDERED list: index 0 is the first element of the
    /// chosen order. For a "put N on top of your library in any order" effect
    /// the caller treats <c>result[0]</c> as the card that ends up ON TOP of
    /// the library (so callers can apply the result left-to-right onto the
    /// library top without re-reasoning about insertion order).
    /// <para>
    /// Distinct from looping <see cref="ChooseFromHandAsync"/> N times: the
    /// agent sees the WHOLE joint pick at once, so a smart bot can evaluate
    /// the combined selection + ordering rather than greedily picking each in
    /// isolation. <paramref name="candidates"/> is pre-filtered to legal
    /// picks; the effect is mandatory (no decline) — the agent returns
    /// up to <c>min(count, candidates.Count)</c> cards.
    /// </para>
    /// <para>
    /// Default implementation routes through the declarative
    /// <see cref="ChooseAsync"/> sink as a <see cref="ChoiceKind.OrderedPickN"/>,
    /// then sanitizes the result: keeps only distinct cards drawn from
    /// <paramref name="candidates"/>, in the agent's returned order, capped at
    /// <paramref name="count"/>, and backfills from the remaining candidates
    /// (in candidate order) so a misbehaving / first-candidate agent still
    /// produces a legal ordered pick. The pre-agent posture is therefore the
    /// first <paramref name="count"/> candidates in candidate order. Smart
    /// bots / remote agents override <see cref="ChooseAsync"/> (or this method)
    /// to evaluate the joint pick + order.
    /// </para>
    /// </summary>
    async Task<IReadOnlyList<ICard>> ChooseAndOrderFromHandAsync(
        Player chooser,
        IReadOnlyList<ICard> candidates,
        int count,
        BotIntent intent,
        CancellationToken ct = default)
    {
        var pool = candidates ?? Array.Empty<ICard>();
        var want = Math.Min(Math.Max(0, count), pool.Count);
        if (want == 0) return Array.Empty<ICard>();

        var req = new ChoiceRequest(
            ChoiceKind.OrderedPickN, "choose and order from hand",
            Min: want, Max: want,
            Candidates: pool.Cast<object>().ToList(),
            Intent: intent, Optional: false);
        var chosen = await ChooseAsync(null!, req, ct).ConfigureAwait(false);

        // Sanitize: distinct cards from the candidate pool, in agent order,
        // capped at `want`. Backfill from remaining candidates (candidate
        // order) so the result is always exactly `want` legal cards.
        var seen = new HashSet<ICard>();
        var ordered = new List<ICard>(want);
        foreach (var o in chosen)
        {
            if (o is not ICard c) continue;
            if (!pool.Contains(c)) continue;
            if (!seen.Add(c)) continue;
            ordered.Add(c);
            if (ordered.Count >= want) break;
        }
        if (ordered.Count < want)
        {
            foreach (var c in pool)
            {
                if (ordered.Count >= want) break;
                if (seen.Add(c)) ordered.Add(c);
            }
        }
        return ordered;
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
    /// CR 701.16 / CR 119.x — "sacrifice any number of [permanents]" subset
    /// choice (Scapeshift, Pump-and-sac engines, any "sacrifice any number of
    /// X" clause). Distinct from <see cref="ChooseFromBattlefieldAsync"/>,
    /// which picks exactly one permanent: this returns a chosen MULTISET (a
    /// subset, by reference, of <paramref name="candidates"/>) of the
    /// permanents the chooser elects to sacrifice — possibly the empty set
    /// (the lower bound of "any number", CR 119.x — "any number" includes
    /// zero) or the whole set.
    /// <para>
    /// <paramref name="candidates"/> is pre-filtered by the calling effect to
    /// the legal sacrifice pool (e.g. lands the chooser controls for
    /// Scapeshift; permanents matching the named type). The agent returns the
    /// subset to sacrifice. <paramref name="minCount"/> /
    /// <paramref name="maxCount"/> bound the choice: "any number" passes
    /// <c>min=0, max=candidates.Count</c>; a "sacrifice exactly N" clause
    /// passes <c>min=max=N</c>. The engine sanitises the returned subset
    /// (drops non-candidates + duplicates, clamps to the range) so a
    /// misbehaving agent can never sacrifice an illegal set.
    /// </para>
    /// <para>
    /// Default implementation routes through the declarative
    /// <see cref="ChooseAsync"/> sink as a <see cref="ChoiceKind.PickN"/> over
    /// the candidate permanents. The pre-agent posture mirrors every other
    /// declarative prompt: for an OPTIONAL subset (<paramref name="minCount"/>
    /// == 0) it sacrifices NOTHING (the faithful "any number" lower bound — a
    /// dumb/absent agent never throws away its own board); for a mandatory
    /// floor (<paramref name="minCount"/> &gt; 0) it sacrifices the first
    /// <paramref name="minCount"/> candidates. The result is always sanitised
    /// to a legal subset. Smart bots / remote agents override
    /// <see cref="ChooseAsync"/> (or this method) to choose the subset by
    /// value.
    /// </para>
    /// <para>
    /// <paramref name="ctx"/> may be <see langword="null"/> in v1 effect
    /// closures (same sync-over-async wart as
    /// <see cref="ChooseScryDecisionAsync"/>).
    /// </para>
    /// </summary>
    async Task<IReadOnlyList<ICard>> ChooseSubsetToSacrificeAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> candidates,
        int minCount,
        int maxCount,
        BotIntent intent = BotIntent.None,
        CancellationToken ct = default)
    {
        var pool = candidates ?? Array.Empty<ICard>();
        var min = Math.Clamp(minCount, 0, pool.Count);
        var max = Math.Clamp(maxCount, min, pool.Count);
        if (pool.Count == 0 || max == 0) return Array.Empty<ICard>();

        var req = new ChoiceRequest(
            ChoiceKind.PickN,
            "Sacrifice any number of permanents",
            Min: min, Max: max,
            Candidates: pool.Cast<object>().ToList(),
            Intent: intent,
            // Optional when there is no mandatory floor — an absent agent then
            // declines (sacrifices nothing) rather than first-picking.
            Optional: min == 0);
        var chosen = await ChooseAsync(ctx!, req, ct).ConfigureAwait(false);

        // Sanitise: distinct cards drawn from the candidate pool (by
        // reference), in the agent's returned order, capped at `max`.
        var seen = new HashSet<ICard>();
        var picked = new List<ICard>();
        foreach (var o in chosen)
        {
            if (o is not ICard c) continue;
            if (!pool.Contains(c)) continue;
            if (!seen.Add(c)) continue;
            picked.Add(c);
            if (picked.Count >= max) break;
        }

        // Enforce the mandatory floor (CR 701.16 — a "sacrifice exactly N"
        // clause must sacrifice N). Backfill from the first unused candidates
        // so a misbehaving / declining agent still produces a legal subset.
        if (picked.Count < min)
        {
            foreach (var c in pool)
            {
                if (picked.Count >= min) break;
                if (seen.Add(c)) picked.Add(c);
            }
        }
        return picked;
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

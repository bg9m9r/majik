using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Orchestrates Rule 601 spell-casting steps via async agent prompts:
///   0. casting permission check (CR 117.1, sorcery vs instant speed)
///   1. announce spell; pay any additional costs (CR 601.2f)
///   2. choose modes
///   3. choose X (variable costs); X is added to the mana cost
///   4. choose targets
///   5. choose mana payment (alternative cost replaces printed cost; CR 118.9)
///   6. move card to stack, build Spell, push, publish SpellCastEvent
///   7. when spell resolves, alternative cost's OnResolved fires (e.g. exile)
/// </summary>
public sealed class SpellCastFlow
{
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zoneService;
    private readonly IEventBus _eventBus;

    public SpellCastFlow(
        Majik.Core.Stack.Stack stack,
        ZoneService zoneService,
        IEventBus eventBus)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// CR 702.139c — Companion "cast from outside the game" entry path.
    ///
    /// Resolves the once-per-game tax portion of the companion rule: as a
    /// special action, the controller pays {3} to move their nominated
    /// companion from the sideboard zone to their hand. From there the
    /// card is cast normally via <see cref="CastAsync"/> on the same turn
    /// (the printed mana cost is paid then; the {3} here is the
    /// sideboard → hand tax, not the spell's cost).
    ///
    /// Preconditions enforced here:
    /// <list type="bullet">
    /// <item><see cref="Player.CompanionUsedThisGame"/> is false (once-per-game).</item>
    /// <item>The card is currently in the player's sideboard zone.</item>
    /// <item>It is the player's own turn, in a main phase, with an empty
    ///       stack (CR 702.139c — "any time you could cast a sorcery").</item>
    /// <item>The controller can pay the {3} tax from their mana pool.</item>
    /// </list>
    ///
    /// On success: the {3} is deducted, the card is moved sideboard → hand,
    /// and <see cref="Player.MarkCompanionUsed"/> latches the once-per-game
    /// ledger. The caller is then expected to invoke <see cref="CastAsync"/>
    /// with the printed mana cost to actually cast the companion.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any precondition fails.
    /// </exception>
    public Task CastCompanionAsync(
        Player caster,
        ICard card,
        GameContext ctx,
        CancellationToken ct = default)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));

        // CR 702.139c — once-per-game gate.
        if (caster.CompanionUsedThisGame)
        {
            throw new InvalidOperationException(
                $"Cannot move companion {card.Name} from sideboard: "
                + "the once-per-game companion cast has already been used.");
        }

        // Must be in the player's sideboard.
        if (card.Zone != ZoneType.Sideboard)
        {
            throw new InvalidOperationException(
                $"Cannot move companion {card.Name} from sideboard: "
                + $"card is in {card.Zone}, not Sideboard.");
        }
        if (!ReferenceEquals(card.Owner, caster))
        {
            throw new InvalidOperationException(
                $"Cannot move companion {card.Name} from sideboard: "
                + "card is not in the casting player's sideboard.");
        }

        // CR 702.139c — sorcery-speed restriction. Only legal during the
        // controller's own main phase with an empty stack.
        if (!ReferenceEquals(ctx.ActivePlayer, caster)
            || !_stack.IsEmpty
            || ctx.CurrentPhase != StateMachine.PhaseStateType.Main)
        {
            throw new InvalidOperationException(
                $"Cannot move companion {card.Name} from sideboard: "
                + "sorcery-speed restriction (CR 702.139c — only on your "
                + "own main phase with an empty stack).");
        }

        // CR 702.139c — pay the {3} tax. Atomic — if the pool can't cover
        // it, refuse the move entirely.
        if (!caster.PayMana(ValueObjects.ManaCost.Parse("{3}")))
        {
            throw new InvalidOperationException(
                $"Cannot move companion {card.Name} from sideboard: "
                + "controller cannot pay the {3} companion tax.");
        }

        // Move sideboard → hand, then latch the once-per-game ledger.
        _zoneService.MoveCard(card, ZoneType.Sideboard, ZoneType.Hand, controller: caster);
        caster.MarkCompanionUsed();

        return Task.CompletedTask;
    }

    public async Task<Spells.Spell> CastAsync(
        Player caster,
        ICard card,
        SpellDefinition definition,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct = default,
        IReadOnlyList<IAdditionalCost>? additionalCosts = null,
        IAlternativeCost? alternativeCost = null,
        Majik.Core.Players.Agents.ManaPayment? preChosenMana = null,
        DelveCost? delveCost = null)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (agent == null) throw new ArgumentNullException(nameof(agent));

        // CR 117.1 — sorcery-speed gating (skipped when the alternative cost
        // specifies its own casting permission, e.g. Flashback from graveyard).
        if (alternativeCost == null
            && ctx.CurrentPhase.HasValue
            && !CastingPermission.CanCast(card, caster, ctx.ActivePlayer,
                ctx.CurrentPhase.Value, _stack.IsEmpty, out var reason))
        {
            throw new InvalidOperationException($"Cannot cast {card.Name}: {reason}");
        }

        // Alternative cost legality check (CR 118.9 — zone restriction etc.).
        if (alternativeCost != null && !alternativeCost.CanCastFor(card, caster))
        {
            throw new InvalidOperationException(
                $"Cannot use alternative cost {alternativeCost.Description} for {card.Name}");
        }

        // CR 118.9 — Pitch alt-cost imposes an additional context check:
        // "If it's not your turn …". Force-of-Will-cycle spells embed this
        // timing predicate in the alt cost itself. Other alt-costs (Flashback,
        // Spectacle, Evoke, …) carry their own zone / state predicates inside
        // CanCastFor and don't need this hook. Keep the surface minimal —
        // SpellCastFlow stays generic, only this one concrete type gets the
        // activePlayer gate.
        if (alternativeCost is PitchAlternativeCost pitch
            && !pitch.IsLegalInContext(ctx.ActivePlayer))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name} via pitch: it is the caster's own turn (CR 118.9 timing gate).");
        }

        // CR 117.1 + CR 715.3b — Adventure alt-cost. A sorcery-typed
        // Adventure ("while on the stack as an Adventure, the spell has
        // only its alternative characteristics") must be cast at sorcery
        // speed even though the printed card is a Creature. SpellCastFlow
        // already skips the generic CastingPermission gate when an alt-cost
        // is supplied (per the alternativeCost == null check above), so the
        // Adventure-shaped sorcery-speed re-check lives here — same shape
        // PitchAlternativeCost uses for its activePlayer gate. Instant
        // Adventures return true unconditionally.
        if (alternativeCost is AdventureAlternativeCost adv
            && !adv.IsLegalInContext(ctx.ActivePlayer, ctx.CurrentPhase, _stack.IsEmpty, caster))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name} as Adventure: sorcery-speed restriction (CR 117.1 / 715.3b).");
        }

        // CR 702.138a — Escape alt-cost has a bundled "exile N other
        // graveyard cards" rider that must be paid as part of the
        // alt-cost (not as a generic IAdditionalCost — see
        // EscapeAlternativeCost xmldoc). Pre-check + pay it BEFORE any
        // other zone mutations so a graveyard with too few "other"
        // cards short-circuits the cast cleanly (CR 601.2g — illegal to
        // announce a cost you can't pay, no partial payment). The
        // Pay call atomically moves the picked cards Graveyard → Exile.
        if (alternativeCost is EscapeAlternativeCost escape)
        {
            if (!escape.Pay(caster, card))
            {
                throw new InvalidOperationException(
                    $"Cannot pay Escape exile rider for {card.Name}: " +
                    $"need {escape.ExileFromGraveyardCount} OTHER graveyard cards.");
            }
        }

        // CR 601.2f — additional costs first, before mana payment.
        // Merge the caller-supplied list with any costs the card itself
        // declares via SpellDefinition.AdditionalCosts (template-bound
        // "As an additional cost to cast this spell, sacrifice …" cards).
        var mergedAdditional = new List<IAdditionalCost>();
        if (definition.AdditionalCosts is { Count: > 0 } defCosts)
        {
            mergedAdditional.AddRange(defCosts);
        }
        if (additionalCosts != null)
        {
            mergedAdditional.AddRange(additionalCosts);
        }

        // Pre-check legality so we fail BEFORE mutating any zone — CR
        // 601.2g requires that if any cost can't be paid the cast is
        // illegal and the game is rewound. v1 short-circuit: if any cost
        // refuses, throw, no partial payment.
        foreach (var pre in mergedAdditional)
        {
            if (!pre.CanPay(caster))
            {
                throw new InvalidOperationException(
                    $"Cannot pay additional cost: {pre.Description}");
            }
        }

        foreach (var addCost in mergedAdditional)
        {
            if (!addCost.Pay(caster))
            {
                throw new InvalidOperationException(
                    $"Failed to pay additional cost: {addCost.Description}");
            }
        }

        // CR 701.59 — Bloomburrow Gift cast-time prompt. If the card
        // carries an IGiftClause (Into the Flood Maw etc.) the caster
        // may promise an opponent the named gift. The promise must be
        // recorded BEFORE target collection because Gift spells upgrade
        // their target predicate when promised (Flood Maw flips
        // "target creature an opponent controls" → "target nonland
        // permanent an opponent controls"); the resolve body branches
        // on Card.HasGiftPromised which is stamped here. Gift delivery
        // is a cast-time side-effect in v1 — see IGiftClause xmldoc for
        // the deviation from CR 701.59's resolve-time delivery (kept so
        // the gift survives a countered Gift spell, matching the engine
        // simplification documented in the test spec).
        Player? giftRecipient = null;
        if (card is IGiftClause giftClause && card is Card giftCardForPrompt)
        {
            var opponents = ctx.AllPlayers
                .Where(p => !ReferenceEquals(p, caster))
                .ToList();
            if (opponents.Count > 0)
            {
                giftRecipient = await agent.ChooseGiftRecipientAsync(
                    ctx, card, giftClause.Description, opponents, ct);
                if (giftRecipient != null)
                {
                    giftCardForPrompt.SetHasGiftPromised(true);
                }
            }
        }

        int? mode = null;
        IReadOnlyList<int>? modeIndexes = null;
        if (definition.Modes.Count > 0)
        {
            if (definition.RequiredModeCount > 1)
            {
                // CR 700.2d — multi-pick "Choose N —" prompt. Aggregate
                // per-mode intents into a single prompt intent (OR-mask)
                // so the heuristic bot's scoring path has signal even on
                // the interface-level (ctx-less) overload.
                var promptIntent = BotIntent.None;
                var miList = definition.ModeIntentsOrEmpty;
                for (var i = 0; i < miList.Count; i++) promptIntent |= miList[i];
                modeIndexes = await agent.ChooseModeAsync(
                    definition.Modes, promptIntent, definition.RequiredModeCount, ct);
                mode = modeIndexes.Count > 0 ? modeIndexes[0] : (int?)null;
            }
            else
            {
                mode = await agent.ChooseModeAsync(
                    ctx, definition.Modes, definition.ModeIntents, ct);
                modeIndexes = mode.HasValue ? new[] { mode.Value } : null;
            }
        }

        int? xValue = null;
        if (definition.HasVariableX)
        {
            xValue = await agent.ChooseXAsync(ctx, card, ct);

            // CR 202.3b — stamp the chosen X on the card itself so
            // permanents whose ETB references X (Chalice of the Void's
            // "enters with X charge counters", Walking Ballista, …) can
            // read the value without us threading ChosenSpellParams.X
            // through the spell → permanent boundary. Consumed + cleared
            // by the ETB effect (same pattern Murktide Regent uses for
            // PendingDelveExiledCount).
            if (card is Card concreteForX && xValue.HasValue)
            {
                concreteForX.SetPendingCastX(xValue.Value);
            }
        }

        var collectedTargets = new List<IReadOnlyList<object>>(definition.TargetRequests.Count);
        foreach (var req in definition.TargetRequests)
        {
            // Lazy-gather candidate pool against the live ctx (TargetRequest's
            // optional CandidateGatherer fires here). Falls through to the
            // request's static LegalCandidates when no gatherer is set.
            var live = req.ResolveCandidates(ctx);
            var promptReq = ReferenceEquals(live, req.LegalCandidates)
                ? req
                : req.WithCandidates(live);
            var picked = await agent.ChooseTargetsAsync(ctx, promptReq, ct);
            // CR 601.2c — cast is illegal if the agent can't pick enough
            // legal targets. Throw a typed exception so the caller (cast
            // dispatcher) can catch and abort cleanly instead of letting
            // EffectFactory crash on Targets[0][0].
            if (picked.Count < req.MinTargets)
            {
                throw new InvalidOperationException(
                    $"Cannot cast {card.Name}: target request '{req.Description}' " +
                    $"needs {req.MinTargets}, agent provided {picked.Count}.");
            }
            collectedTargets.Add(picked);
        }

        // CR 601.2f — stamp the freshly-picked targets onto the card so
        // any cost-reduction ability on the card itself can read them
        // during cost calculation below (Mystical Dispute's "costs {2}
        // less if it targets a blue spell"). Same Pending* idiom used
        // for X / Delve count above. Cleared after the spell hits the
        // stack so a later re-cast starts from a clean slate.
        if (card is Card concreteForTargets && collectedTargets.Count > 0)
        {
            concreteForTargets.SetPendingCastTargets(collectedTargets);
        }

        // Cost — printed + X, OR alternative cost when supplied. CR 117.7:
        // also subtract any CostReductionAbility on the card (Affinity etc.).
        var totalCost = alternativeCost?.AlternativeManaCost
            ?? Majik.Core.Costs.CostReduction.GetEffectiveCost(card, caster);
        if (xValue.HasValue && xValue.Value > 0)
        {
            totalCost = totalCost.AddGenericCost(xValue.Value);
        }

        // CR 702.66 — Delve. Each exiled graveyard card reduces the
        // spell's total generic mana by 1. Apply after X (X is generic
        // and is delve-payable per CR 702.66 + CR 601.2g order) and
        // after cost reduction. Pay the exile portion of the cost now —
        // CR 702.66b says delve is paid when the spell is cast.
        if (delveCost != null)
        {
            if (!delveCost.CanPay(caster, totalCost))
            {
                throw new InvalidOperationException(
                    $"Cannot pay Delve cost for {card.Name}: " +
                    $"selection of {delveCost.ReductionAmount} card(s) " +
                    $"invalid (generic={totalCost.Generic}).");
            }
            totalCost = delveCost.ApplyTo(totalCost);
            delveCost.Pay(caster);

            // CR 702.66 — stamp the count of delve-exiled cards on the card
            // itself so downstream ETB-with-counters effects (Murktide Regent
            // — CR 122.1g X-counter ETB) can read "cards exiled with me"
            // without us re-plumbing DelveCost across the spell-cast →
            // permanent boundary. Consumed + cleared by the ETB effect.
            if (card is Card concreteCard)
            {
                concreteCard.SetPendingDelveExiledCount(delveCost.ReductionAmount);
            }
        }

        // CR 702.127 — Improvise. "Each artifact you tap after you're done
        // activating mana abilities pays for {1}." The Improvise cost
        // already tapped the chosen artifacts in the CR 601.2f additional-
        // cost loop above; here we fold the generic-mana reduction into
        // the cost before the agent is prompted for the remaining mana
        // payment (CR 605.1 — mana abilities are settled by the time
        // ChooseManaSourcesAsync fires, satisfying the
        // "after you're done activating mana abilities" timing rule).
        // Coloured pips are preserved per CR 702.127.
        foreach (var addCost in mergedAdditional)
        {
            if (addCost is ImproviseAdditionalCost improvise && improvise.ReductionAmount > 0)
            {
                totalCost = improvise.ApplyTo(totalCost);
            }
        }

        // CR 702.51 — Convoke. "Each creature you tap while casting this
        // spell pays for {1} or one mana of that creature's color." Same
        // timing shape as Improvise: the chosen creatures were already
        // tapped in the CR 601.2f additional-cost loop above, and here we
        // fold the per-tap reduction (generic OR creature-coloured pip,
        // per CR 702.51b) into the mana cost before the agent's mana-
        // source prompt fires (CR 605.1 — mana abilities settled by then).
        foreach (var addCost in mergedAdditional)
        {
            if (addCost is ConvokeAdditionalCost convoke && convoke.ReductionAmount > 0)
            {
                totalCost = convoke.ApplyTo(totalCost);
            }
        }

        // CR 601.2g — mana sourcing. When the caller has already prompted +
        // paid mana (TurnDriver does this so a failed pay can rotate the
        // hand instead of mutating the stack), reuse that ManaPayment as
        // metadata so the agent isn't asked twice (visible UX bug: double
        // mana prompt). Otherwise prompt here as the canonical caster.
        var mana = preChosenMana
            ?? await agent.ChooseManaSourcesAsync(ctx, totalCost, ct);

        var chosen = new ChosenSpellParams(
            mode, xValue, collectedTargets, mana, ctx.AllPlayers,
            ModeIndexes: modeIndexes,
            AdditionalCostPayments: mergedAdditional.Count > 0 ? mergedAdditional : null);
        var effects = definition.EffectFactory(chosen);

        // CR 702.46 — Splice onto Arcane. After the Arcane spell's printed
        // body resolves we run each spliced rider's effects in announcement
        // order (CR 702.46b — multiple splice riders concatenate in the
        // order the caster announced them). The splice cost was already
        // paid in the CR 601.2f additional-cost loop above (mana drained,
        // Arcane + hand-residence gate enforced); here we only fold the
        // pre-built effect chain in. The spliced card itself stays in the
        // caster's hand (CR 702.46a — "the card stays in your hand"); no
        // zone move is performed for it.
        var spliceRiders = mergedAdditional.OfType<SpliceOntoArcaneCost>().ToList();
        if (spliceRiders.Count > 0)
        {
            var combined = effects.ToList();
            foreach (var rider in spliceRiders)
            {
                combined.AddRange(rider.BuildSplicedEffects(caster));
            }
            effects = combined;
        }

        // CR 601.2 / CR 113.5 — capture the source zone BEFORE the Hand →
        // Stack move so the "cast from hand" sentinel can branch on it.
        // Bedlam Reveler's ETB intervening-if reads this off the resolving
        // spell / card via Card.WasCastFromHand. Distinct from Card.WasCast
        // (any cast — flashback / suspend / from-graveyard included): this
        // flag is the strict "source zone was Hand" gate.
        var sourceZoneAtCast = card.Zone;

        // If casting via alternative cost (e.g. Flashback), card may not be in
        // hand — move it from whatever zone it's in.
        _zoneService.MoveCard(card, card.Zone, ZoneType.Stack, controller: caster);

        // Wrap effects so the alternative cost's OnResolved fires after the
        // spell's printed effects (CR 702.34b style).
        IReadOnlyList<IEffect> finalEffects = effects;
        if (alternativeCost != null)
        {
            var wrapped = effects.Append(new Effect(
                $"{alternativeCost.Description} cleanup",
                () => alternativeCost.OnResolved(card, caster))).ToList();
            finalEffects = wrapped;
        }

        // CR 702.33b / CR 400.7 — if a kicker additional cost was paid
        // this cast, append a cleanup effect that clears
        // <see cref="Card.WasKicked"/> after the spell's printed body
        // resolves so the sentinel doesn't leak to a copy / blink /
        // re-cast. KickerAdditionalCost.Pay stamps the flag during the
        // additional-cost loop above; the spell-level mirror stamp
        // (see below) is read by stack-side gates that don't have
        // the card handy.
        bool hasKickerPayment = false;
        foreach (var addCost in mergedAdditional)
        {
            if (addCost is KickerAdditionalCost)
            {
                hasKickerPayment = true;
                break;
            }
        }
        if (hasKickerPayment)
        {
            var withKickerCleanup = finalEffects.Append(new Effect(
                "Kicker cleanup — clear Card.WasKicked",
                () =>
                {
                    if (card is Card concreteForKicked)
                    {
                        concreteForKicked.ClearWasKicked();
                    }
                })).ToList();
            finalEffects = withKickerCleanup;
        }

        // CR 701.59 — Gift cleanup. After the printed body runs (and
        // its resolve-time branch has read Card.HasGiftPromised), clear
        // the sentinel so a later re-cast / blink / token copy doesn't
        // inherit the prior promise (CR 400.7 — new object per zone
        // change). Mirrors the Kicker cleanup append directly above.
        if (giftRecipient != null)
        {
            var withGiftCleanup = finalEffects.Append(new Effect(
                "Gift cleanup — clear Card.HasGiftPromised",
                () =>
                {
                    if (card is Card concreteForGift)
                    {
                        concreteForGift.ClearHasGiftPromised();
                    }
                })).ToList();
            finalEffects = withGiftCleanup;
        }

        var spell = new Spells.Spell(card, caster, effects: finalEffects);

        // CR 608.2 / CR 715.3d — let the alt-cost re-route the post-
        // resolution destination away from the printed-type default
        // (Adventure exiles a Creature card; future "exile if it would
        // be put in graveyard" riders can reuse the same hook). The
        // StackResolver reads this in preference to the printed-type
        // check when deciding where the card lands after Spell.Resolve().
        if (alternativeCost?.PostResolutionZone is { } overrideZone)
        {
            spell.PostResolutionZoneOverride = overrideZone;
        }

        // CR 118 — Roiling Vortex / Eidolon of the Great Revel-style
        // "no mana was spent to cast it" sentinel. After all cost
        // collapses (printed → alternative → cost reductions → +X →
        // Delve), if the resulting totalCost is zero across every bucket
        // then no mana payment was actually made for this cast. Stamping
        // the spell rather than diffing buckets at trigger time lets
        // downstream consumers ignore the cost machinery entirely.
        spell.WasFreeCast = totalCost.IsZero;

        // CR 702.138b — stamp the "escaped" sentinel so downstream gates
        // (Uro's "sacrifice it unless it escaped" trigger; future
        // "escapes with [counters]" replacements per CR 702.138c) can
        // read it off the resolving spell + resulting permanent. We
        // stamp the Card too — Uro's sac trigger fires on the
        // battlefield, sourced off the resolved permanent, and that
        // is the easiest read site for the gate body.
        if (alternativeCost is EscapeAlternativeCost)
        {
            spell.WasCastForEscape = true;
            if (card is Card concreteForEscape)
            {
                concreteForEscape.SetWasCastForEscape(true);
            }
        }

        // CR 702.62d / 702.62g — stamp the "cast via suspend" sentinel on
        // the resolving spell + underlying card whenever the alt-cost is
        // the suspend "cast for free" payoff. The Card-side mirror lets
        // resolve-body reads (creature haste gate; future "if cast via
        // suspend" triggers) consult the flag without the spell handle.
        if (alternativeCost is CastFromExileAlternativeCost { IsSuspendCast: true })
        {
            spell.WasCastFromSuspend = true;
            if (card is Card concreteForSuspend)
            {
                concreteForSuspend.SetWasCastFromSuspend(true);
            }
        }

        // CR 702.33b — mirror the kicked posture onto the resolving
        // stack object so stack-side gates that don't have the card
        // reference handy (downstream triggers, future "casts a
        // kicked spell" replacements) can read it off the spell.
        if (hasKickerPayment)
        {
            spell.WasKicked = true;
        }

        // CR 701.5b — "An uncounterable spell can't be countered." Cards
        // that print "this spell can't be countered" (Emrakul, the Aeons
        // Torn; Apocalypse Hydra; …) carry a
        // <see cref="KeywordAbility"/>("Uncounterable") marker; the
        // resolving spell mirrors the flag so
        // <see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>
        // can veto the counter-attempt without re-reading the card's
        // abilities at counter resolution time.
        if (HasUncounterableMarker(card))
        {
            spell.CannotBeCountered = true;
        }

        // CR 701.59 — stamp the Gift recipient onto the resolving spell
        // and deliver the promised gift NOW (cast-time delivery — see
        // IGiftClause xmldoc for the v1 deviation from strict CR 701.59
        // resolve-time delivery; the engine simplification keeps the
        // gift in the recipient's hand even when the gift-bearing spell
        // is later countered, matching the test spec).
        if (giftRecipient != null && card is IGiftClause giftClauseForDelivery)
        {
            spell.GiftRecipient = giftRecipient;
            giftClauseForDelivery.DeliverTo(giftRecipient, spell);
        }

        // CR 113.5 / CR 400.7 — stamp the persistent cast marker on the
        // underlying card just before the spell hits the stack. The flag
        // survives Stack → Battlefield so ETB triggers ("when ~ enters,
        // if you cast it, …" — The One Ring) and battlefield-entry
        // replacements ("if it wasn't cast" — Containment Priest, via
        // ZoneMoveIntent.WasCast which ZoneService populates from this
        // field) read the same truth. ZoneService clears the flag when
        // the permanent later leaves the battlefield.
        if (card is Card concreteForCast)
        {
            concreteForCast.SetWasCast(true);
        }

        // CR 601.2 / CR 113.5 — stamp the strict "cast from hand" sentinel
        // when the source zone captured before the stack-push was Hand.
        // Read by ETB intervening-if clauses keyed on "if you cast it from
        // your hand" (Bedlam Reveler). Distinct from Card.WasCast which
        // fires on any cast — flashback / suspend / from-graveyard / from-
        // exile all set WasCast but leave WasCastFromHand false. The flag
        // is cleared by ZoneService on battlefield exit (any destination),
        // mirroring WasCast's CR 400.7 lifecycle.
        if (sourceZoneAtCast == ZoneType.Hand)
        {
            spell.WasCastFromHand = true;
            if (card is Card concreteForHandCast)
            {
                concreteForHandCast.SetWasCastFromHand(true);
            }
        }

        _stack.Push(spell);
        _eventBus.Publish(new SpellCastEvent(spell));

        // Clear the pending-targets stamp — the spell is on the stack and
        // its ChosenSpellParams.Targets is the authoritative source from
        // here on. Cost-calc only needed the stamp for the brief window
        // between target collection and the GetEffectiveCost call.
        if (card is Card concreteToClear)
        {
            concreteToClear.ClearPendingCastTargets();
        }

        return spell;
    }

    /// <summary>
    /// CR 701.5b helper — scan <paramref name="card"/>'s static abilities for
    /// a <see cref="KeywordAbility"/>("Uncounterable") marker. Matches the
    /// pattern used elsewhere in the engine for keyword discoverability
    /// (Annihilator, Indestructible, etc.) so future per-cast stamp sites
    /// (Vexing Shusher's "counter target spell that targets a green spell
    /// you control" trigger; deck-level riders) can reuse the same shape.
    /// Case-insensitive on the keyword string.
    /// </summary>
    private static bool HasUncounterableMarker(ICard card) =>
        card.Abilities
            .OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Uncounterable", StringComparison.OrdinalIgnoreCase));
}

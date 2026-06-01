using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
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
            || ctx.CurrentPhase is not { } companionPhase
            || !companionPhase.IsMain())
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

        // CR 601.2a — verify casting permission + alt-cost legality BEFORE any
        // mutation (sorcery-speed gate, alt-cost CanCastFor, Pitch/Surge/
        // Adventure context predicates).
        ValidateCastingPermissionAndAltCost(card, caster, ctx, alternativeCost);

        // CR 702.138a — Escape's bundled "exile N other graveyard cards" rider
        // is paid as part of the alt-cost itself, before other zone mutations.
        PayEscapeRiderIfAny(card, caster, alternativeCost);

        // CR 601.2f — merge caller-supplied additional costs with the ones the
        // SpellDefinition itself declares, pre-check legality, then pay them.
        var mergedAdditional = BuildAndPayAdditionalCosts(definition, additionalCosts, caster);

        // CR 701.59 — Gift cast-time prompt (must run BEFORE target collection
        // because Gift spells upgrade their target predicate when promised).
        var giftRecipient = await PromptForGiftRecipientAsync(card, caster, ctx, agent, ct);

        // CR 700.2 — choose modes (modal spells).
        int? mode = await PromptForModeAsync(definition, ctx, agent, ct);

        // CR 601.2e + CR 202.3b — choose X and stamp the value on the card so
        // permanents whose ETB references X can read it.
        int? xValue = await PromptForXAsync(definition, card, ctx, agent, ct);

        // CR 601.2c — collect targets in declaration order.
        var collectedTargets = await CollectTargetsAsync(definition, card, ctx, agent, ct);

        // CR 601.2f — compute the post-reduction total cost (printed cost OR
        // alt cost; + X; − cost reductions; − Delve, Improvise, Convoke
        // reductions). Delve pays its exile portion here per CR 702.66b.
        var totalCost = ComputeAndApplyTotalCost(
            card, caster, ctx, alternativeCost, xValue, delveCost, mergedAdditional);

        // CR 601.2g / CR 605.1 — mana sourcing. Reuse pre-chosen mana when the
        // caller (TurnDriver) already prompted, otherwise prompt the agent.
        var mana = preChosenMana ?? await agent.ChooseManaSourcesAsync(ctx, totalCost, ct);

        var chosen = new ChosenSpellParams(
            mode, xValue, collectedTargets, mana, ctx.AllPlayers,
            ModeIndexes: null,
            AdditionalCostPayments: mergedAdditional.Count > 0 ? mergedAdditional : null);
        var effects = definition.EffectFactory(chosen);

        // CR 702.46 — Splice onto Arcane: fold each spliced rider's effects
        // into the spell's effect chain in announcement order.
        effects = ApplySpliceRiders(effects, mergedAdditional, caster);

        // CR 601.2 / CR 113.5 — capture source zone BEFORE the Hand → Stack
        // move so the "cast from hand" sentinel can branch on it.
        var sourceZoneAtCast = card.Zone;
        _zoneService.MoveCard(card, card.Zone, ZoneType.Stack, controller: caster);

        // CR 702.34b / 702.33b / 702.115 / 701.59 — append per-feature cleanup
        // effects (alt-cost OnResolved, Kicker/Surge/Gift sentinel-clear).
        bool hasKickerPayment = mergedAdditional.OfType<KickerAdditionalCost>().Any();
        var finalEffects = AppendCleanupEffects(
            effects, card, caster, alternativeCost, hasKickerPayment, giftRecipient);

        var spell = new Spells.Spell(card, caster, effects: finalEffects);

        // CR 608.2 / 715.3d / 118 / 702.138b / 702.62d / 702.33b / 702.115 /
        // 701.5b / 701.59 / 113.5 / 601.2 — stamp every per-cast sentinel on
        // the spell + underlying card so downstream gates can branch on them.
        StampSpellAndCardSentinels(
            spell, card, caster, alternativeCost, totalCost,
            hasKickerPayment, giftRecipient, sourceZoneAtCast);

        // CR 702.10 / 106.4 — the mana-provenance haste rider (Arena of Glory)
        // is now applied at PAY time by ManaPaymentResolver firing the tagged
        // mana's OnSpent reaction (slot-level provenance, deferral #1), so it
        // attaches strictly to the creature the exert mana paid for — not to
        // "the first spell cast after the exert" as the old player-scoped
        // counter did. No cast-time provenance step is needed here.

        _stack.Push(spell);
        _eventBus.Publish(new SpellCastEvent(spell));

        // Clear the pending-targets stamp — once the spell is on the stack,
        // ChosenSpellParams.Targets is authoritative.
        if (card is Card concreteToClear)
        {
            concreteToClear.ClearPendingCastTargets();
        }

        // CR 601.3 — decrement the per-player additional-spell allowance
        // counter (Irencrag Feat: "You can cast only one more spell this turn.")
        // so ActionValidator can gate the next cast attempt.
        Majik.Core.Rules.CastingRestrictions.ConsumeAdditionalSpellAllowance(caster);

        return spell;
    }

    // ---------------------------------------------------------------------
    // Per-step helpers — one extraction per CR 601.2 sub-rule (plus the
    // associated alt-cost predicates). Documented with the rule cite the
    // original inline comment carried; behaviour is identical.
    // ---------------------------------------------------------------------

    /// <summary>CR 117.1 / 118.9 / 702.115a / 117.1+715.3b — sorcery-speed
    /// gate + alternative-cost legality + per-alt context predicates
    /// (Pitch / Surge / Adventure). Throws when any precondition fails.
    /// </summary>
    private void ValidateCastingPermissionAndAltCost(
        ICard card, Player caster, GameContext ctx, IAlternativeCost? alternativeCost)
    {
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
        // timing predicate in the alt cost itself.
        if (alternativeCost is PitchAlternativeCost pitch
            && !pitch.IsLegalInContext(ctx.ActivePlayer))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name} via pitch: it is the caster's own turn (CR 118.9 timing gate).");
        }

        // CR 702.115a — Surge alt-cost. Gated on "you or a teammate has
        // cast another spell this turn".
        if (alternativeCost is SurgeAlternativeCost surge
            && !surge.IsLegalInContext(caster))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name} via surge: caster has not cast another spell this turn (CR 702.115a).");
        }

        // CR 117.1 + CR 715.3b — Adventure alt-cost. A sorcery-typed
        // Adventure must be cast at sorcery speed.
        if (alternativeCost is AdventureAlternativeCost adv
            && !adv.IsLegalInContext(ctx.ActivePlayer, ctx.CurrentPhase, _stack.IsEmpty, caster))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name} as Adventure: sorcery-speed restriction (CR 117.1 / 715.3b).");
        }
    }

    /// <summary>CR 702.138a — Escape alt-cost's bundled "exile N other
    /// graveyard cards" rider. Atomically moves the picked cards Graveyard →
    /// Exile. Throws on insufficient yard.</summary>
    private static void PayEscapeRiderIfAny(
        ICard card, Player caster, IAlternativeCost? alternativeCost)
    {
        if (alternativeCost is not EscapeAlternativeCost escape) return;
        if (!escape.Pay(caster, card))
        {
            throw new InvalidOperationException(
                $"Cannot pay Escape exile rider for {card.Name}: " +
                $"need {escape.ExileFromGraveyardCount} OTHER graveyard cards.");
        }
    }

    /// <summary>CR 601.2f — additional costs first, before mana payment.
    /// Merges caller-supplied costs with definition-supplied costs,
    /// pre-checks legality (CR 601.2g — no partial payment), then pays
    /// each.</summary>
    private static List<IAdditionalCost> BuildAndPayAdditionalCosts(
        SpellDefinition definition,
        IReadOnlyList<IAdditionalCost>? additionalCosts,
        Player caster)
    {
        var mergedAdditional = new List<IAdditionalCost>();
        if (definition.AdditionalCosts is { Count: > 0 } defCosts)
        {
            mergedAdditional.AddRange(defCosts);
        }
        if (additionalCosts != null)
        {
            mergedAdditional.AddRange(additionalCosts);
        }

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

        return mergedAdditional;
    }

    /// <summary>CR 701.59 — Bloomburrow Gift cast-time prompt. Promised gifts
    /// upgrade target predicates and are read at resolve time via
    /// Card.HasGiftPromised.</summary>
    private static async Task<Player?> PromptForGiftRecipientAsync(
        ICard card, Player caster, GameContext ctx, IPlayerAgent agent, CancellationToken ct)
    {
        if (card is not IGiftClause giftClause || card is not Card giftCardForPrompt)
        {
            return null;
        }
        var opponents = ctx.AllPlayers.Where(p => !ReferenceEquals(p, caster)).ToList();
        if (opponents.Count == 0) return null;

        // PLAN 01 (Slice G) — the bespoke ChooseGiftRecipientAsync prompt is
        // gone; the gift recipient is now an optional declarative PickOne over
        // the opponent pool, surfaced through the single ChooseAsync sink.
        // Empty result == decline the gift (CR 701.59 — "you may promise").
        var giftRequest = new ChoiceRequest(
            ChoiceKind.PickOne,
            giftClause.Description,
            Min: 0,
            Max: 1,
            Candidates: opponents.Cast<object>().ToList(),
            Intent: BotIntent.None,
            Optional: true);
        var chosen = await agent.ChooseAsync(ctx, giftRequest, ct);
        var giftRecipient = chosen.Count > 0 ? (Player)chosen[0] : null;
        if (giftRecipient != null)
        {
            giftCardForPrompt.SetHasGiftPromised(true);
        }
        return giftRecipient;
    }

    /// <summary>CR 700.2 — modal-spell mode prompt.</summary>
    private static async Task<int?> PromptForModeAsync(
        SpellDefinition definition, GameContext ctx, IPlayerAgent agent, CancellationToken ct)
    {
        if (definition.Modes.Count == 0) return null;
        return await agent.ChooseModeAsync(ctx, definition.Modes, definition.ModeIntents, ct);
    }

    /// <summary>CR 601.2e + CR 202.3b — choose X and stamp it on the card so
    /// ETB-with-X-counters effects (Chalice of the Void, Walking Ballista)
    /// can read it without threading ChosenSpellParams.</summary>
    private static async Task<int?> PromptForXAsync(
        SpellDefinition definition, ICard card, GameContext ctx, IPlayerAgent agent, CancellationToken ct)
    {
        if (!definition.HasVariableX) return null;
        var xValue = await agent.ChooseXAsync(ctx, card, ct);
        if (card is Card concreteForX)
        {
            concreteForX.SetPendingCastX(xValue);
        }
        return xValue;
    }

    /// <summary>CR 601.2c — collect targets in declaration order, lazy-
    /// gathering candidate pools against the live ctx. Throws when the agent
    /// can't pick enough legal targets (cast is illegal).</summary>
    private static async Task<List<IReadOnlyList<object>>> CollectTargetsAsync(
        SpellDefinition definition, ICard card, GameContext ctx, IPlayerAgent agent, CancellationToken ct)
    {
        // PLAN 01 (Slice E) — one shared targeting pipeline. CR 601.2c: an
        // agent that can't supply enough legal targets makes the cast illegal,
        // so the spell path enforces min cardinality.
        var collectedTargets = await Targeting.TargetCollection.CollectAsync(
            definition.TargetRequests,
            card,
            ctx,
            agent,
            throwOnInsufficient: true,
            ct);

        // CR 601.2f — stamp the freshly-picked targets onto the card so any
        // cost-reduction ability on the card itself can read them during cost
        // calculation (Mystical Dispute's "costs {2} less if it targets a blue
        // spell").
        if (card is Card concreteForTargets && collectedTargets.Count > 0)
        {
            concreteForTargets.SetPendingCastTargets(collectedTargets);
        }
        return collectedTargets;
    }

    /// <summary>CR 117.7 / 601.2f / 702.66 / 702.127 / 702.51 — compute the
    /// effective mana cost: alt-cost OR printed-with-reductions, plus X,
    /// minus Delve (and pay its exile rider per CR 702.66b), minus Improvise
    /// + Convoke reductions.</summary>
    private static ManaCost ComputeAndApplyTotalCost(
        ICard card,
        Player caster,
        GameContext ctx,
        IAlternativeCost? alternativeCost,
        int? xValue,
        DelveCost? delveCost,
        IReadOnlyList<IAdditionalCost> mergedAdditional)
    {
        var totalCost = alternativeCost?.AlternativeManaCost
            ?? Majik.Core.Costs.CostReduction.GetEffectiveCost(card, caster, ctx.AllPlayers);
        if (xValue is { } xv && xv > 0)
        {
            totalCost = totalCost.AddGenericCost(xv);
        }

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

            // CR 702.66 — stamp the delve-exile count for ETB-with-counters
            // (Murktide Regent — CR 122.1g X-counter ETB).
            if (card is Card concreteCard)
            {
                concreteCard.SetPendingDelveExiledCount(delveCost.ReductionAmount);
            }
        }

        // CR 702.127 — Improvise generic reduction (artifacts tapped in the
        // additional-cost loop above).
        foreach (var addCost in mergedAdditional)
        {
            if (addCost is ImproviseAdditionalCost improvise && improvise.ReductionAmount > 0)
            {
                totalCost = improvise.ApplyTo(totalCost);
            }
        }

        // CR 702.51 — Convoke generic-or-coloured reduction (creatures tapped
        // in the additional-cost loop above).
        foreach (var addCost in mergedAdditional)
        {
            if (addCost is ConvokeAdditionalCost convoke && convoke.ReductionAmount > 0)
            {
                totalCost = convoke.ApplyTo(totalCost);
            }
        }

        // CR 601.2f + CR 117.7c — March cycle generic reduction (cards
        // exiled from hand in the additional-cost loop above). {2} per
        // exiled card, floored at zero. Applied AFTER X is folded into
        // Generic so the reduction can eat the X portion uniformly.
        foreach (var addCost in mergedAdditional)
        {
            if (addCost is MarchAdditionalCost march && march.ReductionAmount > 0)
            {
                totalCost = march.ApplyTo(totalCost);
            }
        }

        return totalCost;
    }

    /// <summary>CR 702.46 — Splice onto Arcane: spliced riders' effects
    /// concatenate after the printed body in announcement order
    /// (CR 702.46b).</summary>
    private static IReadOnlyList<IEffect> ApplySpliceRiders(
        IReadOnlyList<IEffect> effects, IReadOnlyList<IAdditionalCost> mergedAdditional, Player caster)
    {
        var spliceRiders = mergedAdditional.OfType<SpliceOntoArcaneCost>().ToList();
        if (spliceRiders.Count == 0) return effects;
        var combined = effects.ToList();
        foreach (var rider in spliceRiders)
        {
            combined.AddRange(rider.BuildSplicedEffects(caster));
        }
        return combined;
    }

    /// <summary>CR 702.34b / 702.33b / 702.115 / 701.59 — append per-feature
    /// cleanup effects (alt-cost OnResolved, Kicker / Surge / Gift sentinel
    /// clears). Each cleanup matches the CR 400.7 "new object per zone
    /// change" lifecycle so a later re-cast / blink / token copy starts
    /// clean.</summary>
    private static IReadOnlyList<IEffect> AppendCleanupEffects(
        IReadOnlyList<IEffect> effects,
        ICard card,
        Player caster,
        IAlternativeCost? alternativeCost,
        bool hasKickerPayment,
        Player? giftRecipient)
    {
        IReadOnlyList<IEffect> finalEffects = effects;
        if (alternativeCost != null)
        {
            finalEffects = finalEffects.Append(new Effect(
                $"{alternativeCost.Description} cleanup",
                () => alternativeCost.OnResolved(card, caster))).ToList();
        }
        if (hasKickerPayment)
        {
            finalEffects = finalEffects.Append(new Effect(
                "Kicker cleanup — clear Card.WasKicked",
                () =>
                {
                    if (card is Card concreteForKicked)
                    {
                        concreteForKicked.ClearWasKicked();
                    }
                })).ToList();
        }
        if (alternativeCost is SurgeAlternativeCost)
        {
            finalEffects = finalEffects.Append(new Effect(
                "Surge cleanup — clear Card.WasCastForSurge",
                () =>
                {
                    if (card is Card concreteForSurgeCleanup)
                    {
                        concreteForSurgeCleanup.ClearWasCastForSurge();
                    }
                })).ToList();
        }
        if (giftRecipient != null)
        {
            finalEffects = finalEffects.Append(new Effect(
                "Gift cleanup — clear Card.HasGiftPromised",
                () =>
                {
                    if (card is Card concreteForGift)
                    {
                        concreteForGift.ClearHasGiftPromised();
                    }
                })).ToList();
        }
        return finalEffects;
    }

    /// <summary>CR 608.2 / 715.3d / 118 / 702.138b / 702.62d / 702.33b /
    /// 702.115 / 701.5b / 701.59 / 113.5 / 601.2 — stamp every per-cast
    /// sentinel on the resolving spell + (mirrored) on the underlying Card so
    /// stack-side and battlefield-side gates can read the same truth without
    /// the spell handle.</summary>
    private static void StampSpellAndCardSentinels(
        Spells.Spell spell,
        ICard card,
        Player caster,
        IAlternativeCost? alternativeCost,
        ManaCost totalCost,
        bool hasKickerPayment,
        Player? giftRecipient,
        ZoneType sourceZoneAtCast)
    {
        // CR 608.2 / CR 715.3d — alt-cost post-resolution zone override.
        if (alternativeCost?.PostResolutionZone is { } overrideZone)
        {
            spell.PostResolutionZoneOverride = overrideZone;
        }

        // CR 118 — "no mana was spent" sentinel (Roiling Vortex, Eidolon).
        spell.WasFreeCast = totalCost.IsZero;

        // CR 702.138b — Escape sentinel (Uro's "sacrifice unless escaped").
        if (alternativeCost is EscapeAlternativeCost)
        {
            spell.WasCastForEscape = true;
            if (card is Card concreteForEscape)
            {
                concreteForEscape.SetWasCastForEscape(true);
            }
        }

        // CR 702.62d / 702.62g — suspend cast sentinel.
        if (alternativeCost is CastFromExileAlternativeCost { IsSuspendCast: true })
        {
            spell.WasCastFromSuspend = true;
            if (card is Card concreteForSuspend)
            {
                concreteForSuspend.SetWasCastFromSuspend(true);
            }
        }

        // CR 702.33b — kicked posture mirror.
        if (hasKickerPayment)
        {
            spell.WasKicked = true;
        }

        // CR 702.115 — Surge posture mirror onto the underlying card so
        // resolve-body branches (Reckless Bushwhacker) can read it even before
        // SurgeAlternativeCost.OnResolved fires.
        if (alternativeCost is SurgeAlternativeCost
            && card is Card concreteForSurge)
        {
            concreteForSurge.SetWasCastForSurge(true);
        }

        // CR 701.5b — Uncounterable keyword marker (card carries the ability).
        if (HasUncounterableMarker(card))
        {
            spell.CannotBeCountered = true;
        }

        // CR 701.5b — one-shot "next spell can't be countered" rider from an
        // activated ability (e.g. Mistrise Village's {U}{T} activation). The
        // flag is consumed on the first cast so only that spell benefits.
        // ConsumeNextSpellUncounterableForTurn returns true exactly once per
        // activation and clears the entry; the OR preserves an existing stamp.
        if (!spell.CannotBeCountered
            && Majik.Core.Rules.CastingRestrictions.ConsumeNextSpellUncounterableForTurn(caster))
        {
            spell.CannotBeCountered = true;
        }

        // CR 701.59 — stamp Gift recipient + deliver the promised gift NOW
        // (v1 cast-time delivery — see IGiftClause xmldoc).
        if (giftRecipient != null && card is IGiftClause giftClauseForDelivery)
        {
            spell.GiftRecipient = giftRecipient;
            giftClauseForDelivery.DeliverTo(giftRecipient, spell);
        }

        // CR 113.5 / CR 400.7 — persistent "was cast" marker (survives Stack →
        // Battlefield for ETB triggers like The One Ring; cleared by
        // ZoneService on battlefield exit).
        if (card is Card concreteForCast)
        {
            concreteForCast.SetWasCast(true);
        }

        // CR 601.2 / CR 113.5 — strict "cast from hand" sentinel for ETB
        // intervening-if clauses (Bedlam Reveler).
        if (sourceZoneAtCast == ZoneType.Hand)
        {
            spell.WasCastFromHand = true;
            if (card is Card concreteForHandCast)
            {
                concreteForHandCast.SetWasCastFromHand(true);
            }
        }

        // CR 601.2 / CR 113.5 — strict "cast from library" sentinel for
        // ETB intervening-if clauses (Fblthp, the Lost's draw-2 rider).
        if (sourceZoneAtCast == ZoneType.Library)
        {
            spell.WasCastFromLibrary = true;
            if (card is Card concreteForLibraryCast)
            {
                concreteForLibraryCast.SetWasCastFromLibrary(true);
            }
        }
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

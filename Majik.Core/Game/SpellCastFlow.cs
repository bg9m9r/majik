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
///   0a. CR 601.2a — move the spell to the stack (done first for a HAND cast;
///       deferred to step 6 for non-hand origins whose gates/riders read the
///       origin zone live — see the move sites below). A hand cast that later
///       proves illegal is rewound off the stack (CR 731.1).
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
        DelveCost? delveCost = null,
        Func<ManaCost, bool>? payManaCost = null,
        int? preChosenX = null)
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

        // CR 601.2 / CR 113.5 — capture the origin zone BEFORE any move so the
        // per-source-zone sentinels (cast-from-hand / -library / -graveyard) can
        // branch on it, AND so the Hand→Stack move below can be gated on it.
        var sourceZoneAtCast = card.Zone;

        // CR 601.2a — "the player moves the spell from where it is to the
        // stack" is the FIRST proposal step. For a HAND-sourced cast we honour
        // that ordering literally: the card is placed on the stack here, before
        // cost determination (CR 601.2f) and target collection (CR 601.2c), so a
        // cast-time observer (a "whenever you cast" watcher or a self-
        // referential cost/target predicate) sees the card on the stack rather
        // than still in hand.
        //
        // The move is gated to the Hand source ON PURPOSE — the late move below
        // is retained for every OTHER origin zone (Library / Graveyard / Exile),
        // where deferring is REQUIRED, not cosmetic:
        //   * the Library-top-cast permission (CR 601.3e) and an alt-cost's
        //     CanCastFor zone restriction (CR 118.9) read card.Zone live in
        //     ValidateCastingPermissionAndAltCost above; a hand card trips
        //     neither gate (Library auth is skipped when Zone != Library, and a
        //     hand cast carries no zone-restricted alt cost path through here);
        //   * the from-graveyard riders (Delve's CR 702.66b exile, Escape's
        //     CR 702.138a exile, already paid above) move OTHER graveyard cards
        //     while the cast card is itself still in the graveyard — moving it
        //     early would corrupt the "N OTHER cards" count. A hand card is in
        //     none of those riders' source zones.
        // Because nothing between here and Push observes the card on the stack
        // in a way that differs for a hand card, and CR 731.1 makes the whole
        // proposal atomic anyway, the early hand move is behaviourally safe.
        if (sourceZoneAtCast == ZoneType.Hand)
        {
            _zoneService.MoveCard(card, ZoneType.Hand, ZoneType.Stack, controller: caster);
        }

        try
        {
            return await ProposeAndPushAsync(
                caster, card, definition, agent, ctx, ct,
                additionalCosts, alternativeCost, preChosenMana, delveCost,
                payManaCost, preChosenX, sourceZoneAtCast);
        }
        catch when (sourceZoneAtCast == ZoneType.Hand && card.Zone == ZoneType.Stack)
        {
            // CR 731.1 / CR 728.x — the proposal became illegal AFTER the
            // CR 601.2a Hand→Stack move (insufficient targets, unpayable
            // additional cost, failed mana payment). "The entire action is
            // reversed and any payments already made are canceled" — so the
            // card is rewound off the stack back to its origin hand before the
            // exception propagates. (Non-hand origins never entered the early
            // move, so they have nothing to rewind.) The card has not been
            // Pushed onto the stack object yet, so this is a pure zone rewind.
            _zoneService.MoveCard(card, ZoneType.Stack, ZoneType.Hand, controller: caster);
            throw;
        }
    }

    /// <summary>
    /// CR 601.2b–h — the remaining spell-proposal steps once the card is (for a
    /// hand cast) already on the stack: additional-cost merge, mode / X / target
    /// choice, total-cost determination, the non-mana + mana payments, effect
    /// construction, the (non-hand) late zone move, sentinel stamping, and the
    /// final <see cref="Majik.Core.Stack.Stack.Push"/> + SpellCastEvent publish.
    /// Extracted so <see cref="CastAsync"/> can wrap it in the CR 731.1 rewind
    /// guard that returns a hand-cast card to hand if any step throws.
    /// </summary>
    private async Task<Spells.Spell> ProposeAndPushAsync(
        Player caster,
        ICard card,
        SpellDefinition definition,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct,
        IReadOnlyList<IAdditionalCost>? additionalCosts,
        IAlternativeCost? alternativeCost,
        Majik.Core.Players.Agents.ManaPayment? preChosenMana,
        DelveCost? delveCost,
        Func<ManaCost, bool>? payManaCost,
        int? preChosenX,
        ZoneType sourceZoneAtCast)
    {
        // CR 601.2f — merge caller-supplied additional costs with the ones the
        // SpellDefinition itself declares and pre-check legality (CR 601.2g —
        // no partial payment). The actual PAYMENT is deferred to the
        // CR 601.2h point below (next to the mana payment), AFTER target
        // collection (CR 601.2c) — so a targeting failure throws before any
        // discard / sacrifice / exile rider is paid (CR 731.1 rewind).
        var mergedAdditional = BuildAndPrecheckAdditionalCosts(definition, additionalCosts, caster);

        // CR 702.51 — cast-time Convoke creature-tap prompt. When the card
        // carries a "Convoke" keyword marker and the caller did NOT already
        // supply a ConvokeAdditionalCost (the bot probe path pre-builds one),
        // ask the caster's agent which untapped creatures they control to tap.
        // The chosen creatures fold into mergedAdditional as a ConvokeAdditionalCost
        // so the CR 601.2h payment loop taps them and ComputeAndApplyTotalCost
        // applies the pip reduction. Runs BEFORE total-cost determination so the
        // reduced cost reaches the mana prompt + payManaCost callback.
        await PromptForConvokeAsync(card, caster, ctx, agent, mergedAdditional, ct);

        // CR 701.59 — Gift cast-time prompt (must run BEFORE target collection
        // because Gift spells upgrade their target predicate when promised).
        var giftRecipient = await PromptForGiftRecipientAsync(card, caster, ctx, agent, ct);

        // CR 700.2 — choose modes (modal spells). Single-mode ("Choose one")
        // spells return a scalar; multi-mode ("Choose one or more" / "Choose
        // two") spells return the chosen set (CR 700.2d). The scalar is kept
        // in sync with the first chosen mode so legacy single-mode
        // EffectFactory closures still read ChosenSpellParams.ModeIndex.
        var modeChoice = await PromptForModesAsync(definition, ctx, agent, ct);
        int? mode = modeChoice.Count > 0 ? modeChoice[0] : (int?)null;
        IReadOnlyList<int>? modeIndexes =
            definition.IsMultiMode && modeChoice.Count > 0 ? modeChoice : null;

        // CR 702.121 — Escalate. Build one escalate additional-cost instance
        // for each mode chosen BEYOND the first (CR 702.121a), pre-check that
        // the whole escalate bill is affordable (CR 601.2g — no partial
        // payment), and append the instances to the merged additional costs so
        // downstream effect closures / cleanup see them. PAYMENT is deferred to
        // the CR 601.2h point below (alongside the other additional costs and
        // mana), so an unpayable escalate or a later targeting failure aborts
        // the cast before any escalate discard / pay-life is committed.
        if (definition.Escalate is { } escalate && modeChoice.Count > 1)
        {
            BuildAndPrecheckEscalateCosts(escalate, card, caster, modeChoice.Count - 1, mergedAdditional);
        }

        // CR 601.2e + CR 202.3b — choose X and stamp the value on the card so
        // permanents whose ETB references X can read it. When the caller (the
        // TurnDriver / GameFacade dispatch path) already prompted X BEFORE
        // collecting the mana sources — so the dispatcher's mana prompt +
        // payManaCost callback could see the X-inclusive cost (CR 601.2b/f) —
        // reuse that pre-chosen value here instead of re-prompting (CR 601.2g:
        // one X announcement per cast). Still stamp PendingCastX so ETB-with-X
        // effects read it even on the dispatch path.
        int? xValue = preChosenX
            ?? await PromptForXAsync(definition, card, ctx, agent, ct);
        if (preChosenX is { } && definition.HasVariableX && card is Card concretePreX)
        {
            concretePreX.SetPendingCastX(preChosenX.Value);
        }

        // CR 601.2c — collect targets in declaration order. For a multi-mode
        // ("choose one or more") spell whose target requests are index-aligned
        // with its modes, only the CHOSEN modes' target slots are prompted —
        // CR 601.2c only chooses targets for modes that were chosen. Unchosen
        // modes keep an empty slot so EffectFactory's per-mode index lookups
        // stay aligned.
        var collectedTargets = await CollectTargetsAsync(
            definition, card, ctx, agent, modeChoice, ct);

        // CR 601.2d — divide-damage announcement. A spell that deals a fixed
        // total of damage "divided as you choose among" its chosen targets
        // (Forked Bolt, Fire, Electrolyze, Arc Lightning, …) announces the
        // division NOW, immediately after targets are chosen (CR 601.2c) and
        // before payment. Prompt the caster's agent for the per-target split,
        // defensively normalise it to a legal division (CR 119.4 — each ≥ 1,
        // sum = printed total), and record it on ChosenSpellParams.DamageDivision
        // so the deal-damage EffectFactory reads the announced amounts instead of
        // an even-split fallback. No-op for every spell that didn't declare a
        // DamageDivisionSpec.
        var damageDivision = await DivideDamageAsync(
            definition, card, collectedTargets, agent, ctx, ct);

        // CR 601.2f — compute the post-reduction total cost (printed cost OR
        // alt cost; + X; − cost reductions; − Delve, Improvise, Convoke
        // reductions). Delve pays its exile portion here per CR 702.66b.
        var totalCost = ComputeAndApplyTotalCost(
            card, caster, ctx, alternativeCost, xValue, delveCost, mergedAdditional);

        // CR 601.2g / CR 605.1 — mana sourcing. Reuse pre-chosen mana when the
        // caller (TurnDriver) already prompted, otherwise prompt the agent.
        var mana = preChosenMana ?? await agent.ChooseManaSourcesAsync(ctx, totalCost, ct);

        // CR 601.2h — pay the non-mana additional costs (discard / sacrifice /
        // exile / pay-life riders, plus any escalate instances) NOW, after
        // target collection (CR 601.2c) and total-cost determination
        // (CR 601.2f). The early pass only pre-checked legality; deferring the
        // actual irreversible payment to here means a targeting failure (which
        // throws inside CollectTargetsAsync above) aborts the cast before any
        // card is discarded / permanent sacrificed (CR 731.1 rewind). This
        // runs BEFORE EffectFactory below because sacrifice-coupled effects
        // (e.g. Eldritch Evolution) read the sacrificed permanent off the paid
        // cost instance eagerly when the factory is invoked.
        PayAdditionalCosts(mergedAdditional, caster);

        var chosen = new ChosenSpellParams(
            mode, xValue, collectedTargets, mana, ctx.AllPlayers,
            ModeIndexes: modeIndexes,
            AdditionalCostPayments: mergedAdditional.Count > 0 ? mergedAdditional : null,
            DamageDivision: damageDivision);
        var effects = definition.EffectFactory(chosen);

        // CR 702.46 — Splice onto Arcane: fold each spliced rider's effects
        // into the spell's effect chain in announcement order.
        effects = ApplySpliceRiders(effects, mergedAdditional, caster);

        // CR 601.2h — pay the mana cost LAST, after targets are chosen
        // (CR 601.2c) and the total cost is determined (CR 601.2f). Callers
        // that enforce mana payment (TurnDriver / GameFacade dispatchers)
        // supply this callback; invoking it HERE — after every step that can
        // make the cast illegal (sorcery-speed gate, unpayable additional
        // costs, insufficient targets) has already thrown — means an illegal
        // cast aborts with NO mana paid and no sources tapped, matching the
        // CR 732.1 rewind ("the entire action is reversed and any payments
        // already made are canceled"). Before this hook, dispatchers paid
        // up front and a targeting failure stranded the payment: the live
        // bot repeatedly tapped lands for casts that never happened. A false
        // return (payment failed) makes the cast illegal too.
        if (payManaCost != null && !payManaCost(totalCost))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name}: mana payment failed (CR 601.2h).");
        }

        // CR 601.2a — for a hand-sourced cast the card is ALREADY on the stack
        // (moved at the top of this method, honouring the strict CR 601.2a "move
        // to the stack first" ordering). For every OTHER origin zone the move is
        // deferred to HERE — after every step that can make the cast illegal has
        // run — because those zones' gates and riders read card.Zone live:
        //   * the source-zone gates — ValidateCastingPermissionAndAltCost checks
        //     the Library-top-cast permission (CR 601.3e) and the alt-cost's
        //     CanCastFor zone restriction (CR 118.9); CostReduction and the
        //     targeting candidate pools also resolve against the origin zone;
        //   * the from-graveyard riders (Delve's CR 702.66b exile, Escape's
        //     CR 702.138a exile) move OTHER graveyard cards while the cast card
        //     is itself still in the graveyard — exiling it early would corrupt
        //     the "N OTHER cards" count.
        // Because nothing observes the card on the stack until Push below, the
        // deferred move is behaviourally identical to the rules-order move
        // (CR 731.1 makes the whole proposal atomic anyway). sourceZoneAtCast was
        // captured at the top, before any move, so the per-source-zone sentinels
        // below still see the true origin.
        if (sourceZoneAtCast != ZoneType.Hand)
        {
            _zoneService.MoveCard(card, card.Zone, ZoneType.Stack, controller: caster);
        }

        // CR 702.34b / 702.33b / 702.115 / 701.59 — append per-feature cleanup
        // effects (alt-cost OnResolved, Kicker/Surge/Gift sentinel-clear).
        // CR 702.33 / 702.32 — a kicked OR multikicked spell needs the same
        // sentinel-clear cleanup + "was kicked" mirror. Multikicker is just a
        // kicker that may be paid any number of times (CR 702.32a); a
        // multikicker paid zero times leaves the spell un-kicked.
        bool hasKickerPayment = mergedAdditional.OfType<KickerAdditionalCost>().Any()
            || mergedAdditional.OfType<MultikickerAdditionalCost>().Any(m => m.Times > 0);
        var finalEffects = AppendCleanupEffects(
            effects, card, caster, alternativeCost, hasKickerPayment, giftRecipient,
            mergedAdditional);

        var spell = new Spells.Spell(card, caster, effects: finalEffects);

        // CR 707.10a — retain the per-slot target requests on the stack object so
        // a "copy this spell" effect (Twincast / Reverberate) can re-prompt the
        // copier for new targets for the copy. Only meaningful for targeted
        // spells; untargeted casts leave it null (the copier reuses nothing).
        if (definition.TargetRequests is { Count: > 0 } reqs)
        {
            spell.RetargetRequests = reqs.ToArray();

            // CR 608.2b — stamp a category-derived legality predicate ONLY when
            // the spell doesn't already carry one, so the resolution recheck in
            // StackResolver stays honest for the many TargetRequests that ship
            // only a free-text Description (no per-card predicate). Derived from
            // the FIRST targeted request whose description maps to a real
            // category; cards that set their own predicate are untouched. This
            // is the single stamp point — the only place a Spell is reachable
            // next to its target requests. The shared
            // TargetCollection.CollectAsync is also used by abilities / triggers
            // which carry no Spell, so it must NOT stamp.
            if (spell.TargetLegalityPredicate == null)
            {
                foreach (var tr in reqs)
                {
                    var pred = Majik.Core.Targeting.TargetCandidateService
                        .BuildLegalityPredicate(tr.Description);
                    if (pred != null)
                    {
                        spell.TargetLegalityPredicate = pred;
                        break;
                    }
                }
            }
        }

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

        // CR 115.6 / 603.6c / 702.21e — once targets are locked (CR 601.2c) the
        // chosen objects "become the target of" this spell. Broadcast that fact
        // so battlefield-attached "becomes the target of a spell or ability an
        // opponent controls" triggers fire on the live cast path — Ward
        // (CR 702.21), Bonecrusher Giant / Goldspan Dragon's "becomes the
        // target" pings, and the rest of the becomes-targeted family. The
        // legacy SpellCaster service published this; the live engine routes
        // every cast through SpellCastFlow, so without this the whole
        // becomes-targeted family was dead on the real engine. Only fire when
        // the spell actually chose at least one target (CR 115.6 — a spell with
        // no targets never "becomes targeted").
        PublishTargetsChosen(spell, collectedTargets);

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

        // CR 601.3 / 608.2 — record this cast for the static spells-per-turn cap
        // counter (Eidolon of Rhetoric / Archon of Emeria: "Each player can't
        // cast more than one spell each turn."). Tracked unconditionally so a
        // cap source entering mid-turn correctly sees prior casts; the
        // battlefield gate + the cap value live on the static rail + the
        // lifecycle binder. Separate ledger from the consumable allowance above.
        Majik.Core.Rules.CastingRestrictions.RecordSpellCast(caster);

        // CR 605/616 / 601.3 — record a NONARTIFACT cast for the per-turn
        // Canonist counter (Ethersworn Canonist: "Each player who has cast a
        // nonartifact spell this turn can't cast additional nonartifact
        // spells."). Tracked unconditionally so a Canonist that enters mid-turn
        // correctly sees who has already cast a nonartifact spell; the
        // battlefield gate lives in ActionValidator + the Canonist lifecycle.
        if (!card.HasType(Cards.Types.CardType.Artifact))
        {
            Majik.Core.Rules.CastingRestrictions.RecordNonartifactSpellCast(caster);
        }

        return spell;
    }

    /// <summary>
    /// CR 115.6 / 603.6c — broadcast a <see cref="TargetsChosenEvent"/> for the
    /// freshly-pushed <paramref name="spell"/> so "becomes the target of a
    /// spell" triggered abilities (Ward — CR 702.21e, Bonecrusher Giant, …)
    /// fire on the live cast path. The raw chosen objects (per-slot, CR 601.2c)
    /// are wrapped into <see cref="ITarget"/>s by runtime kind. No-op when the
    /// spell chose no targets (CR 115.6 — an untargeted spell never "becomes
    /// targeted").
    /// </summary>
    private void PublishTargetsChosen(
        Spells.Spell spell,
        IReadOnlyList<IReadOnlyList<object>> collectedTargets)
    {
        if (collectedTargets is null || collectedTargets.Count == 0) return;

        var targets = new List<Majik.Core.Targeting.ITarget>();
        foreach (var slot in collectedTargets)
        {
            if (slot is null) continue;
            foreach (var obj in slot)
            {
                var wrapped = WrapTarget(obj);
                if (wrapped != null) targets.Add(wrapped);
            }
        }

        if (targets.Count == 0) return;

        _eventBus.Publish(new TargetsChosenEvent(spell, targets));
    }

    /// <summary>
    /// Wrap a raw chosen-target object (CR 601.2c) into the engine's
    /// <see cref="Majik.Core.Targeting.Target"/> abstraction by runtime kind so
    /// the <see cref="TargetsChosenEvent"/> consumers (the becomes-targeted
    /// trigger predicates) can read <c>TargetType</c> + <c>TargetObject</c>.
    /// Returns null for an unrecognised shape (defensive — never block the cast).
    /// </summary>
    private static Majik.Core.Targeting.Target? WrapTarget(object obj) => obj switch
    {
        Permanent perm => Majik.Core.Targeting.Target.Permanent(perm),
        Player player => Majik.Core.Targeting.Target.Player(player),
        Spells.ISpell s => Majik.Core.Targeting.Target.Spell(s),
        ICard card => Majik.Core.Targeting.Target.Card(card),
        _ => null,
    };

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

        // CR 601.3 — SELF-imposed cast-timing restriction baked onto the card
        // ("Cast this spell only before the combat damage step." — Berserk).
        // The predicate is consulted against the live step regardless of the
        // alt-cost path (a self-timing clause restricts EVERY way the card is
        // cast, CR 601.3 — it is part of the card, not the cost). A context with
        // no known step (CurrentPhase null — some test / mulligan harnesses)
        // leaves the gate unenforced, matching the sorcery-speed gate's
        // HasValue guard above.
        if (card is Card cardWithTiming
            && cardWithTiming.CastTimingRestriction is { } timingAllows
            && ctx.CurrentPhase.HasValue
            && !timingAllows(ctx.CurrentPhase.Value))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name}: its cast-timing restriction does not "
                + $"allow casting during the {ctx.CurrentPhase.Value} step (CR 601.3).");
        }

        // CR 601.3e — cast-from-top-of-library authorization. A spell is being
        // cast from the library ONLY because a continuous effect granted that
        // permission (Mystic Forge, Bolas's Citadel, Conspicuous Snoop, the
        // Augur of Autumn Coven clause, Oracle of Mul Daya, …). Verify the live
        // grant authorizes THIS card BEFORE any zone mutation, so an arbitrary
        // library card can never be moved to the stack. This check applies even
        // when an alternative cost is supplied: Bolas's Citadel's pay-life-equal-
        // to-mana-value (CR 118.9) is the cast-from-top cost itself, so a
        // library-zone alt-cost cast must STILL be backed by a registered grant
        // — the alt cost does not grant zone permission on its own. Other
        // alt-cost casts (Suspend / Foretell) move the card from Exile, not the
        // library, so they never reach this branch.
        if (card.Zone == ZoneType.Library
            && !Majik.Core.Rules.LibraryTopPlayPermissions.MayCastTopCard(caster, card))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name}: no effect lets you cast it from the "
                + "top of your library (CR 601.3e).");
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

        // CR 601.2a / 113.6 / 601.3 — from-zone cast-restriction enforcement.
        // This is the cast-from-zone-provenance-stamping seam: every cast path
        // (hand, flashback / jump-start / aftermath from the graveyard,
        // cascade / suspend's last counter / foretell from exile, Bolas's
        // Citadel from the top of the library) funnels through CastAsync, and
        // ValidateCastingPermissionAndAltCost runs BEFORE any zone move — so
        // card.Zone here is the spell's TRUE origin zone (CR 601.2a captures it
        // before "the player moves the spell from where it is to the stack").
        // Reading it live and enforcing the from-zone gates here is exactly the
        // provenance the ActionValidator's CheckCastZoneGates needs: previously
        // the validator's from-zone axis only fired for callers that hand-built
        // a CastSpellAction.FromZone, which no production caster did, so the
        // restriction no-opped on this axis in live games. These three gates
        // mirror ActionValidator.CheckCastZoneGates one-for-one.
        EnforceFromZoneRestrictions(card, caster);
    }

    /// <summary>
    /// CR 601.2a / 113.6 / 601.3 — reject a cast whose live origin zone
    /// (<see cref="ICard.Zone"/>, read before the CR 601.2a move to the stack)
    /// is blocked on the from-zone axis. Mirrors
    /// <see cref="Majik.Core.Rules.ActionValidator"/>'s
    /// <c>CheckCastZoneGates</c>:
    /// <list type="bullet">
    /// <item>card-baked restricted zones — CR 601.2a / 117.6 (Hogaak);</item>
    /// <item>cast-from-hand-only player restriction — CR 113.6 / 601.3
    ///       (Drannith Magistrate);</item>
    /// <item>global cast-from-zone block — CR 601.3 (Grafdigger's Cage).</item>
    /// </list>
    /// Throws so the CR 731.1 rewind in <see cref="CastAsync"/> returns the
    /// card unchanged (the throw happens before any zone move).
    /// </summary>
    private static void EnforceFromZoneRestrictions(ICard card, Player caster)
    {
        var fromZone = card.Zone;

        // CR 601.2a / 117.6 — card-baked restricted zones (Hogaak).
        if (card is Card concreteCard
            && concreteCard.RestrictedCastZones.Contains(fromZone))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name}: it can't be cast from {fromZone} (CR 601.2a).");
        }

        // CR 113.6 / 601.3 — cast-from-hand-only player restriction
        // (Drannith Magistrate: opponents can only cast from their hands).
        if (fromZone != ZoneType.Hand
            && Majik.Core.Rules.CastingRestrictions.MustCastFromHand(caster))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name}: {caster.Name} can't cast spells from {fromZone} "
                + "(CR 113.6 — cast-from-hand-only restriction).");
        }

        // CR 601.3 — global cast-from-zone block (Grafdigger's Cage).
        if (Majik.Core.Rules.CastingRestrictions.IsCastFromZoneGloballyBlocked(fromZone))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name}: players can't cast spells from {fromZone} "
                + "(CR 601.3 — global cast-from-zone block).");
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

    /// <summary>CR 601.2f / CR 601.2g — merge caller-supplied costs with
    /// definition-supplied costs and pre-check legality (no partial payment).
    /// Does NOT pay — the actual non-mana payment is deferred to the CR 601.2h
    /// point (after target collection, CR 601.2c) via
    /// <see cref="PayAdditionalCosts"/>, so a targeting failure refunds nothing
    /// already paid because nothing has been paid yet (CR 731.1 rewind).</summary>
    private static List<IAdditionalCost> BuildAndPrecheckAdditionalCosts(
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

        return mergedAdditional;
    }

    /// <summary>
    /// CR 702.51 — cast-time Convoke prompt. If <paramref name="card"/> carries
    /// a <see cref="KeywordAbility"/>("Convoke") marker AND no
    /// <see cref="ConvokeAdditionalCost"/> is already in
    /// <paramref name="mergedAdditional"/> (the bot probe / test pre-selection
    /// path supplies one), prompt the caster's agent — via the declarative
    /// <see cref="IPlayerAgent.ChooseAsync"/> sink, a <see cref="ChoiceKind.PickN"/>
    /// over the caster's untapped creatures — for the creatures to tap. The
    /// resulting <see cref="ConvokeAdditionalCost"/> is appended to
    /// <paramref name="mergedAdditional"/> so the downstream CR 601.2h payment
    /// loop taps the creatures (CR 702.51a) and
    /// <see cref="ComputeAndApplyTotalCost"/> applies the pip reduction
    /// (CR 702.51b). Declining (empty pick) is legal — convoke is optional
    /// (CR 702.51 — "can help"); the spell is then cast at its printed cost.
    /// No-op for any spell without the Convoke marker.
    /// </summary>
    private static async Task PromptForConvokeAsync(
        ICard card,
        Player caster,
        GameContext ctx,
        IPlayerAgent agent,
        List<IAdditionalCost> mergedAdditional,
        CancellationToken ct)
    {
        // Only convoke-marked spells, and only when the caller hasn't already
        // baked the selection (probe path) — avoid double-tapping.
        if (!HasConvokeMarker(card)) return;
        if (mergedAdditional.OfType<ConvokeAdditionalCost>().Any()) return;

        var pool = ConvokeAdditionalCost.AvailableCreatures(caster);
        if (pool.Count == 0) return;

        // CR 702.51 — optional PickN over the caster's untapped creatures.
        // Min 0 (decline allowed), Max = whole pool.
        var request = new ChoiceRequest(
            ChoiceKind.PickN,
            $"Convoke {card.Name} — choose creatures to tap (CR 702.51)",
            Min: 0,
            Max: pool.Count,
            Candidates: pool.Cast<object>().ToList(),
            Intent: BotIntent.None,
            Optional: true);

        var chosenRaw = await agent.ChooseAsync(ctx, request, ct).ConfigureAwait(false);
        var chosen = chosenRaw.OfType<Creature>().Distinct().ToList();
        if (chosen.Count == 0) return;

        mergedAdditional.Add(new ConvokeAdditionalCost(card, chosen));
    }

    /// <summary>CR 702.51 helper — does <paramref name="card"/> carry a
    /// <see cref="KeywordAbility"/>("Convoke") marker? Mirrors
    /// <see cref="HasUncounterableMarker"/> and the bot probe's detector
    /// (<see cref="Players.Agents.ConvokeAltCostProbe.DefaultIsConvokeCard"/>).
    /// Case-insensitive.</summary>
    private static bool HasConvokeMarker(ICard card) =>
        card.Abilities
            .OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Convoke", StringComparison.OrdinalIgnoreCase));

    /// <summary>CR 601.2h — pay every accumulated non-mana additional cost
    /// (discard / sacrifice / exile / pay-life riders, plus any escalate
    /// instances appended during mode choice). Invoked LAST, after target
    /// collection (CR 601.2c) and immediately before the mana payment, so an
    /// illegal cast (insufficient targets, sorcery-speed violation) throws
    /// before any of these irreversible payments is made (CR 731.1 — the
    /// whole action is reversed and any payments already made are canceled).
    /// Re-checks affordability defensively (CR 601.2g — no partial payment):
    /// the early pre-check already ran, but state may have shifted (e.g. an
    /// escalate discard consuming the last card a later sacrifice needed).</summary>
    private static void PayAdditionalCosts(
        IReadOnlyList<IAdditionalCost> mergedAdditional, Player caster)
    {
        foreach (var addCost in mergedAdditional)
        {
            if (!addCost.CanPay(caster))
            {
                throw new InvalidOperationException(
                    $"Cannot pay additional cost: {addCost.Description}");
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

    /// <summary>CR 700.2 / CR 700.2d — modal-spell mode prompt. Returns the
    /// chosen mode index set (one entry for "Choose one", N entries for
    /// "Choose one or more" / "Choose two"). Empty when the spell is not
    /// modal. Single-mode spells route through the scalar
    /// <see cref="IPlayerAgent.ChooseModeAsync"/> so existing agents /
    /// scripts keep their behaviour; multi-mode spells route through
    /// <see cref="IPlayerAgent.ChooseModesAsync"/>.</summary>
    private static async Task<IReadOnlyList<int>> PromptForModesAsync(
        SpellDefinition definition, GameContext ctx, IPlayerAgent agent, CancellationToken ct)
    {
        if (definition.Modes.Count == 0) return Array.Empty<int>();

        if (!definition.IsMultiMode)
        {
            var single = await agent.ChooseModeAsync(
                ctx, definition.Modes, definition.ModeIntents, ct);
            return new[] { single };
        }

        return await agent.ChooseModesAsync(
            ctx, definition.Modes, definition.MinModes, definition.MaxModes,
            definition.ModeIntents, ct);
    }

    /// <summary>CR 702.121 / CR 601.2f / CR 601.2g — build the escalate
    /// additional cost <paramref name="extraModes"/> times (one per mode
    /// chosen beyond the first) and pre-check affordability. Each instance is
    /// a fresh cost from <see cref="EscalateSpec.BuildPerModeCost"/>; the whole
    /// bill is affordability-checked up front so a shortfall can't leave a
    /// half-paid escalate. The built instances are appended to
    /// <paramref name="mergedAdditional"/> so downstream effect / cleanup
    /// machinery can read them AND so the deferred CR 601.2h payment pass
    /// (<see cref="PayAdditionalCosts"/>) pays them alongside the other
    /// additional costs — after target collection, so a targeting failure
    /// aborts before any escalate discard / pay-life is committed. Throws
    /// <see cref="InvalidOperationException"/> when the total escalate cost
    /// can't be paid — making the cast illegal.</summary>
    private static void BuildAndPrecheckEscalateCosts(
        EscalateSpec escalate,
        ICard card,
        Player caster,
        int extraModes,
        List<IAdditionalCost> mergedAdditional)
    {
        if (extraModes <= 0) return;

        // CR 601.2g — confirm the WHOLE escalate bill is affordable BEFORE the
        // cast proceeds, so a shortfall can't leave a half-paid escalate (one
        // card discarded then the cast aborts). The aggregate probe counts the
        // depletable resource (hand size for discard, life for pay-life); when
        // no probe is supplied this is permissive and the per-cost CanPay guard
        // in PayAdditionalCosts still catches a mid-sequence shortfall.
        if (!escalate.CanPayExtraModes(caster, extraModes))
        {
            throw new InvalidOperationException(
                $"Cannot pay Escalate cost ({escalate.Description}) for {card.Name}: " +
                $"insufficient resources for {extraModes} extra-mode payment(s).");
        }

        for (var i = 0; i < extraModes; i++)
        {
            var cost = escalate.BuildPerModeCost(card);
            if (!cost.CanPay(caster))
            {
                throw new InvalidOperationException(
                    $"Cannot pay Escalate cost ({escalate.Description}) for {card.Name}: " +
                    $"insufficient resources for {extraModes} extra-mode payment(s).");
            }
            mergedAdditional.Add(cost);
        }
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
    /// can't pick enough legal targets (cast is illegal).
    /// <para>
    /// CR 700.2d — for a MODAL spell (single- or multi-mode) whose target
    /// requests are index-aligned with its modes (one request per printed
    /// mode), only the CHOSEN modes are prompted for targets; unchosen modes
    /// keep an empty slot so per-mode index lookups in the EffectFactory stay
    /// aligned. When the request/mode counts don't line up (e.g. Cryptic
    /// Command, whose two requests cover only the first two of four modes) the
    /// whole request list is collected, preserving the legacy behaviour.
    /// </para>
    /// <para>
    /// CR 601.2c — a chosen mode's request has its minimum raised to its
    /// PRINTED minimum (<see cref="Targeting.TargetRequest.AsChosenMode"/>):
    /// the per-mode requests carry <c>MinTargets = 0</c> so an UNCHOSEN mode
    /// never gates the cast, but once a targeted mode IS chosen it demands its
    /// printed minimum (typically 1) — so an escalate-paid (or single-mode)
    /// targeted mode with no legal target makes the whole cast illegal and
    /// rewinds, instead of silently no-opping on resolution.
    /// </para></summary>
    /// <summary>
    /// CR 601.2d / CR 119.4 — prompt the caster for the damage division of a
    /// "deals N damage divided as you choose among …" spell, immediately after
    /// targets are chosen (CR 601.2c). Returns null when the spell declares no
    /// <see cref="DamageDivisionSpec"/> or the divided target slot is empty
    /// (nothing to divide among) — in which case the EffectFactory falls back to
    /// its own default. Otherwise returns one <see cref="DamageAllocation"/> per
    /// chosen target, defensively normalised to a legal division (each ≥ 1,
    /// summing to the printed total).
    /// </summary>
    private static async Task<IReadOnlyList<DamageAllocation>?> DivideDamageAsync(
        SpellDefinition definition,
        ICard card,
        IReadOnlyList<IReadOnlyList<object>> collectedTargets,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct)
    {
        if (definition.DamageDivision is not { } spec) return null;

        var slot = spec.TargetSlotIndex;
        if (slot < 0 || slot >= collectedTargets.Count) return null;

        var targets = collectedTargets[slot];
        if (targets.Count == 0) return null;

        // CR 601.2d / CR 119.4 — shared with the triggered/activated dispatch
        // seam (TriggerManager) so the cast-time and trigger-time announcement
        // points never diverge.
        return await Players.Agents.DamageDivisionDefaults.PromptAsync(
            agent, ctx, card, spec.TotalDamage, targets, ct).ConfigureAwait(false);
    }

    private static async Task<List<IReadOnlyList<object>>> CollectTargetsAsync(
        SpellDefinition definition, ICard card, GameContext ctx, IPlayerAgent agent,
        IReadOnlyList<int> chosenModes, CancellationToken ct)
    {
        // CR 700.2d / CR 601.2c — mode-aware target collection for SPARSE
        // modal spells: the targeted modes are a SUBSET of the printed modes
        // and each targeting request carries an explicit ModeIndex tying it to
        // its printed mode (e.g. Cryptic Command — four modes, only modes 0 and
        // 1 are targeted). Collect a target ONLY for a chosen targeted mode,
        // raising its minimum to the printed minimum so a chosen targeted mode
        // with no legal target makes the whole cast illegal and rewinds, rather
        // than silently no-opping on resolution. The returned slots are keyed by
        // ModeIndex (sized to cover the highest mode index) so the EffectFactory's
        // per-mode Targets[ModeIndex] lookups stay aligned. This generalizes the
        // escalate/Charm rewind (CR 601.2c) to the index-misaligned Cryptic /
        // Command family.
        if (definition.Modes.Count > 0
            && definition.TargetRequests.Count > 0
            && definition.TargetRequests.All(r => r.ModeIndex.HasValue)
            && chosenModes.Count > 0)
        {
            var chosenModeSet = new HashSet<int>(chosenModes);
            var slotCount = definition.Modes.Count;
            var perModeSparse = new IReadOnlyList<object>[slotCount];
            for (var s = 0; s < slotCount; s++)
            {
                perModeSparse[s] = Array.Empty<object>();
            }

            foreach (var request in definition.TargetRequests)
            {
                var modeIdx = request.ModeIndex!.Value;
                if (modeIdx < 0 || modeIdx >= slotCount) continue;
                if (!chosenModeSet.Contains(modeIdx)) continue;

                // CR 601.2c — a CHOSEN targeted mode demands its printed
                // minimum; an agent that can't supply it makes the whole cast
                // illegal (throwOnInsufficient rewinds the cast).
                var oneSlot = await Targeting.TargetCollection.CollectAsync(
                    new[] { request.AsChosenMode() },
                    card, ctx, agent, throwOnInsufficient: true, ct);
                perModeSparse[modeIdx] = oneSlot.Count > 0 ? oneSlot[0] : Array.Empty<object>();
            }

            var sparseList = new List<IReadOnlyList<object>>(perModeSparse);
            if (card is Card concreteSparse)
            {
                concreteSparse.SetPendingCastTargets(sparseList);
            }
            return sparseList;
        }

        // CR 700.2d — mode-aware target collection for index-aligned modal
        // spells (single- OR multi-mode). Prompt only the chosen modes' slots;
        // fill the rest empty. Single-mode "Choose one" charms route here too
        // (chosenModes carries the single scalar pick), so a chosen targeted
        // charm mode with no legal target is illegal per CR 601.2c.
        if (definition.Modes.Count > 0
            && definition.TargetRequests.Count == definition.Modes.Count
            && chosenModes.Count > 0)
        {
            var chosenSet = new HashSet<int>(chosenModes);
            var perMode = new List<IReadOnlyList<object>>(definition.TargetRequests.Count);
            for (var i = 0; i < definition.TargetRequests.Count; i++)
            {
                if (!chosenSet.Contains(i))
                {
                    perMode.Add(Array.Empty<object>());
                    continue;
                }
                // CR 601.2c — a CHOSEN mode demands its printed minimum; an
                // agent that can't supply it makes the whole cast illegal.
                var oneSlot = await Targeting.TargetCollection.CollectAsync(
                    new[] { definition.TargetRequests[i].AsChosenMode() },
                    card, ctx, agent, throwOnInsufficient: true, ct);
                perMode.Add(oneSlot.Count > 0 ? oneSlot[0] : Array.Empty<object>());
            }

            if (card is Card concreteMulti && perMode.Count > 0)
            {
                concreteMulti.SetPendingCastTargets(perMode);
            }
            return perMode;
        }

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
        Player? giftRecipient,
        IReadOnlyList<IAdditionalCost> mergedAdditional)
    {
        IReadOnlyList<IEffect> finalEffects = effects;
        if (alternativeCost != null)
        {
            finalEffects = finalEffects.Append(new Effect(
                $"{alternativeCost.Description} cleanup",
                () => alternativeCost.OnResolved(card, caster))).ToList();
        }

        // CR 702.27c — Buyback. If the optional buyback additional cost was
        // paid, the spell returns to its owner's hand instead of the graveyard
        // as it resolves. Appended as the LAST printed-body effect so the
        // spell's own body resolves first, then the return-to-hand fires before
        // the engine's default stack → graveyard disposition.
        var buyback = mergedAdditional.OfType<BuybackAdditionalCost>().FirstOrDefault();
        if (buyback != null)
        {
            finalEffects = finalEffects.Append(new Effect(
                "Buyback — return spell to hand on resolve (CR 702.27c)",
                () => buyback.ReturnOnResolve(caster))).ToList();
        }

        // CR 702.169 / CR 400.7 — Bargain sentinel clear (mirror of Kicker's).
        if (mergedAdditional.OfType<BargainAdditionalCost>().Any())
        {
            finalEffects = finalEffects.Append(new Effect(
                "Bargain cleanup — clear Card.WasBargained",
                () =>
                {
                    if (card is Card concreteForBargain)
                    {
                        concreteForBargain.ClearWasBargained();
                    }
                })).ToList();
        }
        // CR 702.33b / 702.32c / 400.7 — clear the kicker / multikicker
        // sentinel after the spell resolves. For a PERMANENT spell the clear
        // is deferred to ZoneService's Stack → Battlefield move (it runs AFTER
        // any CR 614.1d "enters with a counter for each time it was kicked"
        // replacement has read the count — Everflowing Chalice), so we only
        // append the resolution-effect clear for non-permanents (instants /
        // sorceries), which never reach the battlefield-entry clear.
        if (hasKickerPayment && card is not Permanent)
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

        // CR 118.10 — total amount of mana spent to cast this spell (the mana
        // value of the resolved totalCost). WasFreeCast is exactly this being
        // zero. Stamp the magnitude on the spell handle AND mirror it onto the
        // card so "if {N} or more mana was spent to cast it" payoffs (Prompto
        // Argentum / Blazing Bomb / the Opus family) can read it off either
        // the watched SpellCastEvent's spell or a battlefield-resident card.
        var totalManaSpent = totalCost.TotalValue;
        spell.TotalManaSpentThisCast = totalManaSpent;
        if (card is Card concreteForTotalSpent)
        {
            concreteForTotalSpent.SetTotalManaSpentThisCast(totalManaSpent);
        }

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

        // CR 702.33b / 702.32c — kicked posture mirror onto the resolving
        // spell. The kick count was already stamped on the underlying card by
        // the (Multi)Kicker cost's Pay; surface it on the spell handle too so a
        // scaling resolution (Everflowing Chalice) can read either reference.
        if (hasKickerPayment)
        {
            spell.WasKicked = true;
            if (card is Card concreteForKickCount && concreteForKickCount.TimesKicked > 0)
            {
                spell.TimesKicked = concreteForKickCount.TimesKicked;
            }
            else
            {
                // Plain Kicker (KickerAdditionalCost) stamps only the boolean;
                // a single kick = 1 (CR 702.33 — kicked at most once).
                spell.TimesKicked = 1;
            }
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

        // CR 701.5b / 106.4 — pay-time uncounterable rider (Boseiju, Who
        // Shelters All: "If that mana is spent on an instant or sorcery spell,
        // that spell can't be countered."). The mana ability's provenance
        // reaction stamped PendingCastUncounterable on the card when one of its
        // {C} units paid a pip on this (instant/sorcery) spell during the
        // CR 601.2h mana payment, which ran just before this spell object was
        // constructed. Copy the stamp onto the spell, then clear it so a later
        // non-cast battlefield entry (blink, copy) never reuses it.
        if (!spell.CannotBeCountered
            && card is Card concreteForUncounterable
            && concreteForUncounterable.PendingCastUncounterable)
        {
            spell.CannotBeCountered = true;
        }
        if (card is Card concreteToClearUncounterable)
        {
            concreteToClearUncounterable.ClearPendingCastUncounterable();
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

        // CR 701.5b — controller-scoped "spells you control can't be countered"
        // static (Destiny Spinner: creature + enchantment spells; the wider
        // can't-be-countered cluster). Scan the caster's battlefield for a live
        // UncounterableControllerStatic marker whose covered type set includes
        // one of this spell's card types. Battlefield gating is enforced here
        // (the marker only counts while its source permanent is on the caster's
        // battlefield, CR 603-style), so an LTB'd source contributes nothing.
        if (!spell.CannotBeCountered
            && CasterControlsUncounterableStaticFor(caster, card))
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

        // CR 601.2 / CR 113.5 — strict "cast from a graveyard" sentinel for
        // graveyard-cast punisher triggers (Ash Zealot's "whenever a player
        // casts a spell from a graveyard"). Flashback / Escape / Disturb and
        // any "you may cast this from your graveyard" permission all move the
        // card Graveyard → Stack, so this single source-zone check covers the
        // whole family. Read off the live spell at cast time (see
        // SpellCastFlow's publish of SpellCastEvent), so no Card mirror is
        // needed — unlike the hand/library sentinels there is no
        // battlefield-side ETB consumer.
        if (sourceZoneAtCast == ZoneType.Graveyard)
        {
            spell.WasCastFromGraveyard = true;
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

    /// <summary>
    /// CR 701.5b helper — does <paramref name="caster"/> control a live
    /// <see cref="UncounterableControllerStatic"/> marker that covers one of
    /// the cast card's types? The marker is battlefield-gated: only permanents
    /// currently in the caster's Battlefield zone contribute, so a source that
    /// has since left the battlefield grants nothing. Mirrors
    /// <see cref="HasUncounterableMarker"/> (per-card self marker) but scans the
    /// controller's board rather than the spell itself, which is what makes
    /// "spells you control can't be countered" controller-scoped.
    /// </summary>
    private static bool CasterControlsUncounterableStaticFor(Player caster, ICard card)
    {
        var spellTypes = card.CardTypes;
        foreach (var permanent in caster.Zones.Battlefield.GetCards())
        {
            foreach (var marker in permanent.Abilities.OfType<UncounterableControllerStatic>())
            {
                if (marker.Covers(spellTypes))
                {
                    return true;
                }
            }
        }
        return false;
    }
}

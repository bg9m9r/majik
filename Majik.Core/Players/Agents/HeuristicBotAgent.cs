using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Phase 27 heuristic bot. Smarter than <see cref="DeterministicBotAgent"/>:
///
///   - Priority: if a land is in hand and a land drop is legal, plays it;
///     otherwise passes. (Spell-casting decision deferred — needs cost
///     evaluator + target selection, see remaining Phase 15.)
///   - Combat (attack): declares every non-sick untapped creature as an
///     attacker, swinging at the defender.
///   - Combat (block): for each attacker, blocks with the smallest creature
///     whose toughness strictly exceeds the attacker's power (a "safe"
///     block that doesn't lose the blocker). If no safe blocker exists,
///     doesn't block that attacker.
///
/// Everything else delegates to the default no-op behaviour from
/// <see cref="DeterministicBotAgent"/>.
/// </summary>
public sealed class HeuristicBotAgent : IPlayerAgent
{
    // Cards the bot proposed to cast but that didn't actually leave hand
    // (no SpellDef match, target-fill failed, etc.). Cleared each turn —
    // if the bot picks something up next turn it might be castable then.
    private readonly HashSet<Guid> _failedThisTurn = new();
    private int _failedTurnNumber = -1;
    private Guid? _lastProposed;

    // Activated abilities the bot has already fired this turn. Prevents
    // infinite-activation loops in the priority pump.
    private readonly HashSet<Guid> _abilityFiredThisTurn = new();
    private Guid? _lastAbilityProposed;

    /// <summary>
    /// Optional probe that surfaces alternative-cost candidates per card
    /// (CR 118.9 — flashback, spectacle, evoke, pitch). When null, the bot
    /// only casts spells for their printed cost.
    /// </summary>
    private readonly IAlternativeCostProbe? _altCostProbe;

    /// <summary>
    /// Optional card-data lookup used to read per-card
    /// <see cref="BotIntent"/> for mana-hold + sequencing decisions. When
    /// null, the bot falls back to today's heuristics (every instant
    /// counts as reactive, no intent bias in priority sequencing).
    /// </summary>
    private readonly Majik.Core.CardData.ICardRepository? _cardRepository;

    /// <summary>
    /// Optional vanilla-shell tracker. When non-null, the bot calls
    /// <see cref="Majik.Core.Diagnostics.VanillaShellTracker.Notice"/> on
    /// every castable-spell enumeration touching a vanilla shell — once-
    /// per-game per name, the tracker emits a WARN + an
    /// <see cref="Majik.Core.Events.UnimplementedCardEncounteredEvent"/>.
    /// Cast-bid priority is also pushed below every implemented bid so
    /// the bot only proposes a vanilla shell when nothing else is in
    /// hand to cast.
    /// </summary>
    private readonly Majik.Core.Diagnostics.VanillaShellTracker? _vanillaTracker;

    public HeuristicBotAgent(
        IAlternativeCostProbe? altCostProbe = null,
        Majik.Core.CardData.ICardRepository? cardRepository = null,
        Majik.Core.Diagnostics.VanillaShellTracker? vanillaTracker = null)
    {
        _altCostProbe = altCostProbe;
        _cardRepository = cardRepository;
        _vanillaTracker = vanillaTracker;
    }

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
    {
        UpdateFailureMemos(ctx);

        // Sorcery window: own main phase, empty stack. Instant window: any
        // other priority opportunity worth reacting to. Outside both → pass.
        var sorceryWindow = ctx.CurrentPhase is { } p && p.IsMain()
            && ReferenceEquals(ctx.Self, ctx.ActivePlayer)
            && ctx.Stack.IsEmpty;
        var instantWindow = !sorceryWindow && IsReactiveWindow(ctx);

        if (!sorceryWindow && !instantWindow) return Task.FromResult(PriorityAction.Pass);

        // 1. Land drop (sorcery-speed only) — CR 305.2 cap is enforced by
        //    PriorityLoop's LandDropTracker; we rely on the failed-proposal
        //    memo to avoid spinning on a rejected land.
        if (sorceryWindow && PickLandDrop(ctx) is { } landDrop)
        {
            return Task.FromResult<PriorityAction>(landDrop);
        }

        // 2. Highest-priority affordable spell across hand+graveyard.
        if (PickHighestPriorityCast(ctx, sorceryWindow) is { } castAction)
        {
            return Task.FromResult<PriorityAction>(castAction);
        }

        // 3. CR 602 activated-ability hook.
        var fired = PickActivatedAbility(ctx);
        if (fired != null)
        {
            _lastAbilityProposed = fired.Id;
            return Task.FromResult<PriorityAction>(
                new PriorityAction.ActivateAbility(fired, Array.Empty<object>()));
        }

        return Task.FromResult(PriorityAction.Pass);
    }

    /// <summary>Maintain the per-turn failed-proposal + fired-ability memos.
    /// Reset on a fresh turn; treat any prior proposal whose card is still
    /// in hand as "dispatcher rejected" so we don't re-offer it.</summary>
    private void UpdateFailureMemos(GameContext ctx)
    {
        if (ctx.TurnNumber != _failedTurnNumber)
        {
            _failedThisTurn.Clear();
            _abilityFiredThisTurn.Clear();
            _failedTurnNumber = ctx.TurnNumber;
            _lastProposed = null;
            _lastAbilityProposed = null;
        }
        if (_lastAbilityProposed is Guid prevAbil)
        {
            _abilityFiredThisTurn.Add(prevAbil);
        }
        _lastAbilityProposed = null;
        if (_lastProposed is Guid prev
            && ctx.Self.Zones.Hand.GetCards().Any(c => c.InstanceId == prev))
        {
            _failedThisTurn.Add(prev);
        }
        _lastProposed = null;
    }

    /// <summary>Pick the first land in hand that hasn't been failed this
    /// turn, recording it as the last proposal so a dispatcher rejection
    /// can be detected on the next call.</summary>
    private PriorityAction.PlayLand? PickLandDrop(GameContext ctx)
    {
        // CR 305.2 — once the land drop is spent (or otherwise unavailable),
        // don't propose ANY land. Without this the bot cycled through every
        // remaining land in hand each turn, each rejected by the loop — a
        // flood of "rejected PlayLand" lines over a long game. LandPlayAvailable
        // is the engine's live LandDropTracker truth (handles extra-drop caps).
        if (!ctx.LandPlayAvailable) return null;
        var land = ctx.Self.Zones.Hand.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Land)
                && !_failedThisTurn.Contains(c.InstanceId));
        if (land == null) return null;
        _lastProposed = land.InstanceId;
        return new PriorityAction.PlayLand(land);
    }

    /// <summary>Score every castable spell across hand + graveyard, pick
    /// the highest-priority bid, and return the corresponding CastSpell
    /// action. Returns null when no spell is affordable.</summary>
    private PriorityAction.CastSpell? PickHighestPriorityCast(GameContext ctx, bool sorceryWindow)
    {
        var hand = ctx.Self.Zones.Hand.GetCards();
        var graveyard = ctx.Self.Zones.Graveyard.GetCards();
        var pool = hand.Concat(graveyard)
            .Where(c => !c.HasType(CardType.Land))
            .Where(IsCastableSpell)
            .Where(c => sorceryWindow || IsInstantSpeed(c))
            .Where(c => !_failedThisTurn.Contains(c.InstanceId))
            .ToList();

        var bids = new List<(ICard Card, ManaCost Cost, IAlternativeCost? Alt, int Priority)>();
        foreach (var card in pool)
        {
            CollectCastBidsForCard(ctx, sorceryWindow, card, bids);
        }

        var best = bids.OrderByDescending(b => b.Priority).FirstOrDefault();
        if (best.Card == null) return null;
        _lastProposed = best.Card.InstanceId;
        return new PriorityAction.CastSpell(
            best.Card,
            Array.Empty<object>(),
            AlternativeCost: best.Alt);
    }

    /// <summary>Enumerate printed- and alt-cost bids for a single card,
    /// appending each to <paramref name="bids"/>. Encapsulates the vanilla-
    /// shell penalty, mana-hold reserve, and graveyard-requires-alt-cost
    /// rules so <see cref="PickHighestPriorityCast"/> remains a flat
    /// "iterate + argmax" loop.</summary>
    private void CollectCastBidsForCard(
        GameContext ctx,
        bool sorceryWindow,
        ICard card,
        List<(ICard Card, ManaCost Cost, IAlternativeCost? Alt, int Priority)> bids)
    {
        // Vanilla-shell graceful degrade: notice + apply a -100 penalty so
        // the bot only picks an unimplemented card when no implemented
        // alternative bid wins.
        if (card.IsVanillaShell)
        {
            _vanillaTracker?.Notice(card, ctx.Self, "castable-spell enumeration");
        }
        var vanillaPenalty = card.IsVanillaShell ? -100 : 0;

        // CR 117.7 / 601.2f — bot affordability mirrors the real cast
        // pipeline (includes SpellCostIncreaseAbility riders from each
        // player's battlefield).
        var printedCost = Majik.Core.Costs.CostReduction.GetEffectiveCost(card, ctx.Self, ctx.AllPlayers);
        var inHand = card.Zone == ZoneType.Hand;

        var altBids = EnumerateAlternativeCostBids(ctx, card);

        if (inHand)
        {
            AppendInHandBids(ctx, sorceryWindow, card, printedCost, altBids, vanillaPenalty, bids);
        }
        else
        {
            // Graveyard / off-hand zone — alt cost is the ONLY legal path.
            var cheapestAlt = altBids
                .OrderBy(b => b.Cost.TotalValue)
                .Cast<(ManaCost Cost, IAlternativeCost Alt)?>()
                .FirstOrDefault();
            if (cheapestAlt is { } chosen)
            {
                bids.Add((card, chosen.Cost, chosen.Alt, printedCost.TotalValue + vanillaPenalty));
            }
        }
    }

    /// <summary>Walk the alt-cost probe for a card, filtering to those that
    /// pass CanCastFor and whose mana cost is actually payable by the
    /// current pool.</summary>
    private List<(ManaCost Cost, IAlternativeCost Alt)> EnumerateAlternativeCostBids(
        GameContext ctx, ICard card)
    {
        var altBids = new List<(ManaCost Cost, IAlternativeCost Alt)>();
        if (_altCostProbe == null) return altBids;
        foreach (var alt in _altCostProbe.CandidatesFor(card, ctx.Self, ctx))
        {
            if (!alt.CanCastFor(card, ctx.Self)) continue;
            if (TryPickManaSources(ctx.Self, alt.AlternativeManaCost) == null) continue;
            altBids.Add((alt.AlternativeManaCost, alt));
        }
        return altBids;
    }

    /// <summary>Append in-hand bids for a single card: printed-cost bid
    /// (affordable + sequencing bonus + mana-hold reserve), and the
    /// cheapest strictly-cheaper alt-cost bid. Bid priority uses the
    /// printed cost so a $1 alt on {2}{R} still ranks as a {2}{R} bid.
    /// </summary>
    private void AppendInHandBids(
        GameContext ctx,
        bool sorceryWindow,
        ICard card,
        ManaCost printedCost,
        List<(ManaCost Cost, IAlternativeCost Alt)> altBids,
        int vanillaPenalty,
        List<(ICard Card, ManaCost Cost, IAlternativeCost? Alt, int Priority)> bids)
    {
        // Mana-hold reserve: during sorcery windows, hold back the cheapest
        // reactive instant's worth of mana when opp has potential threats.
        // Skipped when this bid IS the reactive card (we're casting it).
        var reserve = sorceryWindow && !IsInstantSpeed(card)
            ? ManaHoldReserve(ctx) : 0;
        if (CanAffordWithReserve(ctx.Self, printedCost, reserve))
        {
            var intent = _cardRepository?.IntentFor(card.Name) ?? BotIntent.None;
            bids.Add((card, printedCost, null, printedCost.TotalValue
                + SequencingBonus(card, ctx, sorceryWindow, intent)
                + vanillaPenalty));
        }
        // Alt-cost bids: cheapest alt strictly cheaper than printed cost
        // (alts usually have a downside — pitch a card, etc.).
        var cheapestAlt = altBids
            .Where(b => b.Cost.TotalValue < printedCost.TotalValue)
            .OrderBy(b => b.Cost.TotalValue)
            .Cast<(ManaCost Cost, IAlternativeCost Alt)?>()
            .FirstOrDefault();
        if (cheapestAlt is { } chosen)
        {
            bids.Add((card, chosen.Cost, chosen.Alt, printedCost.TotalValue + vanillaPenalty));
        }
    }

    /// <summary>Mana to reserve for reactive instants during a sorcery
    /// window. Returns the cheapest instant's CMC when we have one in hand
    /// AND opp has an untapped creature that could attack next turn. Zero
    /// otherwise — when opp has no offense or we have no responsive cards
    /// to hold up, all mana is free for sorcery-speed play.</summary>
    /// <summary>Test-only entry point. Use the public priority loop in
    /// production — direct invocation is only needed to assert intent-aware
    /// hold logic in isolation.</summary>
    internal int ManaHoldReserveForTests(GameContext ctx) => ManaHoldReserve(ctx);

    private int ManaHoldReserve(GameContext ctx)
    {
        // Reactive intent classes — instants worth holding mana for during
        // a sorcery window. Cantrip / Draw / Ramp instants don't need a
        // reservation (we'd rather spend the mana now).
        const BotIntent reactiveMask =
            BotIntent.Burn | BotIntent.Removal | BotIntent.Counter
            | BotIntent.CombatTrick | BotIntent.Protection | BotIntent.Bounce;

        var instants = ctx.Self.Zones.Hand.GetCards()
            .Where(c => c.HasType(CardType.Instant))
            .Where(c => !_failedThisTurn.Contains(c.InstanceId))
            .Where(c =>
                _cardRepository == null
                || _cardRepository.IntentFor(c.Name).HasAny(reactiveMask))
            .OrderBy(c => Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost ?? "").TotalValue)
            .ToList();
        if (instants.Count == 0) return 0;

        var opp = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, ctx.Self));
        if (opp == null) return 0;
        var oppHasOffense = opp.Zones.Battlefield.GetCards()
            .OfType<Creature>().Any(c => !c.IsTapped);
        if (!oppHasOffense) return 0;

        return Majik.Core.ValueObjects.ManaCost.Parse(instants[0].ManaCost ?? "").TotalValue;
    }

    /// <summary>True iff the supplied cost can be paid AND we'd still have
    /// <paramref name="reserve"/> untapped sources left over. Reserve is
    /// generic-only for the simple model (refinements can extend to
    /// color-specific reserves).</summary>
    private static bool CanAffordWithReserve(Player self, Majik.Core.ValueObjects.ManaCost cost, int reserve)
    {
        if (reserve <= 0) return TryPickManaSources(self, cost) != null;
        var untapped = self.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => !p.IsTapped)
            // CR 305.6 — route through EffectiveManaAbilities for parity
            // with the mana-payment side. Null layers here ⇒ printed-
            // abilities path (bot has no path to ContinuousEffectsService
            // via GameContext yet); identical behaviour to the prior
            // .OfType<IManaAbility>() enumeration.
            .Where(p => Majik.Core.Effects.EffectiveManaAbilities.For(p, layers: null).Count > 0)
            .Count();
        if (untapped < cost.TotalValue + reserve) return false;
        return TryPickManaSources(self, cost) != null;
    }

    /// <summary>Sequencing bonus to break CMC ties:
    /// + Creatures get +3 when our battlefield has &lt; 2 creatures (build
    ///   board before doing other things).
    /// + Instants get -1 during sorcery windows (save for opp's turn —
    ///   the instant-window cast path can fire them reactively).
    /// + Sorceries get +1 during sorcery windows (use-it-or-lose-it; we
    ///   can't cast them later this turn).
    /// Net effect: highest-CMC affordable still wins most ties; the
    /// bonuses kick in for same-CMC pairs.</summary>
    /// <summary>Test-only entry point. Production callers go through the
    /// priority bid loop; this surface exists so intent-bias rules can be
    /// asserted in isolation.</summary>
    internal static int SequencingBonusForTests(ICard card, GameContext ctx, bool sorceryWindow, BotIntent intent)
        => SequencingBonus(card, ctx, sorceryWindow, intent);

    private static int SequencingBonus(ICard card, GameContext ctx, bool sorceryWindow, BotIntent intent)
    {
        var bonus = 0;
        var ourCreatures = ctx.Self.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count();
        var ourLands = ctx.Self.Zones.Battlefield.GetCards()
            .OfType<Land>().Count();
        var opp = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, ctx.Self));
        var oppHasFinisher = opp != null && opp.Zones.Battlefield.GetCards()
            .OfType<Creature>().Any(c => c.Power >= 5);

        if (card.HasType(CardType.Creature) && ourCreatures < 2) bonus += 3;
        if (sorceryWindow)
        {
            if (card.HasType(CardType.Sorcery)) bonus += 1;
            if (card.HasType(CardType.Instant)) bonus -= 1;
        }

        // Intent bias. No-ops when intent is None — legacy bonus shape preserved
        // for unannotated / pre-classifier cards.
        if (intent.HasAny(BotIntent.Ramp) && ourLands < 4) bonus += 4;
        if (intent.HasAny(BotIntent.Removal) && oppHasFinisher) bonus += 5;
        if (intent.HasAny(BotIntent.Heal) && ctx.Self.LifeTotal <= 8) bonus += 4;
        if (intent.HasAny(BotIntent.Wrath) && ourCreatures == 0) bonus -= 10;

        return bonus;
    }

    private IActivatedAbility? PickActivatedAbility(GameContext ctx)
    {
        var self = ctx.Self;
        var candidates = self.Zones.Battlefield.GetCards()
            .SelectMany(c => c.Abilities.OfType<IActivatedAbility>())
            // Mana abilities are excluded — they don't fire as priority
            // actions; the mana-payment path consumes them.
            .Where(a => a is not IManaAbility)
            .Where(a => !_abilityFiredThisTurn.Contains(a.Id))
            .Where(a => a.Costs.All(cost => cost.CanPay(self)))
            .ToList();
        // Prefer abilities whose TargetRequests resolve cleanly (we have
        // an opponent / creature / etc. to point at). Simple "first
        // affordable" works at v1; better scoring (Walking Ballista
        // damage first, draw second, etc.) is future work.
        return candidates.FirstOrDefault();
    }

    private static bool IsCastableSpell(ICard c) =>
        c.HasType(CardType.Creature)
        || c.HasType(CardType.Artifact)
        || c.HasType(CardType.Enchantment)
        || c.HasType(CardType.Planeswalker)
        || c.HasType(CardType.Instant)
        || c.HasType(CardType.Sorcery);

    /// <summary>Castable at instant speed: Instants, or permanents with the
    /// Flash keyword (CR 702.8). Sorcery-speed cards (sorceries, vanilla
    /// creatures, etc.) are filtered out during instant windows.</summary>
    private static bool IsInstantSpeed(ICard c)
    {
        if (c.HasType(CardType.Instant)) return true;
        return c.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when we have something to react to — opponent's combat
    /// phases (attackers/blockers windows), a non-empty stack (a spell or
    /// ability we might want to counter / piggy-back on), or opponent's
    /// end step (Brainstorm window). Conservative: outside these phases
    /// the bot still passes to avoid wasting mana on speculative casts.</summary>
    private static bool IsReactiveWindow(GameContext ctx)
    {
        if (!ctx.Stack.IsEmpty) return true;
        // Opponent's turn — react during combat or end step.
        if (!ReferenceEquals(ctx.Self, ctx.ActivePlayer))
        {
            var p = ctx.CurrentPhase;
            return p == Majik.Core.StateMachine.PhaseStateType.DeclareAttackers
                || p == Majik.Core.StateMachine.PhaseStateType.DeclareBlockers
                || p == Majik.Core.StateMachine.PhaseStateType.CombatDamage
                || p == Majik.Core.StateMachine.PhaseStateType.End;
        }
        return false;
    }

    /// <summary>Greedy pick of untapped mana sources to cover <paramref name="cost"/>.
    /// Returns null when the cost can't be paid from current untapped lands.
    /// Pure (doesn't tap anything) — engine's ManaPaymentResolver does the
    /// actual tapping once the payment commits.</summary>
    private static List<ICard>? TryPickManaSources(Player self, ManaCost cost)
    {
        // CR 305.6 — same null-layers fallback as CanAffordWithReserve.
        // When the bot is wired with a ContinuousEffectsService accessor
        // in the future, switch from null to the real service here so
        // Blood-Moon-retyped lands are picked by their NEW mana profile.
        var pool = self.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => !p.IsTapped)
            .Where(p => Majik.Core.Effects.EffectiveManaAbilities.For(p, layers: null).Count > 0)
            .ToList();

        var picked = new List<ICard>();
        var used = new HashSet<Permanent>();

        bool Produces(Permanent p, Func<ManaCost, int> selector)
        {
            var abilities = Majik.Core.Effects.EffectiveManaAbilities.For(p, layers: null);
            if (abilities.Count == 0) return false;
            var mana = abilities[0].ManaGenerated;
            return selector(mana) > 0;
        }

        var quotas = new (Func<ManaCost, int> selector, int needed)[]
        {
            (m => m.White, cost.White),
            (m => m.Blue,  cost.Blue),
            (m => m.Black, cost.Black),
            (m => m.Red,   cost.Red),
            (m => m.Green, cost.Green),
        };

        foreach (var (selector, needed) in quotas)
        {
            for (var i = 0; i < needed; i++)
            {
                var src = pool.FirstOrDefault(p => !used.Contains(p) && Produces(p, selector));
                if (src == null) return null;
                used.Add(src);
                picked.Add(src);
            }
        }
        for (var i = 0; i < cost.Generic; i++)
        {
            var src = pool.FirstOrDefault(p => !used.Contains(p));
            if (src == null) return null;
            used.Add(src);
            picked.Add(src);
        }
        return picked;
    }

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
    {
        // Keep hands with 2–5 lands (CR 103.4 mulligan policy — most
        // 60-card decks want 2–4 lands in their opening seven). Below
        // 2 = mana-screwed; above 5 = mana-flooded. Always keep after
        // 3 mulligans to avoid digging into a one-card hand.
        var landCount = hand.Count(c => c.HasType(CardType.Land));
        var keep = mulligansTaken >= 3 || (landCount >= 2 && landCount <= 5);
        return Task.FromResult(keep ? MulliganDecision.Keep : MulliganDecision.Mulligan);
    }

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
    {
        var label = (request.Description ?? "").ToLowerInvariant();
        var opponent = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, ctx.Self));
        // Prefer self-side targets when the request's BotIntent flags a
        // self-favoring effect (Buff/Heal/Protection/Draw/Cantrip). Fall back
        // to the legacy label-sniff when no intent was stamped (e.g. older
        // templates or compiled rows from pre-classifier DBs).
        var preferSelf = request.Intent.HasAny(
                             BotIntent.Buff | BotIntent.Heal
                             | BotIntent.Protection | BotIntent.Draw
                             | BotIntent.Cantrip)
                         || (request.Intent == BotIntent.None && LabelIsBuff(label));

        // Engine-supplied candidate list takes precedence. Rank them so the
        // "first N" pick is actually the most-impactful N, not the first N
        // by insertion order. Buff-style effects (label hints like "you
        // control", "you may", or no-opponent-context) flip the ranking to
        // prefer caster-side targets.
        if (request.LegalCandidates.Count > 0)
        {
            // Vanilla-shell graceful degrade: notice any unimplemented card
            // in the candidate pool so the operator hears about it. We still
            // rank + pick normally — for permanents the body / mana value
            // is enough signal even when the printed rules text is opaque.
            if (_vanillaTracker is not null)
            {
                foreach (var candidate in request.LegalCandidates)
                {
                    if (candidate is ICard c && c.IsVanillaShell)
                    {
                        _vanillaTracker.Notice(c, ctx.Self, "target candidate");
                    }
                }
            }
            var ordered = RankCandidates(request.LegalCandidates, ctx, opponent, preferSelf, label, request.Intent);
            return Task.FromResult<IReadOnlyList<object>>(
                ordered.Take(request.MinTargets).ToList());
        }

        // Empty candidate list — fall back to engine-side picks. Card
        // binders that lack a candidate-gathering pass (e.g. damage-any
        // templates) get sensible defaults so the cast doesn't crash.
        var picked = new List<object>();
        for (var i = 0; i < request.MinTargets; i++)
        {
            object? choice = label switch
            {
                _ when label.Contains("player") || label.Contains("any target")
                    => PickPlayerTarget(ctx, opponent, preferSelf),
                _ when label.Contains("creature")
                    => PickCreatureTarget(ctx, opponent, preferSelf),
                _ when label.Contains("permanent")
                    => PickPermanentTarget(ctx, opponent, preferSelf),
                _ when label.Contains("spell")
                    => ctx.Stack.Top,
                _ => opponent,
            };
            if (choice != null) picked.Add(choice);
        }
        return Task.FromResult<IReadOnlyList<object>>(picked);
    }

    // ----- target-selection helpers -----

    /// <summary>Heuristic: rank candidates so high-impact picks come first.
    /// Removal/burn defaults to opponent's biggest threat. Buff defaults to
    /// caster's best attacker. Players: lethal-face when opponent low, else
    /// damage opponent.</summary>
    private static IEnumerable<object> RankCandidates(
        IReadOnlyList<object> candidates, GameContext ctx, Player? opponent,
        bool preferSelf, string label, BotIntent intent = BotIntent.None)
    {
        var self = ctx.Self;
        return candidates.OrderByDescending(c => Score(c, self, opponent, preferSelf, label, intent));
    }

    private static int Score(object candidate, Player self, Player? opponent, bool preferSelf, string label, BotIntent intent = BotIntent.None)
    {
        switch (candidate)
        {
            case Player p:
                if (preferSelf)
                {
                    // Self-favoring effect (Heal, Draw-to-target-player, etc.) —
                    // self beats opponent regardless of life total.
                    return ReferenceEquals(p, self) ? 1000 : 0;
                }
                // Adversarial effect (Burn/Removal-on-player) — pick the
                // closest-to-lethal opponent. Lower life = higher score.
                if (ReferenceEquals(p, opponent)) return 1000 - p.LifeTotal;
                return 0;

            case Creature c:
                var bigThreat = c.Power * 10 + c.Toughness;
                if (Majik.Core.Combat.CombatAbilities.HasFlying(c)) bigThreat += 5;
                if (Majik.Core.Combat.CombatAbilities.HasTrample(c)) bigThreat += 5;
                if (Majik.Core.Combat.CombatAbilities.HasLifelink(c)) bigThreat += 8;
                if (Majik.Core.Combat.CombatAbilities.HasDeathtouch(c)) bigThreat += 3;
                // Intent-aware bumps. Removal+Burn double down on raw power so
                // the biggest opposing threat ranks highest. Pump+CombatTrick
                // prefer our attackers (power) over flat board stats.
                if (intent.HasAny(BotIntent.Removal | BotIntent.Burn))
                {
                    bigThreat += c.Power * 5;                         // emphasise power
                }
                if (intent.HasAny(BotIntent.Buff | BotIntent.CombatTrick))
                {
                    bigThreat += c.Power * 3;                         // bias toward existing attackers
                }
                // For removal: opponent's biggest is BEST; ours is WORST.
                // For buff: ours is BEST; opponent's is WORST.
                var ownership = ReferenceEquals(c.Controller, self) ? 1 : -1;
                return preferSelf ? bigThreat * ownership : bigThreat * -ownership;

            // Stack-resident spell. Counter intent ranks by mana value: the
            // expensive bomb is the one worth answering. (ISpell.SourceCard
            // gives us the mana cost; ManaCost.Parse handles nulls.)
            case Majik.Core.Spells.ISpell spell when intent.HasAny(BotIntent.Counter):
                var src = spell.Card;
                var spellCmc = src != null
                    ? ValueObjects.ManaCost.Parse(src.ManaCost ?? "").TotalValue
                    : 0;
                // Always positive — we WANT to counter the biggest spell.
                return 100 + spellCmc * 10;

            case Cards.ICard card:
                // Generic non-creature permanent / card target. Score by
                // mana value as a "spend" proxy — bigger mana value = more
                // valuable target. Ownership flip same as creature.
                var cmc = ValueObjects.ManaCost.Parse(card.ManaCost ?? "").TotalValue;
                // Bounce / Discard / Mill: opponent's most-expensive card is best.
                if (intent.HasAny(BotIntent.Bounce | BotIntent.Discard | BotIntent.Mill))
                {
                    var oppOwned = ReferenceEquals(card.Controller, self) ? -1 : 1;
                    return cmc * oppOwned * 5;
                }
                var own = ReferenceEquals(card.Controller, self) ? 1 : -1;
                return preferSelf ? cmc * own : cmc * -own;

            default:
                return 0;
        }
    }

    private static bool LabelIsBuff(string label) =>
        label.Contains("you control")
        || label.Contains(" yours")
        || label.Contains("gains")
        || label.Contains("gain life")
        || label.Contains("draws")
        || label.Contains("you own");

    private static object? PickPlayerTarget(GameContext ctx, Player? opponent, bool preferSelf)
        => preferSelf ? (object?)ctx.Self : opponent;

    private static object? PickCreatureTarget(GameContext ctx, Player? opponent, bool preferSelf)
    {
        var pool = (preferSelf ? ctx.Self : opponent)?.Zones.Battlefield
            .GetCards().OfType<Creature>();
        if (pool == null) return null;
        return pool
            .OrderByDescending(c => c.Power * 10 + c.Toughness)
            .FirstOrDefault();
    }

    private static object? PickPermanentTarget(GameContext ctx, Player? opponent, bool preferSelf)
    {
        var pool = (preferSelf ? ctx.Self : opponent)?.Zones.Battlefield.GetCards();
        if (pool == null) return null;
        return pool
            .OrderByDescending(c => ValueObjects.ManaCost.Parse(c.ManaCost ?? "").TotalValue)
            .FirstOrDefault();
    }

    /// <summary>Pick X = the largest value affordable from untapped mana
    /// after subtracting the printed non-X cost. Caps at 10 to avoid
    /// pathological "20-mana Hydroid Krasis on turn 6" type slow-thinking;
    /// the engine still validates legality.
    ///
    /// When the bot can lethal-face by spending more (e.g. Devil's Play
    /// {X}{R}: deals X damage to any target), pick the opponent's life
    /// total when that's >= 1 and we can pay it.</summary>
    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
    {
        var untapped = ctx.Self.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => !p.IsTapped)
            // CR 305.6 — null layers ⇒ printed-abilities fallback; same
            // rationale as CanAffordWithReserve.
            .Where(p => Majik.Core.Effects.EffectiveManaAbilities.For(p, layers: null).Count > 0)
            .Count();
        // Subtract the printed non-X portion of the cost — the engine has
        // already required printed cost paid; X mana sits on top.
        var printed = ManaCost.Parse(source.ManaCost ?? "").TotalValue;
        var available = Math.Max(0, untapped - printed);

        // Lethal-face heuristic: if there's an opponent at low life and the
        // card likely deals X damage, aim for exact-lethal.
        var opp = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, ctx.Self));
        if (opp != null && opp.LifeTotal > 0 && opp.LifeTotal <= available)
        {
            return Task.FromResult(opp.LifeTotal);
        }

        // Otherwise just spend everything we have on X (within sanity cap).
        return Task.FromResult(Math.Min(available, 10));
    }

    /// <summary>Modal-spell mode pick. Without per-card semantics, score
    /// each mode label by simple keyword sniffing:
    ///   + damage / destroy / counter — high value when opponent has board
    ///   + draw / scry / search       — always useful (utility)
    ///   + gain life / prevent        — value when our life is low
    ///   + create / put — board-build, valuable when our board is light
    /// Highest-scored index wins. Tie-break: first.</summary>
    public Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default)
    {
        if (modes.Count == 0) return Task.FromResult(0);

        // Intent-aware path: when the bound SpellDefinition carried a
        // ModeIntents list parallel to modes, score by intent flags
        // against the live board / life state. The list may be shorter
        // than modes when a clause didn't bind to any known template —
        // we fall back to legacy label sniffing for those entries.
        var intents = modeIntents;
        var allIntentsNone = intents == null
            || intents.Count == 0
            || intents.All(i => i == BotIntent.None);
        if (allIntentsNone)
        {
            return Task.FromResult(LegacyChooseMode(ctx, modes));
        }

        var opp = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, ctx.Self));
        var oppHasCreature = opp != null
            && opp.Zones.Battlefield.GetCards().OfType<Creature>().Any();
        var ourCreatureCount = ctx.Self.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count();
        var ourLifeLow = ctx.Self.LifeTotal <= 8;

        var bestIdx = 0;
        var bestScore = int.MinValue;
        for (var i = 0; i < modes.Count; i++)
        {
            var intent = i < intents!.Count ? intents[i] : BotIntent.None;
            int score;
            if (intent == BotIntent.None)
            {
                // Per-mode fallback: this clause didn't classify; reuse
                // the legacy label-sniff score so the bot still has
                // signal even when neighboring modes are intent-tagged.
                score = LegacyScoreLabel(modes[i], oppHasCreature, ourCreatureCount, ourLifeLow);
            }
            else
            {
                score = ScoreIntentForState(intent, oppHasCreature, ourCreatureCount, ourLifeLow);
            }
            // Tiny bias toward earlier modes to break ties (printed order
            // often represents "default" choice).
            score += (modes.Count - i);
            if (score > bestScore) { bestScore = score; bestIdx = i; }
        }
        return Task.FromResult(bestIdx);
    }

    private int LegacyChooseMode(GameContext ctx, IReadOnlyList<string> modes)
    {
        var opp = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, ctx.Self));
        var oppHasCreature = opp != null
            && opp.Zones.Battlefield.GetCards().OfType<Creature>().Any();
        var ourCreatureCount = ctx.Self.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count();
        var ourLifeLow = ctx.Self.LifeTotal <= 8;

        var bestIdx = 0;
        var bestScore = int.MinValue;
        for (var i = 0; i < modes.Count; i++)
        {
            var score = LegacyScoreLabel(modes[i], oppHasCreature, ourCreatureCount, ourLifeLow);
            score += (modes.Count - i);
            if (score > bestScore) { bestScore = score; bestIdx = i; }
        }
        return bestIdx;
    }

    private static int LegacyScoreLabel(
        string mode, bool oppHasCreature, int ourCreatureCount, bool ourLifeLow)
    {
        var label = mode.ToLowerInvariant();
        var score = 0;
        if (oppHasCreature && (label.Contains("destroy")
            || label.Contains("damage") || label.Contains("exile target creature")
            || label.Contains("return target creature"))) score += 30;
        if (label.Contains("counter")) score += oppHasCreature ? 20 : 10;
        if (label.Contains("draw") || label.Contains("scry")) score += 15;
        if (label.Contains("search")) score += 12;
        if (ourLifeLow && (label.Contains("gain") && label.Contains("life")
            || label.Contains("prevent"))) score += 25;
        if (ourCreatureCount < 2 && (label.Contains("create")
            || label.Contains("put") && label.Contains("counter"))) score += 18;
        return score;
    }

    private static int ScoreIntentForState(
        BotIntent intent, bool oppHasCreature, int ourCreatureCount, bool ourLifeLow)
    {
        var score = 0;
        if (intent.HasAny(BotIntent.Removal | BotIntent.Burn | BotIntent.Bounce) && oppHasCreature) score += 30;
        if (intent.HasAny(BotIntent.Counter)) score += oppHasCreature ? 20 : 10;
        if (intent.HasAny(BotIntent.Wrath)) score += oppHasCreature ? 35 : -10;
        if (intent.HasAny(BotIntent.Draw | BotIntent.Cantrip)) score += 15;
        if (intent.HasAny(BotIntent.Tutor)) score += 12;
        if (intent.HasAny(BotIntent.Heal) && ourLifeLow) score += 25;
        if (intent.HasAny(BotIntent.Token) && ourCreatureCount < 2) score += 18;
        if (intent.HasAny(BotIntent.Ramp)) score += 10;
        if (intent.HasAny(BotIntent.Reanimate)) score += 12;
        return score;
    }

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine.ToList());

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
    {
        var sources = TryPickManaSources(ctx.Self, cost) ?? new List<ICard>();
        return Task.FromResult(new ManaPayment(sources));
    }

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
    {
        var defender = ctx.AllPlayers.First(p => !ReferenceEquals(p, ctx.Self));
        var defenderCreatures = defender.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => !c.IsTapped).ToList();
        var ourBattlefield = ctx.Self.Zones.Battlefield.GetCards()
            .OfType<Creature>().ToList();

        // 1) Lethal this turn? Total attack power vs defender life. If we
        //    can kill them now (assuming worst-case blocks absorb just
        //    enough to keep them alive), swing with everything — race won.
        var totalAttackPower = eligibleAttackers.Sum(c => EffectivePower(c));
        var reach = totalAttackPower >= defender.LifeTotal;

        // 2) Are we under threat of lethal next turn? Sum opp's untapped
        //    power against our life (worst case: they untap + attack with
        //    everything next turn). When true, we want defenders home.
        var oppThreat = defender.Zones.Battlefield.GetCards()
            .OfType<Creature>().Sum(c => c.Power);
        var threatenedNextTurn = oppThreat >= ctx.Self.LifeTotal;

        // 2a) Two-ply race lookahead. We can deal `totalAttackPower` this
        //     turn + same again on our next turn (back-of-envelope; ignores
        //     blocked attackers that die this turn). When 2× our reach >=
        //     opp's life AND opp's race-back (their 2× threat) < our life,
        //     we WIN the race even though it looks tight. Override the
        //     hold-back gate and swing with everything — race math beats
        //     defensive caution.
        var ourTwoTurnDamage = totalAttackPower * 2;
        var oppTwoTurnDamage = oppThreat * 2;
        var raceWonLookahead = ourTwoTurnDamage >= defender.LifeTotal
            && oppTwoTurnDamage < ctx.Self.LifeTotal;

        // 3) Compute the set of attackers to hold back as blockers when
        //    threatened. Greedy: pick the smallest set whose collective
        //    toughness can survive opp's biggest attackers. If not
        //    threatened, hold back nothing.
        var holdBack = new HashSet<Creature>();
        if (threatenedNextTurn && !reach && !raceWonLookahead)
        {
            // Need at least one blocker per opp attacker that we can't
            // afford to let through. Greedy: pair our biggest survivors
            // with their biggest attackers.
            var theirAttackersBySize = defenderCreatures
                .OrderByDescending(c => c.Power).ToList();
            var ourSurvivorsBySize = eligibleAttackers
                .Concat(ourBattlefield.Where(c => c.IsTapped))
                .Distinct()
                .OrderByDescending(c => c.Toughness)
                .ToList();
            foreach (var oppAtk in theirAttackersBySize)
            {
                var blocker = ourSurvivorsBySize
                    .Where(c => !holdBack.Contains(c))
                    .Where(c => !c.IsTapped) // tapped already can't block
                    .FirstOrDefault(c => c.Toughness > oppAtk.Power);
                if (blocker != null) holdBack.Add(blocker);
            }
        }

        var attacks = new List<AttackerDeclaration>();
        foreach (var atk in eligibleAttackers)
        {
            if (holdBack.Contains(atk)) continue;

            // Suicidal-attack guard: if every defender creature can
            // profitably kill the attacker (and attacker doesn't trade or
            // race), skip. Reach (lethal this turn) overrides. Two-ply
            // race-won also overrides — committing to the race is more
            // valuable than the trade we'd avoid.
            if (!reach && !raceWonLookahead && IsSuicidalAttack(atk, defenderCreatures)) continue;

            attacks.Add(new AttackerDeclaration(atk, defender));
        }
        return Task.FromResult(new CombatPlan(attacks));
    }

    private static int EffectivePower(Creature c)
    {
        var p = c.Power;
        if (Majik.Core.Combat.CombatAbilities.HasDoubleStrike(c)) p *= 2;
        return p;
    }

    /// <summary>True when EVERY untapped opposing creature can profitably
    /// block this attacker (kill it without dying). Reach honored (defender
    /// flier counts as a potential blocker for our flier).</summary>
    private static bool IsSuicidalAttack(Creature atk, IReadOnlyList<Creature> defenders)
    {
        if (defenders.Count == 0) return false;
        // Unblockable shortcut: flying with no flying/reach defenders.
        var hasFlying = Majik.Core.Combat.CombatAbilities.HasFlying(atk);
        if (hasFlying)
        {
            var blockers = defenders.Where(d =>
                Majik.Core.Combat.CombatAbilities.HasFlying(d)
                || Majik.Core.Combat.CombatAbilities.HasReach(d)).ToList();
            if (blockers.Count == 0) return false;
            return blockers.All(b => DefenderWinsTrade(atk, b));
        }
        return defenders.All(b => DefenderWinsTrade(atk, b));
    }

    private static bool DefenderWinsTrade(Creature atk, Creature blocker)
    {
        // Defender "wins": blocker survives AND kills attacker.
        var atkDt = Majik.Core.Combat.CombatAbilities.HasDeathtouch(atk);
        var bDt = Majik.Core.Combat.CombatAbilities.HasDeathtouch(blocker);
        var atkKillsBlocker = atkDt || atk.Power >= blocker.Toughness;
        var blockerKillsAtk = bDt ? blocker.Power > 0 : blocker.Power >= atk.Toughness;
        return blockerKillsAtk && !atkKillsBlocker;
    }

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
    {
        var assignments = new List<BlockerDeclaration>();
        var available = eligibleBlockers.ToList();

        // Sort attackers biggest-threat-first so best blockers get used on
        // the most dangerous creatures. Trample + lifelink + raw power
        // weighted into the threat score; ties broken by mana value
        // (proxy for "what the spend is").
        var sorted = attackers
            .OrderByDescending(a => ThreatScore(a))
            .ThenByDescending(a => Cmc(a))
            .ToList();

        var incomingDamage = attackers.Sum(a => a.Power);
        var lethalIncoming = incomingDamage >= ctx.Self.LifeTotal;

        foreach (var atk in sorted)
        {
            var atkFirstStrike = Majik.Core.Combat.CombatAbilities.HasFirstStrike(atk)
                                  || Majik.Core.Combat.CombatAbilities.HasDoubleStrike(atk);
            var atkTrample = Majik.Core.Combat.CombatAbilities.HasTrample(atk);
            var atkDeathtouch = Majik.Core.Combat.CombatAbilities.HasDeathtouch(atk);
            var atkMenace = Majik.Core.Combat.CombatAbilities.HasMenace(atk);

            // 1. Deathtouch blocker (any toughness ≥ 1) profitably kills any
            //    attacker, except when first-strike would kill the blocker
            //    before its damage applies.
            var dtBlocker = available
                .Where(b => Majik.Core.Combat.CombatAbilities.HasDeathtouch(b))
                .Where(b => !atkFirstStrike
                    || Majik.Core.Combat.CombatAbilities.HasFirstStrike(b)
                    || b.Toughness > atk.Power)
                .OrderBy(b => Cmc(b))            // sacrifice cheapest deathtoucher
                .FirstOrDefault();

            // 2. Safe block that ALSO kills attacker — survives + kills.
            //    First-strike asymmetry: attacker FS without our FS kills
            //    blocker before its damage applies (blocker doesn't kill).
            var safeKill = available
                .Where(b => CanSurvive(b, atk, atkFirstStrike) && CanKill(b, atk, atkDeathtouch))
                .OrderBy(b => b.Power)            // preserve bigger blockers
                .FirstOrDefault();

            // Prefer deathtouch when both options exist and trade-cost is
            // similar (deathtoucher is usually the cheaper sacrifice).
            var preferDt = dtBlocker != null && (safeKill == null || Cmc(dtBlocker) < Cmc(safeKill));
            if (atkMenace) preferDt = false;      // menace requires 2 blockers

            if (preferDt)
            {
                assignments.Add(new BlockerDeclaration(dtBlocker!, atk));
                available.Remove(dtBlocker!);
                continue;
            }
            if (safeKill != null && !atkMenace)
            {
                assignments.Add(new BlockerDeclaration(safeKill, atk));
                available.Remove(safeKill);
                continue;
            }

            // 3. Profitable trade — both die, attacker's CMC ≥ blocker's.
            var trade = available
                .Where(b => CanKill(b, atk, atkDeathtouch) && !CanSurvive(b, atk, atkFirstStrike))
                .Where(b => Cmc(atk) >= Cmc(b))
                .OrderBy(b => Cmc(b))
                .FirstOrDefault();
            if (trade != null && !atkMenace)
            {
                assignments.Add(new BlockerDeclaration(trade, atk));
                available.Remove(trade);
                continue;
            }

            // 4. Multi-blocker gang kill — pile two or more blockers if
            //    combined power kills attacker AND it's a profitable trade
            //    (we trade ≤ attacker's CMC worth of creatures). Mandatory
            //    when attacker has menace (CR 702.110a — needs ≥ 2).
            var gangSize = atkMenace ? 2 : 2;
            var gang = PickGangBlock(available, atk, atkFirstStrike, atkDeathtouch, gangSize, ctx.Self.LifeTotal, incomingDamage);
            if (gang != null && (atkMenace || ShouldGang(atk, gang, atkTrample, lethalIncoming)))
            {
                foreach (var b in gang)
                {
                    assignments.Add(new BlockerDeclaration(b, atk));
                    available.Remove(b);
                }
                if (atkTrample)
                {
                    // Trample still deals leftover, but reduce by gang toughness.
                    incomingDamage -= Math.Min(atk.Power, gang.Sum(b => b.Toughness));
                }
                else
                {
                    incomingDamage -= atk.Power;
                }
                lethalIncoming = incomingDamage >= ctx.Self.LifeTotal;
                continue;
            }

            // 5. Safe-but-doesn't-kill — smallest tough that survives.
            var safe = available
                .Where(b => CanSurvive(b, atk, atkFirstStrike))
                .OrderBy(b => b.Toughness)
                .FirstOrDefault();
            if (safe != null && !atkMenace)
            {
                assignments.Add(new BlockerDeclaration(safe, atk));
                available.Remove(safe);
                continue;
            }

            // 6. Chump — blocker dies, attacker lives. Only when otherwise
            //    lethal this combat step.
            if (lethalIncoming && !atkMenace)
            {
                var chump = available
                    .OrderBy(b => Cmc(b))
                    .FirstOrDefault();
                if (chump != null)
                {
                    assignments.Add(new BlockerDeclaration(chump, atk));
                    available.Remove(chump);
                    incomingDamage -= atkTrample ? Math.Max(0, atk.Power - chump.Toughness) : atk.Power;
                    lethalIncoming = incomingDamage >= ctx.Self.LifeTotal;
                    continue;
                }
            }
        }

        return Task.FromResult(new BlockPlan(assignments));
    }

    // ----- combat-block helpers -----

    /// <summary>Effective threat used to prioritise blocking order. Power is
    /// the dominant axis; trample + lifelink scale up to reflect the urgency
    /// of stopping them.</summary>
    private static int ThreatScore(Creature c)
    {
        var p = c.Power;
        var score = p;
        if (Majik.Core.Combat.CombatAbilities.HasTrample(c)) score += 2;
        if (Majik.Core.Combat.CombatAbilities.HasLifelink(c)) score += 2;
        if (Majik.Core.Combat.CombatAbilities.HasDoubleStrike(c)) score += p;
        if (Majik.Core.Combat.CombatAbilities.HasDeathtouch(c)) score += 1;
        return score;
    }

    private static int Cmc(Creature c) => ManaCost.Parse(c.ManaCost ?? "").TotalValue;

    /// <summary>True when blocker survives combat damage. Honors first-strike
    /// asymmetry: if attacker first-strikes and blocker doesn't, attacker
    /// damage applies before blocker can return fire — blocker dies unless
    /// its toughness exceeds attacker's power, since it dies before its own
    /// damage step. (Indestructible short-circuits this.)</summary>
    private static bool CanSurvive(Creature blocker, Creature attacker, bool attackerFirstStrike)
    {
        if (Majik.Core.Combat.CombatAbilities.HasIndestructible(blocker)) return true;
        var atkLethal = Majik.Core.Combat.CombatAbilities.HasDeathtouch(attacker)
                        || attacker.Power >= blocker.Toughness;
        if (attackerFirstStrike
            && !Majik.Core.Combat.CombatAbilities.HasFirstStrike(blocker)
            && !Majik.Core.Combat.CombatAbilities.HasDoubleStrike(blocker)
            && atkLethal)
        {
            return false;
        }
        return blocker.Toughness > attacker.Power
            && !Majik.Core.Combat.CombatAbilities.HasDeathtouch(attacker);
    }

    /// <summary>True when blocker's damage step kills attacker. Deathtouch
    /// shortcuts to any non-zero damage. First-strike asymmetry: if attacker
    /// first-strikes and blocker doesn't, attacker's FS damage step happens
    /// first; if it would kill the blocker, the blocker never reaches the
    /// regular damage step and can't deal damage at all.</summary>
    private static bool CanKill(Creature blocker, Creature attacker, bool attackerDeathtouch)
    {
        if (Majik.Core.Combat.CombatAbilities.HasIndestructible(attacker)) return false;

        var atkFirstStrike = Majik.Core.Combat.CombatAbilities.HasFirstStrike(attacker)
                              || Majik.Core.Combat.CombatAbilities.HasDoubleStrike(attacker);
        var blockerFirstStrike = Majik.Core.Combat.CombatAbilities.HasFirstStrike(blocker)
                                  || Majik.Core.Combat.CombatAbilities.HasDoubleStrike(blocker);
        if (atkFirstStrike && !blockerFirstStrike)
        {
            // Attacker damages blocker first; if lethal, blocker never deals damage.
            var atkDealsLethal = attackerDeathtouch || attacker.Power >= blocker.Toughness;
            if (atkDealsLethal && !Majik.Core.Combat.CombatAbilities.HasIndestructible(blocker)) return false;
        }

        if (Majik.Core.Combat.CombatAbilities.HasDeathtouch(blocker) && blocker.Power > 0) return true;
        return blocker.Power >= attacker.Toughness;
    }

    /// <summary>Picks the smallest set of blockers whose combined power kills
    /// the attacker. Returns null when no legal gang exists.</summary>
    private static List<Creature>? PickGangBlock(
        List<Creature> available, Creature atk,
        bool atkFirstStrike, bool atkDeathtouch,
        int minSize, int defenderLife, int incomingDamage)
    {
        var pool = available
            .OrderBy(b => Cmc(b))       // cheapest first — minimise loss
            .ThenBy(b => b.Power)
            .ToList();
        if (pool.Count < minSize) return null;

        // Deathtouch on attacker means ANY damage from attacker kills its
        // blocker. So all gang blockers die regardless of their toughness.
        // Greedy: take smallest blockers whose combined power ≥ attacker
        // toughness.
        var picked = new List<Creature>();
        var combinedPower = 0;
        foreach (var b in pool)
        {
            picked.Add(b);
            combinedPower += b.Power;
            if (combinedPower >= atk.Toughness && picked.Count >= minSize) break;
        }
        return combinedPower >= atk.Toughness && picked.Count >= minSize ? picked : null;
    }

    /// <summary>Gang-block is worth it if the lost blockers' combined CMC is
    /// less than the attacker's CMC, OR if we'd otherwise die.</summary>
    private static bool ShouldGang(Creature atk, List<Creature> gang, bool atkTrample, bool lethalIncoming)
    {
        if (lethalIncoming) return true;
        if (atkTrample) return false;     // trample punishes gang-blocks
        var gangCmc = gang.Sum(b => Cmc(b));
        return gangCmc <= Cmc(atk);
    }

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
    {
        // Heuristic: bottom the most expensive cards first.
        var sorted = hand.OrderByDescending(c =>
                Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost ?? "").TotalValue)
            .Take(countToBottom).ToList();
        return Task.FromResult<IReadOnlyList<ICard>>(sorted);
    }

    /// <summary>
    /// Default all-to-bottom. Future improvement: keep cards that combo with
    /// current hand / board state (e.g. curve-fillers, payoffs already in hand).
    /// For v1 this matches the pre-agent default in <c>OracleSpellBinder.ScryNSpell</c>.
    /// </summary>
    public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => Task.FromResult(new ScryAction.ScryDecision(
            ToBottom: peeked.ToList(),
            TopOrder: Array.Empty<ICard>()));

    /// <summary>
    /// Default all-to-graveyard. Future improvement: keep cards needed on top
    /// (e.g. lands when mana-screwed). For v1 this matches the pre-agent default
    /// in <c>OracleSpellBinder.SurveilSelfSpell</c> and <c>UndergroundMortuaryFactory</c>.
    /// </summary>
    public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => Task.FromResult(new SurveilAction.SurveilDecision(
            ToGraveyard: peeked.ToList(),
            TopOrder: Array.Empty<ICard>()));

    public Task<ICard?> ChooseLibraryPickAsync(
        GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel, CancellationToken ct = default)
        => Task.FromResult<ICard?>(candidates.Count > 0 ? candidates[0] : null);

    /// <summary>
    /// CR 701.15 — reveal-and-choose heuristic. Picks the highest-mana-
    /// value eligible card (biggest tempo / card-quality from the
    /// shipped reveal pile). Returns <see langword="null"/> when the
    /// eligible set is empty — works for both mandatory (no legal pick)
    /// and optional ("you may" decline) clauses. Aggressive accept is
    /// the right posture: every reveal-and-choose card in the audit
    /// (Malevolent Rumble, Impulse, See the Unwritten, Ancient
    /// Stirrings) is a card-advantage upgrade — declining squanders the
    /// effect.
    /// </summary>
    public Task<ICard?> ChooseFromRevealedAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> revealed,
        IReadOnlyList<ICard> eligible,
        bool optional,
        string label,
        CancellationToken ct = default)
    {
        if (eligible == null || eligible.Count == 0)
            return Task.FromResult<ICard?>(null);
        var pick = eligible
            .OrderByDescending(c => Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost ?? "").TotalValue)
            .First();
        return Task.FromResult<ICard?>(pick);
    }

    // PLAN 01 (Slice G) — the bespoke ChooseGiftRecipientAsync override is
    // gone. The gift recipient now flows through this agent's ChooseAsync as
    // an optional PickOne over the opponent pool; ChooseAsync already returns
    // the first candidate for a non-empty optional pick, preserving the bot's
    // most-aggressive "promise the gift to the first opponent" posture (every
    // printed Gift spell's upgrade clause dominates the small-token cost in
    // static evaluation — same posture as the Ascend / Spectacle prompts).

    /// <summary>
    /// Smarter Yes/No heuristics, intent-keyed. Upside intents always
    /// accept; downside intents always decline; CostToDecline accepts only
    /// when the cost looks affordable (heuristic: accept). CheatIntoPlay
    /// accepts (the spec is "you may put a big creature for free" which is
    /// almost never a mistake — same rationale as the bot's Sneak Attack
    /// posture). Falls back to the legacy auto-accept default for unknown
    /// intent.
    /// </summary>
    /// <summary>
    /// PLAN 01 (Slice C) — declarative choice sink. Routes Yes/No through
    /// this agent's intent-aware <see cref="ChooseYesNoAsync(string,BotIntent,CancellationToken)"/>
    /// policy; PickOne/PickN return the first candidate(s) (or decline when
    /// optional), matching the agent's first-pick posture on the bespoke
    /// methods that don't carry a richer heuristic.
    /// </summary>
    public Task<IReadOnlyList<object>> ChooseAsync(
        GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
    {
        var candidates = req.Candidates ?? Array.Empty<object>();
        if (req.Kind == ChoiceKind.YesNo)
        {
            var yes = ChooseYesNoAsync(req.Description, req.Intent, ct).GetAwaiter().GetResult();
            IReadOnlyList<object> r = yes
                ? (candidates.Count > 0 ? new[] { candidates[0] } : new object[] { true })
                : Array.Empty<object>();
            return Task.FromResult(r);
        }
        if (req.Optional && candidates.Count == 0)
            return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        var take = Math.Max(req.Min, candidates.Count > 0 ? 1 : 0);
        IReadOnlyList<object> picked = candidates.Take(Math.Min(take, candidates.Count)).ToList();
        return Task.FromResult(picked);
    }

    public Task<bool> ChooseYesNoAsync(
        string question,
        BotIntent intent,
        CancellationToken ct = default)
    {
        // Aggressive accept on net-upside intents (subsumes the default).
        if (intent.HasAny(BotIntent.CardAdvantage
                          | BotIntent.Buff
                          | BotIntent.Heal
                          | BotIntent.Tutor
                          | BotIntent.Draw
                          | BotIntent.Reanimate
                          | BotIntent.CheatIntoPlay
                          | BotIntent.Token
                          | BotIntent.Ramp))
        {
            return Task.FromResult(true);
        }
        // Hard decline on pure-downside intents.
        if (intent.HasAny(BotIntent.LoseLife | BotIntent.DiscardCost))
        {
            return Task.FromResult(false);
        }
        // CostToDecline ("unless you pay X") — pay only when the tax is
        // small (≤ 2 mana). The factory passes the relevant question
        // string through; richer cost-aware logic ships when the cost is
        // threaded into the prompt signature. v1 declines (i.e. doesn't
        // pay) → the active player takes the trigger's stated penalty.
        if (intent.HasAny(BotIntent.CostToDecline))
        {
            return Task.FromResult(false);
        }
        // Neutral / unclassified — accept (legacy auto-accept posture).
        return Task.FromResult(true);
    }

    /// <summary>
    /// Smarter hand-pick: for <see cref="BotIntent.Discard"/> the bot
    /// pitches the highest-CMC card (excess mana value, less playable
    /// late-game); for <see cref="BotIntent.CheatIntoPlay"/> the bot picks
    /// the highest-CMC creature in the candidate list (biggest cheat).
    /// For everything else falls back to the deterministic first-card
    /// pick used by the default interface implementation.
    /// </summary>
    public Task<ICard?> ChooseFromHandAsync(
        Player chooser,
        IReadOnlyList<ICard> candidates,
        BotIntent intent,
        CancellationToken ct = default)
    {
        if (candidates == null || candidates.Count == 0)
            return Task.FromResult<ICard?>(null);

        if (intent.HasAny(BotIntent.Discard | BotIntent.DiscardCost))
        {
            // Pitch the highest-mana-value card — biggest excess MV that
            // we're least likely to cast organically next few turns.
            var pick = candidates
                .OrderByDescending(c => Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost ?? "").TotalValue)
                .First();
            return Task.FromResult<ICard?>(pick);
        }

        if (intent.HasAny(BotIntent.CheatIntoPlay))
        {
            // Cheat the biggest creature / permanent: bigger MV ≈ bigger
            // tempo swing when bypassing the mana cost.
            var pick = candidates
                .OrderByDescending(c => Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost ?? "").TotalValue)
                .First();
            return Task.FromResult<ICard?>(pick);
        }

        // Fallback to deterministic first pick.
        return Task.FromResult<ICard?>(candidates[0]);
    }
}

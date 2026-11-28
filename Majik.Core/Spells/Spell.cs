using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Spells;

/// <summary>
/// Represents a spell on the stack.
/// </summary>
public class Spell : ISpell
{
    private ResolutionState _resolutionState;
    private readonly List<ITarget> _targets = new();
    private readonly List<ICost> _costs = new();
    private readonly List<IEffect> _effects = new();

    public Guid Id { get; internal set; }
    public Player Controller { get; }
    public DateTime Timestamp { get; }
    public ICard Card { get; }
    public IReadOnlyList<ITarget> Targets => _targets.AsReadOnly();
    public IReadOnlyList<ICost> Costs => _costs.AsReadOnly();
    public IReadOnlyList<IEffect> Effects => _effects.AsReadOnly();
    public bool IsResolving => _resolutionState.IsResolving;

    /// <summary>
    /// The spell's CURRENT targets (CR 601.2c, 115.7), flattened in
    /// announcement order: target slots in order, then each slot's picks. This
    /// is the one representation every reader and writer uses: the cast flow
    /// fills it (<see cref="SetCastTargets"/>), redirect effects rewrite it
    /// (Spellskite, Misdirection, Deflecting Swat … CR 115.7), the CR 608.2b
    /// recheck reads it, and resolution builds the effects from it
    /// (<see cref="ResolveAsync"/>). Copies fill it directly.
    /// </summary>
    public IList<object> ChosenTargets { get; } = new List<object>();

    // Slot sizes of a cast spell's targets (one per target request), so the
    // flat ChosenTargets can be cut back into the slots its EffectFactory reads.
    private int[]? _slotSizes;
    // Rebuilds the effects for a given set of slots (SpellCastFlow's effect
    // pipeline), and the targets the current effects were built for.
    private Func<IReadOnlyList<IReadOnlyList<object>>, IReadOnlyList<IEffect>>? _rebuildEffects;
    private object[]? _effectsBuiltFor;
    // CR 608.2b / 400.7 — each current target as the spell took it (announced,
    // or put there by a redirect) and a card target's zone-change count at that
    // moment. A card that has changed zones since, even back to the same zone,
    // is a new object: no longer the spell's target.
    private object[]? _announced;
    private int?[]? _announcedZoneChanges;
    // The live game the spell is resolving in, for the per-request recheck.
    private GameContext? _resolvingIn;

    /// <summary>
    /// CR 601.2c — <see cref="ChosenTargets"/> cut into one slot per target
    /// request, in announcement order (a view: it always reflects redirects).
    /// Null for spells not cast through SpellCastFlow (copies, test spells).
    /// Setting it (tests) replaces <see cref="ChosenTargets"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>>? AnnouncedTargets
    {
        get
        {
            if (_slotSizes is null) return null;
            var slots = new List<IReadOnlyList<object>>(_slotSizes.Length);
            var i = 0;
            foreach (var size in _slotSizes)
            {
                slots.Add(ChosenTargets.Skip(i).Take(size).ToArray());
                i += size;
            }
            return slots;
        }
        internal set => SetCastTargets(value ?? Array.Empty<IReadOnlyList<object>>(), null);
    }

    /// <summary>
    /// CR 601.2c — record the targets announced for this spell: fills
    /// <see cref="ChosenTargets"/> and remembers the slot shape.
    /// <paramref name="rebuildEffects"/> rebuilds the spell's effects for given
    /// slots. The current effects are taken to be built for these targets.
    /// </summary>
    internal void SetCastTargets(
        IReadOnlyList<IReadOnlyList<object>> slots,
        Func<IReadOnlyList<IReadOnlyList<object>>, IReadOnlyList<IEffect>>? rebuildEffects)
    {
        ChosenTargets.Clear();
        foreach (var slot in slots) foreach (var t in slot) ChosenTargets.Add(t);
        _slotSizes = slots.Select(s => s.Count).ToArray();
        _rebuildEffects = rebuildEffects;
        _effectsBuiltFor = ChosenTargets.ToArray();
        _announced = ChosenTargets.ToArray();
        _announcedZoneChanges = _announced.Select(ZoneChangesOf).ToArray();
    }

    private static int? ZoneChangesOf(object target) => target is Card c ? c.ZoneChangeCount : null;

    /// <summary>
    /// CR 115.7 — a redirect changes the target at <paramref name="index"/> to
    /// <paramref name="newTarget"/>. The new target is held to the same rules
    /// as an announced one from here on (CR 608.2b): the same object, still
    /// targetable, still meeting the target request of its slot.
    /// </summary>
    public void RedirectTarget(int index, object newTarget)
    {
        ArgumentNullException.ThrowIfNull(newTarget);
        ChosenTargets[index] = newTarget;
        if (_announced is not null && _announcedZoneChanges is not null && index < _announced.Length)
        {
            _announced[index] = newTarget;
            _announcedZoneChanges[index] = ZoneChangesOf(newTarget);
        }
    }

    /// <summary>
    /// CR 115.7 — change the target at <paramref name="index"/> to
    /// <paramref name="newTarget"/> only if it is a legal target for this
    /// spell: not the spell itself (CR 115.5), not the current target,
    /// targetable by the spell (hexproof, shroud, protection), and, with the
    /// live game, offered by the slot's target request for the spell's
    /// controller (the pool the cast offered). False, with nothing changed,
    /// otherwise ("if the new target is illegal, the target doesn't change").
    /// </summary>
    public bool TryRedirectTarget(int index, object newTarget, GameContext? game = null)
    {
        if (!IsLegalNewTarget(index, newTarget, game)) return false;
        RedirectTarget(index, newTarget);
        return true;
    }

    /// <summary>The legal new targets for the slot at <paramref name="index"/>
    /// among <paramref name="candidates"/> (see <see cref="TryRedirectTarget"/>).</summary>
    public bool IsLegalNewTarget(int index, object newTarget, GameContext? game)
    {
        ArgumentNullException.ThrowIfNull(newTarget);
        if (index < 0 || index >= ChosenTargets.Count) return false;
        if (ReferenceEquals(newTarget, this) || ReferenceEquals(newTarget, ChosenTargets[index])) return false;
        if (!Majik.Core.Targeting.TargetCandidateService.IsTargetableBy(newTarget, Controller, Card)) return false;
        if (game is not null && RequestForTarget(index) is { } request)
        {
            var pool = Majik.Core.Targeting.TargetCollection.ResolveLivePool(request, AsController(game));
            if (pool.Count > 0 && !pool.Any(c => ReferenceEquals(c, newTarget))) return false;
        }
        return true;
    }

    /// <summary>The live pool the slot's request offers this spell's
    /// controller (the cast's pool, with the category fallback). A spell that
    /// recorded no request for the slot (a copy, a test spell) gets every
    /// player and battlefield permanent; <see cref="IsLegalNewTarget"/> then
    /// filters.</summary>
    public IReadOnlyList<object> CandidatesForTarget(int index, GameContext game)
    {
        if (RequestForTarget(index) is { } request)
            return Majik.Core.Targeting.TargetCollection.ResolveLivePool(request, AsController(game));
        return game.AllPlayers.Cast<object>()
            .Concat(game.AllPlayers.SelectMany(p => p.Zones.Battlefield.GetCards()))
            .ToList();
    }

    private GameContext AsController(GameContext game) =>
        ReferenceEquals(game.Self, Controller)
            ? game
            : new GameContext(Controller, game.AllPlayers, game.ActivePlayer, game.TurnNumber,
                game.CurrentPhase, game.Stack, game.LandPlayAvailable, game.TurnState);

    /// <summary>The target request that governs <see cref="ChosenTargets"/>[<paramref name="index"/>]
    /// (CR 601.2c), when the cast recorded one per slot.</summary>
    public TargetRequest? RequestForTarget(int index)
    {
        if (_slotSizes is null || RetargetRequests is not { } reqs || reqs.Count != _slotSizes.Length) return null;
        var at = 0;
        for (var slot = 0; slot < _slotSizes.Length; slot++)
        {
            at += _slotSizes[slot];
            if (index < at) return reqs[slot];
        }
        return null;
    }

    /// <summary>
    /// CR 608.2b — whether the target at <paramref name="index"/> of
    /// <see cref="ChosenTargets"/> is still legal.
    /// <para>For a spell cast through SpellCastFlow, a target is legal when
    /// all of these hold:</para>
    /// <list type="bullet">
    ///   <item>CR 400.7 — it is the same object: a card target has not
    ///   changed zones since the spell took it (announced, or redirected via
    ///   <see cref="RedirectTarget"/>);</item>
    ///   <item>CR 115.5 / 702.11 / 702.16 / 702.18 — the spell can still
    ///   target it (hexproof, shroud, protection from one of its colours);</item>
    ///   <item>CR 608.2b — with the live game (<paramref name="game"/>, or the
    ///   one it is resolving in), it still meets its slot's target request: a
    ///   request with a candidate gatherer must still offer it, evaluated for
    ///   the spell's controller ("creature you don't control" after a control
    ///   change). A request with only a static pool is not re-judged.</item>
    /// </list>
    /// The free-text categories are not used: too coarse to decide legality
    /// (Exorcise's "artifact, enchantment, or creature" classifies as Creature).
    /// <para>For other spells (copies, test spells) the rule is
    /// <see cref="TargetLegalityPredicate"/>, else legal.</para>
    /// </summary>
    public bool IsTargetLegal(int index) => IsTargetLegal(index, null);

    /// <inheritdoc cref="IsTargetLegal(int)"/>
    public bool IsTargetLegal(int index, GameContext? game)
    {
        var target = ChosenTargets[index];
        if (_announced is null || _announcedZoneChanges is null)
            return TargetLegalityPredicate?.Invoke(target) ?? true;

        if (index < _announced.Length && ReferenceEquals(_announced[index], target)
            && target is Card card && _announcedZoneChanges[index] is { } changes
            && card.ZoneChangeCount != changes)
            return false;

        if (!Majik.Core.Targeting.TargetCandidateService.IsTargetableBy(target, Controller, Card))
            return false;

        game ??= _resolvingIn;
        if (game is not null && RequestForTarget(index) is { CandidateGatherer: not null } request)
        {
            if (!request.ResolveCandidates(AsController(game)).Any(c => ReferenceEquals(c, target)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// CR 115.7 / 608.2b — make the effects act on the spell's current, legal
    /// targets. Effects are built at cast time and hold their targets, so when
    /// a redirect changed <see cref="ChosenTargets"/>, or a target has become
    /// illegal, the effects are rebuilt through the cast's effect pipeline:
    /// from the current targets, with each illegal one replaced by
    /// <see cref="IllegalTarget.Instance"/> (an object no effect acts on: "the
    /// spell doesn't perform any actions on it", CR 608.2b). A spell whose
    /// every target is illegal never gets here (StackResolver counters it).
    /// No-op for spells without a rebuild (copies, test spells).
    /// </summary>
    private void SyncEffectsWithTargets()
    {
        if (_rebuildEffects is null || _slotSizes is null) return;
        var current = new object[ChosenTargets.Count];
        for (var i = 0; i < current.Length; i++)
            current[i] = IsTargetLegal(i) ? ChosenTargets[i] : IllegalTarget.Instance;
        if (_effectsBuiltFor is not null && current.Length == _effectsBuiltFor.Length
            && current.Zip(_effectsBuiltFor).All(p => ReferenceEquals(p.First, p.Second)))
            return;

        var slots = new List<IReadOnlyList<object>>(_slotSizes.Length);
        var at = 0;
        foreach (var size in _slotSizes)
        {
            slots.Add(current.Skip(at).Take(size).ToArray());
            at += size;
        }
        var rebuilt = _rebuildEffects(slots);
        _effects.Clear();
        _effects.AddRange(rebuilt);
        _effectsBuiltFor = current;
    }

    /// <summary>CR 608.2b — true when this spell has targets and at least one
    /// legality rule applies to them (so the resolution recheck can judge).</summary>
    public bool HasTargetLegalityRules =>
        ChosenTargets.Count > 0
        && (TargetLegalityPredicate != null || _announced != null);

    /// <summary>
    /// Predicate the resolver uses to check whether at least one chosen
    /// target is still legal. Null = spell has no targets (always passes).
    /// </summary>
    public Func<object, bool>? TargetLegalityPredicate { get; set; }

    /// <summary>
    /// CR 608.2 / CR 715.3d — optional override of the post-resolution
    /// destination zone. Stamped by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// from <see cref="Majik.Core.Costs.IAlternativeCost.PostResolutionZone"/>
    /// when the cast used an alt-cost that re-routes destination (Adventure
    /// → Exile so a creature card cast as Adventure does not enter the
    /// battlefield). Read by <see cref="Majik.Core.Services.StackResolver"/>
    /// in preference to the printed-type default when non-null.
    /// </summary>
    public ZoneType? PostResolutionZoneOverride { get; set; }

    /// <summary>
    /// CR 118 — "no mana was spent to cast this spell" sentinel. Stamped by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the resolved total
    /// cost (printed + X + alt-cost overrides + Delve / cost reductions) is
    /// <c>ManaCost.Zero</c>. Read by triggers gated on the free-cast posture
    /// — Roiling Vortex's "Whenever a player casts a spell, if no mana was
    /// spent to cast it, …" is the prototypical consumer. Defaults to
    /// <c>false</c> so hand-built test spells without an explicit stamp are
    /// treated as normal (mana-paid) casts.
    /// </summary>
    public bool WasFreeCast { get; set; }

    /// <summary>
    /// CR 118.10 — "the amount of mana spent to cast this spell." The total
    /// mana value of the cost actually paid for this cast: printed cost + X
    /// (its chosen value, CR 107.3) + additional costs (Kicker pips) − cost
    /// reductions (Delve / Convoke / Improvise / Affinity), i.e. the mana
    /// value of the same resolved <c>totalCost</c> that drives
    /// <see cref="WasFreeCast"/> (<see cref="WasFreeCast"/> is exactly this
    /// being zero). Stamped by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> alongside
    /// <see cref="WasFreeCast"/> and mirrored onto the underlying
    /// <see cref="Majik.Core.Cards.Card.TotalManaSpentThisCast"/>.
    ///
    /// <para>This is the magnitude sibling of the per-color spent-count
    /// ledger (<see cref="Majik.Core.Cards.Card.PendingCastColorCounts"/>):
    /// the count ledger answers "how much of color X" (Adamant /
    /// Sunburst / hybrid Incarnations); this answers "how much in total"
    /// — the gate the Opus / Selfie-Shot / Adamant-total ("if {N} or more
    /// mana was spent to cast it") payoffs read off the watched spell.
    /// Convoke / Improvise pay with tapped creatures / artifacts, NOT mana,
    /// so those reductions correctly lower this total (CR 118.10 — only
    /// mana counts). Defaults to <c>0</c> so hand-built test spells without
    /// an explicit stamp report "no mana spent".</para>
    /// </summary>
    public int TotalManaSpentThisCast { get; set; }

    /// <summary>
    /// CR 702.138b — "escaped" runtime sentinel. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used an
    /// <see cref="Majik.Core.Costs.EscapeAlternativeCost"/> alt-cost.
    /// Read by downstream gates that branch on "escaped"-ness:
    /// <see cref="Majik.Core.CardData.Factories.UroTitanFactory"/>'s
    /// "sacrifice it unless it escaped" trigger is the canonical
    /// consumer; future <em>escapes with [counters]</em> wiring
    /// (CR 702.138c) reads the same flag on the resolving spell to gate
    /// the ETB-with-counters replacement.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp are treated as normal (non-escape) casts.
    /// </summary>
    public bool WasCastForEscape { get; set; }

    /// <summary>
    /// CR 702.62d / 702.62g — "cast via suspend" runtime sentinel on
    /// the resolving spell. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used a
    /// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/> whose
    /// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost.IsSuspendCast"/>
    /// flag is set. Read by downstream gates that branch on the
    /// suspend-cast posture; the matching
    /// <see cref="Majik.Core.Cards.Card.WasCastFromSuspend"/> mirror
    /// stamps the underlying card for resolve-body reads.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp are treated as non-suspend casts.
    /// </summary>
    public bool WasCastFromSuspend { get; set; }

    /// <summary>
    /// CR 601.2 / CR 113.5 — "cast from hand" runtime sentinel on the
    /// resolving spell. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the resolving
    /// spell's source zone (the zone the card was in immediately before
    /// the stack push) was <see cref="Majik.Core.Zones.ZoneType.Hand"/>.
    /// Read by stack-side gates on the "if you cast it from your hand"
    /// branch — Bedlam Reveler's ETB intervening-if is the canonical
    /// consumer; the matching <see cref="Majik.Core.Cards.Card.WasCastFromHand"/>
    /// mirror stamps the underlying card for resolve-body reads after
    /// Stack → Battlefield.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp are treated as non-hand casts.
    /// </summary>
    public bool WasCastFromHand { get; set; }

    /// <summary>
    /// CR 601.2 / CR 113.5 — "cast from library" runtime sentinel on the
    /// resolving spell. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the resolving
    /// spell's source zone was <see cref="Majik.Core.Zones.ZoneType.Library"/>.
    /// Read by ETB intervening-if clauses that gate on the "if it was cast
    /// from your library" branch — Fblthp, the Lost's ETB is the canonical
    /// consumer; the matching <see cref="Majik.Core.Cards.Card.WasCastFromLibrary"/>
    /// mirror stamps the underlying card for resolve-body reads after
    /// Stack → Battlefield.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp are treated as non-library casts.
    /// </summary>
    public bool WasCastFromLibrary { get; set; }

    /// <summary>
    /// CR 601.2 / CR 113.5 — "cast from a graveyard" runtime sentinel on the
    /// resolving spell. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the resolving spell's
    /// source zone (the zone the card was in immediately before the stack
    /// push) was <see cref="Majik.Core.Zones.ZoneType.Graveyard"/> — i.e. the
    /// spell was cast via Flashback / Escape / Disturb / a "you may cast …
    /// from your graveyard" permission (CR 702.34 / 702.138 / 702.143).
    /// Read by graveyard-cast triggers that punish "whenever a player casts a
    /// spell from a graveyard" (Ash Zealot) — the trigger reads it off the
    /// live <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/>'s spell
    /// synchronously, so no <see cref="Majik.Core.Cards.Card"/> mirror is
    /// needed (the read happens at cast time, not after the card resolves).
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an explicit
    /// stamp are treated as non-graveyard casts.
    /// </summary>
    public bool WasCastFromGraveyard { get; set; }

    /// <summary>
    /// CR 702.33b — "kicked" runtime sentinel on the resolving spell.
    /// Stamped <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// when the cast layered a paid
    /// <see cref="Majik.Core.Costs.KickerAdditionalCost"/>. Read by
    /// downstream rules / triggers that branch on the kicker decision
    /// (Burst Lightning's deals-4-instead-of-2 toggle is the canonical
    /// consumer; future kicker-bearing factories that key triggers on
    /// "if [spell] was kicked" read off the resolving spell).
    /// <see cref="Majik.Core.Cards.Card.WasKicked"/> mirrors the flag
    /// on the underlying card for resolve-body reads that don't have
    /// the spell reference handy.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp are treated as non-kicked casts.
    /// </summary>
    public bool WasKicked { get; set; }

    /// <summary>
    /// CR 702.32 — number of times Multikicker (or Kicker) was paid as this
    /// spell was cast. Mirrors <see cref="Majik.Core.Cards.Card.TimesKicked"/>
    /// on the resolving stack object so a resolution that scales on the kick
    /// count (Everflowing Chalice — "a charge counter for each time it was
    /// kicked", CR 702.32c) can read it off the spell handle. Plain Kicker
    /// tops out at 1; defaults to 0 (not kicked).
    /// </summary>
    public int TimesKicked { get; set; }

    /// <summary>
    /// CR 701.59 — the opponent who was promised this spell's gift
    /// (Bloomburrow "Gift" mechanic, e.g. Into the Flood Maw).
    /// Stamped by <see cref="Majik.Core.Game.SpellCastFlow"/> when the
    /// caster opts into the cast-time gift promise. Non-null means the
    /// spell was cast with a gift promised; the resolving effect should
    /// branch on the promise (see
    /// <see cref="Majik.Core.Cards.Card.HasGiftPromised"/> mirror flag
    /// for resolve-body reads that don't have the spell handy).
    /// </summary>
    public Player? GiftRecipient { get; set; }

    /// <summary>
    /// CR 701.5b — "an uncounterable spell can't be countered". Stamped
    /// <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/> when the
    /// resolving card carries a
    /// <see cref="Majik.Core.Abilities.KeywordAbility"/>("Uncounterable")
    /// marker (Emrakul, the Aeons Torn / Apocalypse Hydra cycle); read by
    /// <see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>
    /// so a counter-spell targeting the spell becomes a no-op (the spell
    /// is not removed from the stack and resolves normally).
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp behave as normal (counterable) casts.
    /// </summary>
    public bool CannotBeCountered { get; set; }

    /// <summary>
    /// CR 707.10 / 706.10a — "a copy of a spell". A copy is itself a spell
    /// placed on the stack (so it is a distinct <see cref="IStackObject"/>),
    /// but it has NO card in any zone — its <see cref="Card"/> is the snapshot
    /// of the copied spell's characteristics shared with the original. When a
    /// copy finishes resolving (or otherwise leaves the stack) it ceases to
    /// exist as a state-based action (CR 707.10c / CR 110.5g): it is NOT moved
    /// to a graveyard / battlefield. <see cref="Majik.Core.Services.StackResolver"/>
    /// reads this flag to skip the post-resolution zone move so a copy never
    /// drags the original card (which is still on the stack or already in
    /// another zone) anywhere — the original is left exactly where it was.
    ///
    /// Constructed by <see cref="Majik.Core.Services.SpellCopier"/>; defaults
    /// to <c>false</c> so a normally-cast spell resolves into a zone as usual.
    /// </summary>
    public bool IsCopy { get; set; }

    /// <summary>
    /// CR 702.34a / 702.133a — cast with flashback or jump-start: "exile this
    /// card instead of putting it anywhere else any time it would leave the
    /// stack". Stamped by SpellCastFlow; read by <see cref="SpellStackExit"/>.
    /// </summary>
    public bool ExileWhenLeavesStack { get; set; }

    /// <summary>
    /// CR 702.27a — the buyback cost was paid: as the spell resolves, its card
    /// goes to its owner's hand instead of the graveyard. Only on resolution:
    /// a countered buyback spell goes to the graveyard. Read by
    /// <see cref="SpellStackExit"/>.
    /// </summary>
    public bool ReturnToHandOnResolution { get; set; }

    /// <summary>
    /// CR 707.10a — the per-slot <see cref="Majik.Core.Players.Agents.TargetRequest"/>s
    /// that governed this spell's original target selection (CR 601.2c),
    /// retained on the stack object so a "copy a spell" effect can re-prompt the
    /// copier's controller for NEW targets for the copy ("you may choose new
    /// targets for the copy"). Each request carries the same legal-candidate
    /// pool / <c>CandidateGatherer</c> the original cast used, so a retargeted
    /// copy is held to the same legality.
    ///
    /// <para>Stamped by <see cref="Majik.Core.Game.SpellCastFlow"/> from the
    /// resolved <see cref="Majik.Core.CardData.Definitions.SpellDefinition.TargetRequests"/>
    /// (and by hand on test spells). Read by
    /// <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpellAsync"/>;
    /// when <c>null</c> / empty (or no live agent is supplied), the copy keeps
    /// the original's chosen targets verbatim — the prior behaviour.</para>
    /// </summary>
    public IReadOnlyList<Majik.Core.Players.Agents.TargetRequest>? RetargetRequests { get; set; }

    public Spell(ICard card, Player controller, IEnumerable<ITarget>? targets = null, IEnumerable<ICost>? costs = null, IEnumerable<IEffect>? effects = null)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        if (controller == null)
        {
            throw new ArgumentNullException(nameof(controller));
        }

        Card = card;
        Controller = controller;
        // PLAN 08 — per-game deterministic id (portal's `stackId`). Reseeded
        // from the ambient DeterministicIdSource inside a game scope; falls back
        // to Guid.NewGuid() for scope-less direct construction.
        Id = Majik.Core.Game.DeterministicIdScope.NewId();
        Timestamp = DateTime.UtcNow;
        _resolutionState = ResolutionState.NotResolving();

        if (targets != null)
        {
            _targets.AddRange(targets);
        }

        if (costs != null)
        {
            _costs.AddRange(costs);
        }

        if (effects != null)
        {
            _effects.AddRange(effects);
        }
    }

    /// <summary>
    /// <see cref="IStackObject.Resolve"/> — synchronous resolution shim over
    /// <see cref="ResolveAsync"/> for stack objects that have not migrated to
    /// the async path (CR 608).
    /// </summary>
    public void Resolve() => ResolveAsync(null, null).GetAwaiter().GetResult();

    /// <summary>
    /// PLAN 01 — resolve the spell's effects (CR 608) on the async path.
    /// Builds a <see cref="ResolutionContext"/> from this spell's
    /// <see cref="Controller"/> + <see cref="ChosenTargets"/> (wrapped as a
    /// single target group to fit the list-of-lists shape) and the
    /// resolver-supplied <paramref name="agent"/> / <paramref name="game"/> /
    /// <paramref name="ct"/>, then awaits each effect in declaration order.
    /// </summary>
    public async ValueTask ResolveAsync(
        IPlayerAgent? agent,
        GameContext? game,
        CancellationToken ct = default)
    {
        if (_resolutionState.IsResolving)
        {
            throw new InvalidOperationException("Spell is already resolving");
        }

        _resolutionState = ResolutionState.Resolving();

        _resolvingIn = game;
        SyncEffectsWithTargets();

        // Spell.ChosenTargets is a flat list (CR 601.2c); present it to the
        // resolution context as a single target group so async effects can
        // read ChosenTargets[0] uniformly with the ability paths.
        var chosen = ChosenTargets.Count > 0
            ? new IReadOnlyList<object>[] { ChosenTargets.ToList() }
            : Array.Empty<IReadOnlyList<object>>();
        // CR 608 — surface the resolving spell's underlying card so resolution
        // effects can read per-cast state stamped at payment time, most notably
        // the mana-provenance colors-spent ledger (Card.PendingCastColors) that
        // gates Converge (Prismatic Ending / Bring to Light, CR 202.2).
        var rc = ResolutionContext.For(
            Controller, agent, game, chosen, ct, sourceCard: Card);

        // Resolution logic (Rule 608) — await each effect in order.
        foreach (var effect in _effects)
        {
            await effect.ExecuteAsync(rc).ConfigureAwait(false);
        }

        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }

}

/// <summary>
/// CR 608.2b — stands in for a target that became illegal when a spell's
/// effects are rebuilt at resolution: it is no card, player or spell, so no
/// effect acts on it ("the spell or ability doesn't perform any actions on it").
/// </summary>
public sealed class IllegalTarget
{
    public static readonly IllegalTarget Instance = new();
    private IllegalTarget() { }
    public override string ToString() => "(illegal target)";
}

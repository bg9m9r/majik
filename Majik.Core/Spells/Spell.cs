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
    /// Raw targets selected at cast time (CR 601.2c). Independent of the
    /// engine's <see cref="ITarget"/> abstraction — used by resolution
    /// recheck (CR 608.2b) to validate against current game state.
    /// </summary>
    public IList<object> ChosenTargets { get; } = new List<object>();

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

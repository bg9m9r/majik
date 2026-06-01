using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613 layer-system executor. Cards/combat consult this to get a
/// creature's CURRENT power/toughness/keywords after every continuous
/// effect has been applied in layer order.
///
/// Within each layer/sublayer effects are ordered per CR 613.8: if
/// effect A depends on effect B (applying B first would change A's
/// existence, what it does, or the set of objects it applies to), B
/// applies before A regardless of timestamp. Independent effects use
/// timestamp order; dependency cycles fall back to timestamp order.
/// </summary>
public sealed class ContinuousEffectsService
{
    private readonly List<ContinuousEffect> _effects = new();

    // -----------------------------------------------------------------------
    // CR 613 layer-pipeline memoization.
    //
    // INVARIANT: the cache key is (generation, Permanent) ONLY — it is NEVER
    // keyed or invalidated by effect type. Any mutation that can change ANY
    // permanent's computed characteristics bumps the single global
    // _generation counter, which invalidates the whole cache at once. This is
    // what makes new effect classes auto-covered: an author adding a brand-new
    // ContinuousEffect subclass need only route its registration through
    // Register/Unregister (or whatever mutator already bumps generation) and
    // the cache stays correct with zero per-type plumbing.
    //
    // The cached value is the LAYERED result only. The cheap Layer-7c counter
    // arithmetic (CR 122.1g) is deliberately EXCLUDED from the cache and
    // re-applied on every Compute after the cache lookup, so counter changes
    // (CounterCollection mutation) never need to bump generation.
    // -----------------------------------------------------------------------
    private long _generation;
    private readonly Dictionary<Permanent, (long gen, PermanentCharacteristics chars)> _cache
        = new(ReferenceEqualityComparer.Instance);

    // -----------------------------------------------------------------------
    // Scalar (Power, Toughness) hot-path cache.
    //
    // Creature.GetPower() and GetToughness() are SEPARATE Compute calls, and
    // both StateSnapshotter and the SBA fixed-point loop read each per creature
    // many times per pass. Serving those through the full Compute() — even on a
    // cache HIT — allocated a fresh CloneLayered() (a PermanentCharacteristics
    // plus four HashSets) just to read two ints back off it.
    //
    // This parallel cache stores the FINAL scalar P/T (the layered result with
    // the Layer-7c counter postlude already folded in), keyed by the SAME
    // generation as the layered cache. A hit returns two ints with ZERO heap
    // allocation and never touches a HashSet. Invalidation is identical to the
    // layered cache: any mutation that can change computed P/T bumps
    // _generation, and counter mutations also bump it
    // (CounterCollection.OnMutated → BumpGeneration), so a generation match is
    // sufficient to serve the cached scalars — counter delta included — without
    // recomputing. Entries are dropped alongside the layered cache in Clear /
    // Prune / ExpireEndOfTurn so the two never diverge.
    // -----------------------------------------------------------------------
    private readonly Dictionary<Permanent, (long gen, int power, int toughness)> _ptCache
        = new(ReferenceEqualityComparer.Instance);

    private readonly IEventBus? _eventBus;
    private readonly Action<GameEvent>? _busHandler;

    public ContinuousEffectsService()
    {
    }

    /// <summary>
    /// Wire the service to the engine <see cref="IEventBus"/> so the
    /// memoization cache stays fresh against external characteristic-defining
    /// inputs. CDAs like Tarmogoyf (graveyard card-type count), Death's
    /// Shadow (controller's life total), and Master of Etherium (artifact
    /// count) read game state that changes WITHOUT touching <c>_effects</c>;
    /// those changes surface as <see cref="CardMovedEvent"/> /
    /// <see cref="LifeChangedEvent"/> / <see cref="CounterAddedEvent"/>
    /// (control changes also ride <see cref="CardMovedEvent"/>). We bump the
    /// global generation on every event — conservative but always correct, and
    /// the P/T-read multipliers (combat damage, the SBA fixed-point loop,
    /// StateSnapshotter) still issue many cached Compute calls between events.
    /// </summary>
    public ContinuousEffectsService(IEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _busHandler = OnBusEvent;
        _eventBus.SubscribeAll(_busHandler);
    }

    private void OnBusEvent(GameEvent _)
    {
        // Any game event can shift an external CDA input (graveyard contents,
        // life totals, control, counts of artifacts/etc.). Bump unconditionally
        // — a spurious bump only costs a recompute, never correctness.
        BumpGeneration();
    }

    /// <summary>
    /// Invalidate the whole memoization cache by advancing the global
    /// generation. Called by every mutator that can change a computed
    /// characteristic (see the INVARIANT note on the cache fields).
    /// </summary>
    internal void BumpGeneration() => _generation++;

    /// <summary>
    /// CR 613 — full invalidate (mirror of <c>TriggerManager.Clear</c>).
    /// Drops every cache entry and advances the generation. Safe to call at
    /// any reset boundary.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _ptCache.Clear();
        _generation++;
    }

    public void Register(ContinuousEffect effect)
    {
        if (effect == null) throw new ArgumentNullException(nameof(effect));
        _effects.Add(effect);
        // CR 613 — an effect's existence/applicability frequently gates on its
        // SOURCE permanent's state (IsActive() ⇒ source on battlefield; "you
        // control" ⇒ source's controller; etc.). Wire the source to this
        // service so its zone/control/tap/counter/ability mutations bump the
        // generation and invalidate the cache. In production every battlefield
        // permanent already carries ActiveEffects; this also covers sources
        // that callers forgot to wire. Never overwrites an existing link.
        if (effect.Source is { ActiveEffects: null } src)
        {
            src.ActiveEffects = this;
        }
        BumpGeneration();
    }

    public void Unregister(ContinuousEffect effect)
    {
        if (effect is GrantAbilityEffect grant) grant.Revoke();
        _effects.Remove(effect);
        BumpGeneration();
    }

    /// <summary>Drop any inactive (expired) effects.</summary>
    public void Prune()
    {
        // CR 613.6e — when a Layer-6 ability-grant ends, the granted ability
        // is removed from the bearer. Revoke before drop so the bearer's
        // Abilities list stays consistent.
        foreach (var grant in _effects.OfType<GrantAbilityEffect>())
        {
            if (!grant.IsActive()) grant.Revoke();
        }
        _effects.RemoveAll(e => !e.IsActive());
        // Effects may have changed AND permanents may have left play; clear
        // opportunistically to bound cache growth, and bump so any survivor's
        // recompute reflects the dropped effects.
        _cache.Clear();
        _ptCache.Clear();
        BumpGeneration();
    }

    /// <summary>
    /// Compute the current characteristics of a creature by applying all
    /// matching effects in layer order. Starts from printed values, runs
    /// effects, returns the final working set.
    /// </summary>
    public CreatureCharacteristics Compute(Creature creature)
    {
        return (CreatureCharacteristics)Compute((Permanent)creature);
    }

    /// <summary>
    /// CR 613 — compute the current characteristics of any permanent by
    /// applying all matching effects in layer order. For creatures this
    /// returns a <see cref="CreatureCharacteristics"/> (so existing
    /// creature-side mutators keep working via the
    /// <see cref="ContinuousEffect.Apply(PermanentCharacteristics)"/>
    /// default dispatch); for non-creature permanents it returns a plain
    /// <see cref="PermanentCharacteristics"/>.
    /// </summary>
    public PermanentCharacteristics Compute(Permanent permanent)
    {
        if (permanent == null) throw new ArgumentNullException(nameof(permanent));

        // Snapshot the generation at entry. The full pipeline below has SIDE
        // EFFECTS (SyncAbilityGrants attaches/removes KeywordAbility markers on
        // bearers via AddAbility/RemoveAbility), and those mutators bump the
        // generation. We must store the cache entry against the generation we
        // SEEDED from — not the post-side-effect value — so a grant attached
        // mid-pass correctly invalidates this entry. The next Compute then
        // re-seeds with the freshly-attached marker and (because Sync is
        // idempotent) stabilizes without further bumps. Storing the
        // post-mutation generation would mark a pre-grant seed as "fresh" and
        // hand back stale keywords.
        var genAtEntry = _generation;

        // ----- (b) generation-counter cache -----------------------------
        // Serve a fresh-generation cached LAYERED result if present. The
        // returned value is a defensive clone (some callers read-mutate the
        // working set), with the cheap Layer-7c counter delta re-applied on
        // top per-call (counters are excluded from the cache — see the field
        // INVARIANT note).
        if (_cache.TryGetValue(permanent, out var entry) && entry.gen == genAtEntry)
        {
            var clone = CloneLayered(entry.chars, permanent);
            ApplyCounterPostlude(clone, permanent);
            return clone;
        }

        // ----- (a) zero-effect fast path --------------------------------
        // With no continuous effects registered, the entire CR-613 layer
        // pipeline (ComputeStrippedSet / SyncAbilityGrants / the LINQ filter /
        // GroupBy / per-layer TopoSort) collapses to "printed values + the
        // Layer-7c counter delta". This is a strict simplification under the
        // _effects.Count == 0 precondition — behaviourally identical to the
        // full path, just allocation-free. Cache the printed (layered) result
        // and re-apply the counter delta on the returned clone.
        if (_effects.Count == 0)
        {
            var printed = SeedPrintedCharacteristics(permanent);
            _cache[permanent] = (genAtEntry, printed);
            var result = CloneLayered(printed, permanent);
            ApplyCounterPostlude(result, permanent);
            return result;
        }

        var chars = SeedPrintedCharacteristics(permanent);

        // CR 613.6 — pre-compute the set of creatures whose abilities have been
        // stripped by an active Layer 6 ability-removing effect. We then drop
        // any continuous effect whose Source is a stripped creature, since a
        // creature with no abilities can no longer produce continuous effects
        // (the dependency relationship between Layer 6 and Layer 7 under CR
        // 613.8). The LoseAllAbilitiesEffect itself is exempt — it must keep
        // applying so its own Apply() clears the affected creature's keywords.
        //
        // Source-suppression here is limited to creature sources: Source is
        // typed as Permanent? but the stripped-set is HashSet<Creature>.
        // Broader stripped-source tracking keyed by Permanent (e.g. for
        // type-changing-to-creature permanents) is a follow-up.
        var stripped = ComputeStrippedSet();

        // CR 613.1f / 613.6e — reconcile every Layer-6 ability-grant before
        // we walk the layer pipeline. If the grant's source is stripped
        // (Humility-class), or its source LTB'd, or the grant target itself
        // is stripped, the grant is revoked so downstream
        // Card.Abilities reads see consistent state.
        SyncAbilityGrants(stripped);

        var applicable = _effects
            .Where(e => e.IsActive() && e.AppliesTo(permanent))
            .Where(e => e is LoseAllAbilitiesEffect
                        || e.Source is not Creature src
                        || !stripped.Contains(src))
            .ToList();

        // CR 613.8 — group by layer, then dependency-sort within each group.
        var byLayer = applicable
            .GroupBy(e => (int)e.Layer)
            .OrderBy(g => g.Key);

        foreach (var group in byLayer)
        {
            foreach (var effect in TopoSortByDependencies(group.ToList()))
            {
                effect.Apply(chars);
            }
        }

        // Cache the LAYERED result against the generation we SEEDED from
        // (genAtEntry), not the possibly-bumped current value — see the
        // snapshot note at the top of this method. Counter delta excluded
        // (field INVARIANT); re-apply it only to the returned value.
        _cache[permanent] = (genAtEntry, chars);
        var computed = CloneLayered(chars, permanent);
        ApplyCounterPostlude(computed, permanent);
        return computed;
    }

    /// <summary>
    /// CR 613 / 708.2 — current (power, toughness) of a creature after the full
    /// layer pipeline + Layer-7c counter postlude, served from a dedicated
    /// SCALAR cache so the hot P/T read path allocates nothing on a cache hit.
    ///
    /// <para>This is the zero-allocation fast lane behind
    /// <see cref="Creature.GetPower"/> / <see cref="Creature.GetToughness"/>.
    /// On a hit it returns two ints without cloning the layered working set or
    /// touching any HashSet; on a miss it falls back to the full
    /// <see cref="Compute(Permanent)"/> (populating BOTH the layered and the
    /// scalar caches) and returns the freshly-computed P/T. The value is
    /// identical to reading <c>Compute(creature).Power/.Toughness</c> — the
    /// scalar cache is keyed by the same generation, so any P/T-affecting
    /// mutation (anthem register/unregister, base-P/T setter, counter add/
    /// remove, strip, EOT expiry, CDA input via the event bus) invalidates it
    /// in lock-step with the layered cache.</para>
    /// </summary>
    public (int Power, int Toughness) ComputePowerToughness(Creature creature)
    {
        if (creature == null) throw new ArgumentNullException(nameof(creature));

        var genAtEntry = _generation;

        // Zero-allocation hit: fresh-generation scalar entry → return the two
        // ints directly. No CloneLayered, no HashSet allocations.
        if (_ptCache.TryGetValue(creature, out var pt) && pt.gen == genAtEntry)
        {
            return (pt.power, pt.toughness);
        }

        // Miss → run the full pipeline once (this also (re)populates the layered
        // _cache). Compute folds in the Layer-7c counter postlude, so the P/T it
        // returns is final. Store the scalars against the generation we SEEDED
        // from — matching Compute's own cache-store discipline (mid-pass grant
        // side-effects may have bumped _generation; keying on genAtEntry keeps
        // this entry invalidated exactly when the layered entry is).
        var chars = (CreatureCharacteristics)Compute((Permanent)creature);
        _ptCache[creature] = (genAtEntry, chars.Power, chars.Toughness);
        return (chars.Power, chars.Toughness);
    }

    /// <summary>
    /// CR 613.1 — seed a permanent's printed/static working set: printed P/T
    /// (creatures), printed card types + subtypes, printed keyword markers,
    /// and the printed/static colour set. SHARED by both the zero-effect fast
    /// path and the full layer pipeline so the two can never diverge.
    /// </summary>
    private static PermanentCharacteristics SeedPrintedCharacteristics(Permanent permanent)
    {
        PermanentCharacteristics chars;
        if (permanent is Creature creature)
        {
            chars = new CreatureCharacteristics
            {
                Power = creature.BasePower,
                Toughness = creature.BaseToughness,
            };
        }
        else
        {
            chars = new PermanentCharacteristics();
        }

        // Seed printed types; Layer 4 effects add/remove on top.
        foreach (var t in permanent.CardTypes) chars.Types.Add(t);
        // Seed printed subtypes; Layer 4 effects add/remove on top.
        foreach (var st in permanent.Subtypes) chars.Subtypes.Add(st);
        // CR 205.4 / 613.1d — seed printed supertypes; GrantSupertypeEffect
        // (Layer 4) adds on top. Mirrors the colour-set seed below.
        foreach (var sup in permanent.Supertypes) chars.Supertypes.Add(sup);
        // Bake in keywords already attached as KeywordAbility markers
        // (printed evergreens like Flying on Air Elemental).
        foreach (var kw in permanent.Abilities.OfType<KeywordAbility>())
        {
            chars.Keywords.Add(kw.Keyword);
        }
        // CR 105 / 613.1e — seed the printed/static colour set (mana-cost
        // pips + colour indicator + token override + Devoid). Layer 5
        // SET / ADD colour effects mutate this on top.
        foreach (var c in Majik.Core.Cards.CardColors.GetColors(permanent))
        {
            chars.Colors.Add(c);
        }

        return chars;
    }

    /// <summary>
    /// Deep-copy the layered working set (everything EXCEPT the Layer-7c
    /// counter delta, which is re-applied per-call). Callers may read-mutate
    /// the returned set, so cache hits must never hand back the stored
    /// instance.
    /// </summary>
    private static PermanentCharacteristics CloneLayered(PermanentCharacteristics src, Permanent permanent)
    {
        PermanentCharacteristics dst;
        if (src is CreatureCharacteristics scc)
        {
            dst = new CreatureCharacteristics { Power = scc.Power, Toughness = scc.Toughness };
        }
        else
        {
            dst = new PermanentCharacteristics();
        }
        foreach (var t in src.Types) dst.Types.Add(t);
        foreach (var st in src.Subtypes) dst.Subtypes.Add(st);
        foreach (var sup in src.Supertypes) dst.Supertypes.Add(sup);
        foreach (var kw in src.Keywords) dst.Keywords.Add(kw);
        foreach (var c in src.Colors) dst.Colors.Add(c);
        return dst;
    }

    /// <summary>
    /// Layer 7c — +1/+1, -1/-1, and -0/-1 counter P/T adjustment
    /// (CR 122.1g). Applied after other 7c effects per CR 613.7 (counters
    /// last). Only meaningful for creatures. SHARED, and deliberately run
    /// OUTSIDE the cache (post cache-lookup) so counter mutation never has to
    /// bump the generation.
    ///   - +1/+1 and -1/-1 both shift power and toughness.
    ///   - -0/-1 (Wall of Roots cost-counter, Phyrexian Furnace's stress
    ///     cycle) shifts only toughness. The named counter type is opaque to
    ///     the +1/+1 / -1/-1 SBA cancellation (CR 704.5q names only that
    ///     pair), so it can stack to lethal-toughness on Wall of Roots
    ///     without being cancelled out.
    /// </summary>
    private static void ApplyCounterPostlude(PermanentCharacteristics chars, Permanent permanent)
    {
        if (chars is not CreatureCharacteristics cc) return;
        var plus = permanent.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne);
        var minus = permanent.Counters.Count(Majik.Core.Counters.CounterType.MinusOneMinusOne);
        var minusZeroMinusOne = permanent.Counters.Count(Majik.Core.Counters.CounterType.MinusZeroMinusOne);
        cc.Power += plus - minus;
        cc.Toughness += plus - minus - minusZeroMinusOne;
    }

    /// <summary>
    /// Walk <see cref="_effects"/> once and collect every creature targeted
    /// by an active <see cref="LoseAllAbilitiesEffect"/>. Used by Compute to
    /// suppress effects sourced from stripped creatures WITHOUT re-entering
    /// Compute (which would recurse indefinitely). Short-circuits to an empty
    /// set when no LoseAllAbilitiesEffect is registered.
    ///
    /// Stripped-set remains keyed by <see cref="Creature"/> for now. A broader
    /// Permanent-keyed stripped-set (needed once non-creature permanents can
    /// have their abilities removed) is a follow-up.
    /// </summary>
    private HashSet<Creature> ComputeStrippedSet()
    {
        var stripped = new HashSet<Creature>();
        foreach (var e in _effects)
        {
            if (e is not LoseAllAbilitiesEffect strip) continue;
            if (!strip.IsActive()) continue;
            foreach (var c in strip.AffectedCreatures())
            {
                stripped.Add(c);
            }
        }
        return stripped;
    }

    /// <summary>
    /// CR 613.8 — Kahn's topological sort within a single layer/sublayer.
    /// Edge: A depends on B (<c>A.DependsOn(B)</c>) ⇒ B must apply before A,
    /// so B has an outgoing edge to A and A's in-degree counts B. Ties broken
    /// by earliest timestamp; same-tick collisions broken by original index
    /// in the group for determinism. If a cycle remains after Kahn drains,
    /// the cycle's members are emitted in timestamp order (then index).
    /// </summary>
    private static IEnumerable<ContinuousEffect> TopoSortByDependencies(IList<ContinuousEffect> group)
    {
        var n = group.Count;
        if (n <= 1)
        {
            foreach (var e in group) yield return e;
            yield break;
        }

        // inDegree[i] = number of effects j in group such that group[i].DependsOn(group[j])
        // (i.e. number of prerequisites of i still unsatisfied).
        var inDegree = new int[n];
        // edges[j] = list of i for which group[i].DependsOn(group[j]) — j unblocks i.
        var edges = new List<int>[n];
        for (var k = 0; k < n; k++) edges[k] = new List<int>();

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                if (i == j) continue;
                if (group[i].DependsOn(group[j]))
                {
                    edges[j].Add(i);
                    inDegree[i]++;
                }
            }
        }

        // Tie-break: earliest timestamp first; same tick → lowest original index.
        int Compare(int a, int b)
        {
            var c = group[a].Timestamp.CompareTo(group[b].Timestamp);
            return c != 0 ? c : a.CompareTo(b);
        }

        var ready = new List<int>();
        for (var i = 0; i < n; i++)
        {
            if (inDegree[i] == 0) ready.Add(i);
        }

        var emitted = new bool[n];
        var emittedCount = 0;

        while (ready.Count > 0)
        {
            ready.Sort(Compare);
            var next = ready[0];
            ready.RemoveAt(0);
            yield return group[next];
            emitted[next] = true;
            emittedCount++;
            foreach (var dependent in edges[next])
            {
                if (--inDegree[dependent] == 0) ready.Add(dependent);
            }
        }

        if (emittedCount == n) yield break;

        // Cycle fallback — remaining nodes form one or more cycles. CR 613.8
        // says fall back to timestamp order for the cycle members.
        var remaining = new List<int>();
        for (var i = 0; i < n; i++)
        {
            if (!emitted[i]) remaining.Add(i);
        }
        remaining.Sort(Compare);
        foreach (var idx in remaining) yield return group[idx];
    }

    /// <summary>Expire and drop all effects whose duration is "until end of turn".</summary>
    public void ExpireEndOfTurn()
    {
        // CR 613.6e — revoke grant lifecycles before drop so the bearer's
        // Abilities list reflects the cleanup-step end of the grant.
        foreach (var grant in _effects.OfType<GrantAbilityEffect>())
        {
            if (grant.ExpiresAtEndOfTurn) grant.Revoke();
        }
        _effects.RemoveAll(e => e.ExpiresAtEndOfTurn);
        // Effects dropped → clear opportunistically (bounds growth) and bump.
        _cache.Clear();
        _ptCache.Clear();
        BumpGeneration();
    }

    /// <summary>
    /// CR 613.1f — reconcile every active <see cref="GrantAbilityEffect"/>
    /// against the current battlefield state. A grant is revoked when its
    /// source has been Humility-stripped (source is in
    /// <paramref name="strippedCreatures"/>) or when its current target is
    /// stripped; otherwise <see cref="GrantAbilityEffect.Sync"/> ensures the
    /// granted ability sits on the live bearer.
    /// </summary>
    private void SyncAbilityGrants(HashSet<Creature> strippedCreatures)
    {
        foreach (var grant in _effects.OfType<GrantAbilityEffect>())
        {
            // Source stripped → grant suppressed (CR 613.8 dependency).
            if (grant.Source is Creature srcCreature && strippedCreatures.Contains(srcCreature))
            {
                grant.Revoke();
                continue;
            }

            // Target stripped → granted ability is removed from the
            // characteristic ability set (CR 613.6 — strip applies after the
            // grant within Layer 6; the strip wins).
            if (grant.GrantedTo is Creature bearerCreature && strippedCreatures.Contains(bearerCreature))
            {
                grant.Revoke();
                continue;
            }

            grant.Sync();

            // Re-check target-stripping in case Sync attached to a freshly
            // resolved bearer that's also under a strip.
            if (grant.GrantedTo is Creature freshBearer && strippedCreatures.Contains(freshBearer))
            {
                grant.Revoke();
            }
        }
    }

    /// <summary>
    /// CR 509.1b — return true iff every active
    /// <see cref="CantBeBlockedExceptByEffect"/> whose source is
    /// <paramref name="attacker"/> accepts <paramref name="blocker"/>.
    /// Multiple such effects intersect (any single rejecting effect forbids
    /// the block). Returns true when no restriction is registered.
    /// </summary>
    public bool CanBlockUnderExceptByRestrictions(Creature attacker, Creature blocker)
    {
        if (attacker == null) throw new ArgumentNullException(nameof(attacker));
        if (blocker == null) throw new ArgumentNullException(nameof(blocker));
        foreach (var e in _effects)
        {
            if (e is not CantBeBlockedExceptByEffect r) continue;
            if (!r.IsActive()) continue;
            if (!ReferenceEquals(r.SourceCard, attacker)) continue;
            if (!r.AllowedBlockerPredicate(blocker)) return false;
        }
        return true;
    }

    /// <summary>
    /// True iff any registered <see cref="CombatRestrictionEffect"/> matches
    /// <paramref name="restriction"/> and either targets
    /// <paramref name="creature"/> directly or has no target (a mass effect
    /// applying to every creature). Consulted by the combat validator
    /// before accepting an attack / block declaration.
    /// </summary>
    public bool HasRestriction(Creature creature, CombatRestriction restriction)
    {
        if (creature == null) throw new ArgumentNullException(nameof(creature));
        foreach (var e in _effects)
        {
            if (MatchesCombatRestriction(e, creature, restriction)) return true;
        }
        return false;
    }

    /// <summary>True iff <paramref name="effect"/> is an active
    /// <see cref="CombatRestrictionEffect"/> of the queried
    /// <paramref name="restriction"/> kind that applies to
    /// <paramref name="creature"/> — either via its dynamic predicate
    /// (Ensnaring Bridge), a direct target match, or a mass effect
    /// (no target).</summary>
    private static bool MatchesCombatRestriction(
        ContinuousEffect effect, Creature creature, CombatRestriction restriction)
    {
        if (effect is not CombatRestrictionEffect r) return false;
        if (!r.IsActive()) return false;
        if (r.Restriction != restriction) return false;
        // Predicate mode (Ensnaring Bridge): evaluate against the queried
        // creature directly so dynamic conditions (power > controller's
        // hand size, etc.) are recomputed every validation pass.
        if (r.Predicate != null)
        {
            return r.Predicate(creature);
        }
        return r.Target == null || ReferenceEquals(r.Target, creature);
    }

    /// <summary>
    /// CR 602.5 / 605.1a — true iff any registered
    /// <see cref="ActivationRestrictionEffect"/> matches
    /// <paramref name="permanent"/>. Consulted by the activation path
    /// before pushing an ability to the stack.
    ///
    /// When <paramref name="isManaAbility"/> is true, restrictions whose
    /// <see cref="ActivationRestrictionEffect.ExcludesManaAbilities"/> is
    /// true do not match — mana abilities (CR 605) bypass the "can't
    /// activate non-mana abilities" lockout.
    /// </summary>
    public bool HasActivationRestriction(Permanent permanent, bool isManaAbility = false)
    {
        if (permanent == null) throw new ArgumentNullException(nameof(permanent));
        foreach (var e in _effects)
        {
            if (e is not ActivationRestrictionEffect r) continue;
            if (!r.IsActive()) continue;
            if (isManaAbility && r.ExcludesManaAbilities) continue;
            if (r.Target == null || ReferenceEquals(r.Target, permanent)) return true;
        }
        return false;
    }

    /// <summary>
    /// CR 205.4 / CR 613.1d — current (effective) supertype set of a
    /// permanent after the Layer-4 type-changing pass, including any active
    /// <see cref="GrantSupertypeEffect"/> (e.g. the Ring-bearer "is legendary"
    /// clause). Mirrors the <c>Compute(perm).Colors</c> read behind
    /// <see cref="Permanent.GetEffectiveColors"/>: it runs the full layer
    /// pipeline and returns the resulting supertype set. The legend-rule and
    /// other supertype-sensitive SBAs read through this so a granted
    /// Legendary is honoured and revoked with the granting effect.
    /// </summary>
    public IReadOnlySet<Majik.Core.Cards.Types.CardSupertype> EffectiveSupertypes(Permanent perm)
    {
        if (perm == null) throw new ArgumentNullException(nameof(perm));
        return Compute(perm).Supertypes;
    }

    /// <summary>
    /// CR 613.2 — current controller of a permanent after applying any
    /// active Layer 2 control-change effects (latest-timestamp wins). Falls
    /// back to <see cref="Permanent.Controller"/> when no override is active.
    /// </summary>
    public Player EffectiveController(Permanent perm)
    {
        if (perm == null) throw new ArgumentNullException(nameof(perm));
        var swap = _effects.OfType<ControlChangeEffect>()
            .Where(e => e.IsActive() && ReferenceEquals(e.Target, perm))
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();
        return swap?.NewController ?? perm.Controller!;
    }
}

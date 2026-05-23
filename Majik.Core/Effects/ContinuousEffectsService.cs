using Majik.Core.Abilities;
using Majik.Core.Cards;
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

    public void Register(ContinuousEffect effect)
    {
        if (effect == null) throw new ArgumentNullException(nameof(effect));
        _effects.Add(effect);
    }

    public void Unregister(ContinuousEffect effect) => _effects.Remove(effect);

    /// <summary>Drop any inactive (expired) effects.</summary>
    public void Prune()
    {
        _effects.RemoveAll(e => !e.IsActive());
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
        // Bake in keywords already attached as KeywordAbility markers
        // (printed evergreens like Flying on Air Elemental).
        foreach (var kw in permanent.Abilities.OfType<KeywordAbility>())
        {
            chars.Keywords.Add(kw.Keyword);
        }

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

        // Layer 7c — +1/+1 and -1/-1 counter P/T adjustment (CR 122.1g).
        // Applied after other 7c effects per CR 613.7 (counters last).
        // Only meaningful for creatures.
        if (chars is CreatureCharacteristics cc && permanent is Permanent perm)
        {
            var plus = perm.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne);
            var minus = perm.Counters.Count(Majik.Core.Counters.CounterType.MinusOneMinusOne);
            cc.Power += plus - minus;
            cc.Toughness += plus - minus;
        }

        return chars;
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
        _effects.RemoveAll(e => e.ExpiresAtEndOfTurn);
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
            if (e is not CombatRestrictionEffect r) continue;
            if (!r.IsActive()) continue;
            if (r.Restriction != restriction) continue;
            if (r.Target == null || ReferenceEquals(r.Target, creature)) return true;
        }
        return false;
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

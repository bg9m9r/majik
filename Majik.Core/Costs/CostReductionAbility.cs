using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 117.7 — "This spell costs {N} less to cast for each X you control."
/// Printed on the card itself; consulted at cast time by
/// <see cref="CostReduction.GetEffectiveCost"/> to lower the spell's
/// generic-mana requirement. Cannot reduce coloured pips (CR 117.7c).
///
/// Instances are static metadata — no resolution / trigger semantics —
/// so they live on the card's <see cref="ICard.Abilities"/> list and are
/// scanned at cost-calculation time.
///
/// Two shapes are supported:
/// 1. Per-instance + predicate (Affinity / Affinity-for-basic-type): the
///    caster's battlefield is scanned, each matching card contributes
///    <see cref="PerInstance"/> generic.
/// 2. Whole-reduction function (<see cref="TotalReducer"/>): the
///    function is called once per cast with the caster and returns the
///    total generic reduction to apply. Used by Domain
///    (CR 702.16 — Scion of Draco / Tribal Flames family) where the
///    reduction is "{N} per distinct basic land type" rather than per
///    instance.
/// 3. Board-aware whole-reduction function (<see cref="ContextReducer"/>):
///    same shape as #2 but the closure receives a <see cref="ReducerContext"/>
///    carrying the caster <b>and the full player roster</b>, so the
///    reduction can count permanents an <i>opponent</i> controls
///    (Hagra Mauling — "costs {1} less if an opponent controls no basic
///    lands"; Affinity-for-opponent-permanents-style reducers). This widens
///    the closure's input beyond the caster — the caster-only
///    <see cref="TotalReducer"/> seam could only see the caster's own board.
/// </summary>
public class CostReductionAbility : IAbility
{
    /// <summary>How many generic mana to remove per matching object the
    /// caster controls. Zero when <see cref="TotalReducer"/> is used.</summary>
    public int PerInstance { get; }

    /// <summary>Predicate matching cards on the caster's battlefield that
    /// count toward the reduction (e.g. all artifacts). Unused when
    /// <see cref="TotalReducer"/> is supplied.</summary>
    public Func<ICard, bool> Predicate { get; }

    /// <summary>Optional whole-reduction computation. When non-null, this
    /// replaces the per-instance scan: the function returns the total
    /// generic reduction to apply for a given caster (e.g. Domain — count
    /// distinct basic land types × {N}).</summary>
    public Func<Player, int>? TotalReducer { get; }

    /// <summary>Optional board-aware whole-reduction computation. When
    /// non-null, this takes precedence over <see cref="PerInstance"/> /
    /// <see cref="TotalReducer"/>: the function receives a
    /// <see cref="ReducerContext"/> (caster + full player roster) and returns
    /// the total generic reduction to apply. This is the seam that lets a
    /// reducer enumerate permanents an opponent controls (Hagra Mauling,
    /// Affinity-for-opponent-permanents). When no player roster is threaded
    /// into <see cref="CostReduction.GetEffectiveCost(ICard, Player,
    /// IEnumerable{Player}?)"/> (callers passing <c>allPlayers: null</c>) the
    /// context still carries the caster alone, so opponent-board-aware
    /// reducers gracefully see an empty opponent set and contribute nothing
    /// (pre-roster cost-calc behaviour for shape tests / agents).</summary>
    public Func<ReducerContext, int>? ContextReducer { get; }

    public string Description { get; }

    public CostReductionAbility(int perInstance, Func<ICard, bool> predicate, string description)
    {
        if (perInstance <= 0) throw new ArgumentOutOfRangeException(nameof(perInstance));
        PerInstance = perInstance;
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        TotalReducer = null;
        ContextReducer = null;
        Description = description ?? string.Empty;
    }

    /// <summary>Construct a whole-reduction cost reducer (e.g. Domain).
    /// <paramref name="totalReducer"/> returns the full generic-mana
    /// reduction for the given caster; floor-at-zero is enforced in
    /// <see cref="CostReduction.GetEffectiveCost"/>.</summary>
    public CostReductionAbility(Func<Player, int> totalReducer, string description)
    {
        TotalReducer = totalReducer ?? throw new ArgumentNullException(nameof(totalReducer));
        ContextReducer = null;
        PerInstance = 0;
        Predicate = static _ => false;
        Description = description ?? string.Empty;
    }

    /// <summary>Construct a board-aware whole-reduction cost reducer
    /// (Hagra Mauling et al.). <paramref name="contextReducer"/> receives a
    /// <see cref="ReducerContext"/> (caster + full player roster) and returns
    /// the full generic-mana reduction; floor-at-zero is enforced in
    /// <see cref="CostReduction.GetEffectiveCost"/>. This is the seam the
    /// caster-only <see cref="TotalReducer"/> could not satisfy — it lets the
    /// closure enumerate opponent-controlled permanents (CR 117.7).</summary>
    public CostReductionAbility(Func<ReducerContext, int> contextReducer, string description)
    {
        ContextReducer = contextReducer ?? throw new ArgumentNullException(nameof(contextReducer));
        TotalReducer = null;
        PerInstance = 0;
        Predicate = static _ => false;
        Description = description ?? string.Empty;
    }

    public static CostReductionAbility AffinityFor(CardType type) =>
        new(1, c => c.HasType(type), $"Affinity for {type.ToString().ToLowerInvariant()}s");
}

/// <summary>
/// CR 117.7 cost-reduction evaluation context. Widens the caster-only input
/// of <see cref="CostReductionAbility.TotalReducer"/> so a board-aware
/// reducer closure can enumerate permanents an <i>opponent</i> controls.
///
/// <para><see cref="Caster"/> is the player casting the spell;
/// <see cref="AllPlayers"/> is the full player roster threaded from the cast
/// flow. <see cref="Opponents"/> is the convenience projection (everyone in
/// the roster who is not the caster). When the cost-calc caller supplies no
/// roster (<c>allPlayers: null</c>), the context carries the caster alone and
/// <see cref="Opponents"/> is empty — opponent-board-aware reducers then
/// contribute nothing, preserving pre-roster behaviour for shape tests /
/// affordability probes.</para>
/// </summary>
public readonly struct ReducerContext
{
    /// <summary>The player casting the spell whose cost is being computed.</summary>
    public Player Caster { get; }

    /// <summary>The full player roster threaded from the cast flow. Always
    /// includes <see cref="Caster"/>; never null (the caster-only fallback
    /// yields a single-element roster).</summary>
    public IReadOnlyList<Player> AllPlayers { get; }

    public ReducerContext(Player caster, IEnumerable<Player>? allPlayers)
    {
        Caster = caster ?? throw new ArgumentNullException(nameof(caster));
        var roster = allPlayers?.Where(p => p != null).ToList();
        if (roster == null || roster.Count == 0)
        {
            roster = new List<Player> { caster };
        }
        else if (!roster.Any(p => ReferenceEquals(p, caster)))
        {
            // Defensive: a roster that somehow omits the caster still names
            // the caster so caster-relative counts stay correct.
            roster.Insert(0, caster);
        }
        AllPlayers = roster;
    }

    /// <summary>Every player in the roster who is not the caster (CR 102.2).
    /// Empty when the caster-only fallback applies.</summary>
    public IEnumerable<Player> Opponents
    {
        get
        {
            // Copy 'this'-backed members to locals — a struct's iterator /
            // closure can't capture 'this' (CS1673).
            var caster = Caster;
            foreach (var p in AllPlayers)
            {
                if (!ReferenceEquals(p, caster)) yield return p;
            }
        }
    }
}

/// <summary>
/// CR 117.7 — additive sibling to <see cref="CostReductionAbility"/>.
/// "Each spell a player casts costs {N} more to cast …" (Damping Sphere,
/// Thalia, Guardian of Thraben, Lodestone Golem family). Lives on a
/// permanent that's already on the battlefield rather than on the spell
/// being cast — <see cref="CostReduction.GetEffectiveCost(ICard, Player,
/// IEnumerable{Player}?)"/> scans every player's battlefield for these
/// abilities at cost-calculation time and sums their per-cast deltas onto
/// the spell's generic cost.
///
/// The increaser owns the spell-eligibility check via <see cref="Predicate"/>
/// — "each spell a player casts" defaults to "all spells" but Thalia-shaped
/// riders restrict to noncreature spells, artifact spells, etc. The
/// <see cref="ExtraGeneric"/> function returns the per-cast {N} given the
/// spell being cast and its caster, so dynamic riders ("for each OTHER
/// spell that player has cast this turn" — Damping Sphere) can read the
/// game's <see cref="Majik.Core.Game.TurnState"/> at evaluation time.
/// </summary>
public sealed class SpellCostIncreaseAbility : IAbility
{
    /// <summary>Predicate matching spells (the card being cast) that this
    /// ability increases the cost of. "Each spell a player casts" maps to
    /// <c>_ =&gt; true</c>.</summary>
    public Func<ICard, bool> Predicate { get; }

    /// <summary>Per-cast generic-mana increase. Inputs are the card being
    /// cast and the caster (so the function can read per-player state such
    /// as <see cref="Majik.Core.Game.TurnState.SpellsCastByPlayer"/>).
    /// Returning zero is fine — emits no rider for that cast.</summary>
    public Func<ICard, Player, int> ExtraGeneric { get; }

    public string Description { get; }

    public SpellCostIncreaseAbility(
        Func<ICard, bool> predicate,
        Func<ICard, Player, int> extraGeneric,
        string description)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        ExtraGeneric = extraGeneric ?? throw new ArgumentNullException(nameof(extraGeneric));
        Description = description ?? string.Empty;
    }
}

/// <summary>
/// CR 117.7 — subtractive sibling to <see cref="SpellCostIncreaseAbility"/>.
/// "&lt;Spell-shape&gt; spells you cast cost {N} less to cast" (Goblin
/// Electromancer, Baral Chief of Compliance, Goblin Anarchomancer family).
/// Lives on a permanent that's already on the battlefield rather than on
/// the spell being cast — <see cref="CostReduction.GetEffectiveCost(ICard,
/// Player, IEnumerable{Player}?)"/> scans the controller's battlefield for
/// these abilities at cost-calculation time and sums their per-cast deltas
/// into the spell's generic-mana reduction.
///
/// The reducer owns the spell-eligibility check via <see cref="Predicate"/>
/// — "instant and sorcery spells you cast" maps to a predicate that returns
/// true only when the spell being cast has CardType.Instant or
/// CardType.Sorcery. Coloured pips are untouched (CR 117.7c); the reduction
/// is layered into the same floor-at-zero clamp as
/// <see cref="CostReductionAbility"/>, before <see cref="SpellCostIncreaseAbility"/>
/// riders are layered back on. Scoped to the CONTROLLER's battlefield —
/// "spells YOU cast" means the controller of the reducer permanent (no
/// opponent-affecting global cost reducers in v1).
/// </summary>
public sealed class SpellCostReductionAbility : IAbility
{
    /// <summary>Predicate matching spells (the card being cast) that this
    /// ability reduces the cost of. "Instant and sorcery spells you cast"
    /// returns true when <c>card.HasType(CardType.Instant) ||
    /// card.HasType(CardType.Sorcery)</c>.</summary>
    public Func<ICard, bool> Predicate { get; }

    /// <summary>Per-cast generic-mana reduction. Inputs are the card being
    /// cast and the caster. Returning zero is fine — emits no reduction for
    /// that cast.</summary>
    public Func<ICard, Player, int> Reduction { get; }

    public string Description { get; }

    public SpellCostReductionAbility(
        Func<ICard, bool> predicate,
        Func<ICard, Player, int> reduction,
        string description)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        Reduction = reduction ?? throw new ArgumentNullException(nameof(reduction));
        Description = description ?? string.Empty;
    }
}

/// <summary>
/// Cost-calculation entry point. Pure function — no side effects. Called
/// by <see cref="Majik.Core.Game.SpellCastFlow"/> for the actual payment
/// cost and by HeuristicBotAgent's mana picker for affordability.
/// </summary>
public static class CostReduction
{
    public static ManaCost GetEffectiveCost(ICard card, Player caster) =>
        GetEffectiveCost(card, caster, allPlayers: null);

    /// <summary>
    /// CR 117.7 / 601.2f cost calculation. Applies (in order):
    ///   1. Printed cost reductions on the card itself
    ///      (<see cref="CostReductionAbility"/> — Affinity, Domain,
    ///      opponent-board-aware reducers via
    ///      <see cref="CostReductionAbility.ContextReducer"/> — Hagra
    ///      Mauling, …). The board-aware shape receives the player roster
    ///      passed via <paramref name="allPlayers"/>; null collapses it to a
    ///      caster-only context (opponent counts read as zero).
    ///   2. Subtractive riders from battlefield permanents under the caster
    ///      (<see cref="SpellCostReductionAbility"/> — Goblin Electromancer,
    ///      Baral, …) — "&lt;X&gt; spells you cast cost {N} less". Always
    ///      scanned against the caster's battlefield (no
    ///      <paramref name="allPlayers"/> needed since the rider is scoped
    ///      to the controller).
    ///   3. Additive riders from battlefield permanents under any player
    ///      (<see cref="SpellCostIncreaseAbility"/> — Damping Sphere, Thalia,
    ///      …) — only scanned when <paramref name="allPlayers"/> is supplied.
    ///      Callers without a game-graph reference pass null and the
    ///      additive riders are silently skipped, preserving pre-rider
    ///      cost-calc behaviour for tests / agents.
    /// Floor at zero applies once after the reduction step (CR 117.7c —
    /// costs can't go below zero); additive riders are then layered back
    /// on, so an additive rider can claw back into positive after a
    /// reduction has driven the cost to zero.
    /// </summary>
    public static ManaCost GetEffectiveCost(
        ICard card,
        Player caster,
        IEnumerable<Player>? allPlayers)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (caster == null) throw new ArgumentNullException(nameof(caster));

        var cost = ManaCost.Parse(card.ManaCost ?? "");
        var reducers = card.Abilities.OfType<CostReductionAbility>().ToList();

        var totalReduction = 0;
        if (reducers.Count > 0)
        {
            var battlefield = caster.Zones.Battlefield.GetCards().ToList();
            ReducerContext? reducerContext = null;
            foreach (var r in reducers)
            {
                if (r.ContextReducer != null)
                {
                    // Board-aware whole-reduction shape (Hagra Mauling et al.).
                    // The closure receives the caster + full player roster so
                    // it can count permanents an opponent controls. When the
                    // caller supplied no roster, ReducerContext degrades to a
                    // caster-only roster and Opponents is empty (the reducer
                    // sees no opponent board, contributes per its own logic).
                    reducerContext ??= new ReducerContext(caster, allPlayers);
                    totalReduction += Math.Max(0, r.ContextReducer(reducerContext.Value));
                    continue;
                }

                if (r.TotalReducer != null)
                {
                    // Whole-reduction shape (Domain et al.). The function
                    // owns its semantics — distinct-basic-type counting for
                    // Domain is computed against the caster's battlefield
                    // and may dwarf the printed generic; floor-at-zero is
                    // enforced below.
                    totalReduction += Math.Max(0, r.TotalReducer(caster));
                    continue;
                }

                // The spell itself doesn't count toward its own Affinity
                // discount (it's still on the stack at cost-calc time, not
                // battlefield); excluding by InstanceId is defensive.
                var count = battlefield.Count(c =>
                    c.InstanceId != card.InstanceId && r.Predicate(c));
                totalReduction += count * r.PerInstance;
            }
        }

        // SpellCostReductionAbility — "<X> spells you cast cost {N} less"
        // (Goblin Electromancer family). Scoped to the caster's battlefield
        // since the printed text says "you cast"; no allPlayers scan needed.
        // Reducer permanent itself doesn't need to exclude — the spell being
        // cast is on the stack, not the battlefield. Folds into the same
        // floor-at-zero bucket as the printed reducers below.
        foreach (var perm in caster.Zones.Battlefield.GetCards())
        {
            foreach (var red in perm.Abilities.OfType<SpellCostReductionAbility>())
            {
                if (!red.Predicate(card)) continue;
                var delta = red.Reduction(card, caster);
                if (delta > 0) totalReduction += delta;
            }
        }

        var totalIncrease = 0;
        if (allPlayers != null)
        {
            foreach (var p in allPlayers)
            {
                if (p == null) continue;
                foreach (var perm in p.Zones.Battlefield.GetCards())
                {
                    foreach (var inc in perm.Abilities.OfType<SpellCostIncreaseAbility>())
                    {
                        if (!inc.Predicate(card)) continue;
                        var delta = inc.ExtraGeneric(card, caster);
                        if (delta > 0) totalIncrease += delta;
                    }
                }
            }
        }

        var newGeneric = Math.Max(0, cost.Generic - totalReduction) + totalIncrease;
        return cost.WithGeneric(newGeneric);
    }
}

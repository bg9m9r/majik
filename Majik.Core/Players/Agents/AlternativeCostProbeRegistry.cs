using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Composite + pluggable registry of <see cref="IAlternativeCostProbe"/>
/// instances. The bot's spell-cast enumeration injects a single
/// <see cref="IAlternativeCostProbe"/> implementation; this registry IS that
/// implementation, fanning <see cref="CandidatesFor"/> across every
/// registered probe and concatenating their yields.
///
/// <para>The default registry (<see cref="CreateDefault"/>) ships with the
/// four core probes covering CR 118.9 alternative costs and adjacent
/// cost-modifications the heuristic bot needs to make sane casting
/// decisions:</para>
///
/// <list type="bullet">
///   <item><see cref="PitchAltCostProbe"/> — CR 118.9 Force-cycle pitch
///   (Force of Will / Force of Negation / Force of Vigor).</item>
///   <item><see cref="DelveAltCostProbe"/> — CR 702.66 delve, wrapped as a
///   discovery-friendly <see cref="DelveAlternativeCost"/>.</item>
///   <item><see cref="OverloadAltCostProbe"/> — CR 702.96 overload
///   (Mizzium Mortars).</item>
///   <item><see cref="CascadeAltCostProbe"/> — CR 702.85 cascade. Yields no
///   alt-cost candidates (cascade doesn't change cost) but exposes a
///   <see cref="CascadeAltCostProbe.HasCascade"/> discovery query and slots
///   into the composite stream cleanly for future surface growth.</item>
/// </list>
///
/// <para>Card factories that need a per-card probe (a "this specific card
/// has a bespoke alt cost not covered by the generic probes" carve-out) can
/// <see cref="Register"/> their own probe at game-setup time. Order of
/// registration is preserved, but consumers should treat the yielded
/// candidates as an unordered set — <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>
/// picks the cheapest affordable bid (CR 118.9 priority is the caster's
/// choice, not insertion order).</para>
///
/// <para>The registry is mutable but not thread-safe — typical wiring sets
/// it up once at game-start and reads from one bot agent on a single
/// thread. Concurrent writers should serialise externally.</para>
/// </summary>
public sealed class AlternativeCostProbeRegistry : IAlternativeCostProbe
{
    private readonly List<IAlternativeCostProbe> _probes = new();

    /// <summary>Snapshot of currently-registered probes. Returned in the
    /// order they were added. Useful for diagnostics + tests.</summary>
    public IReadOnlyList<IAlternativeCostProbe> Probes => _probes.AsReadOnly();

    public AlternativeCostProbeRegistry() { }

    public AlternativeCostProbeRegistry(IEnumerable<IAlternativeCostProbe> probes)
    {
        if (probes == null) throw new ArgumentNullException(nameof(probes));
        foreach (var p in probes) Register(p);
    }

    /// <summary>
    /// Register a probe. Returns the registry to support fluent chaining.
    /// Null probes are rejected; double-registration of the same instance
    /// is allowed (no de-duplication — callers managing their own probe
    /// lifecycles can register the same probe twice if they want its
    /// yields duplicated, though that's typically a bug).
    /// </summary>
    public AlternativeCostProbeRegistry Register(IAlternativeCostProbe probe)
    {
        if (probe == null) throw new ArgumentNullException(nameof(probe));
        _probes.Add(probe);
        return this;
    }

    /// <summary>
    /// Composite enumeration — yields every candidate from every registered
    /// probe in registration order. Each individual probe is responsible
    /// for its own filtering (zone, owner, color matching, etc.); the
    /// registry doesn't dedupe across probes (two probes could in
    /// principle emit different <see cref="IAlternativeCost"/> instances
    /// that pay the same logical cost — caller picks the cheapest).
    /// </summary>
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        foreach (var probe in _probes)
        {
            foreach (var candidate in probe.CandidatesFor(card, caster, ctx))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Build the default registry — pre-populated with Pitch (Force cycle),
    /// Delve, Overload, and Cascade probes. Production wiring (bot agent
    /// construction) uses this; tests can either pass this default or
    /// build a hand-rolled registry with only the probes they want.
    /// </summary>
    public static AlternativeCostProbeRegistry CreateDefault()
    {
        return new AlternativeCostProbeRegistry()
            .Register(new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup))
            .Register(new DelveAltCostProbe())
            .Register(new OverloadAltCostProbe())
            .Register(new CascadeAltCostProbe());
    }
}

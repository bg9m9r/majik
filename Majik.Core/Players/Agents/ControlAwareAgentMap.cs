using Majik.Core.Players;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 720 ("Controlling Another Player") — an <see cref="IReadOnlyDictionary{Player, IPlayerAgent}"/>
/// view that routes agent lookups through a <see cref="ControlPlayerRegistry"/>.
///
/// <para>Every engine decision point looks up the deciding player's agent via
/// <c>agents[player]</c> (priority actions, target/mode/X choices, mulligans,
/// combat declarations, mana payment, …). Wrapping the real agent map in this
/// view makes a single change cover all of them: while a control grant is
/// active for player B (controlled) under player A (controller), <c>this[B]</c>
/// returns A's agent. A then makes every decision B would normally make
/// (CR 720.1), using B's own cards, hand, life, and library (CR 720.2 / 720.3 —
/// only the decision-making is reassigned, never the game objects).</para>
///
/// <para>The wrap is transparent when no control is active (the registry's
/// <see cref="ControlPlayerRegistry.EffectiveDecisionMaker"/> returns the
/// queried player unchanged), so this view is safe to use unconditionally for
/// the whole game — it costs one dictionary lookup per agent access.</para>
/// </summary>
public sealed class ControlAwareAgentMap : IReadOnlyDictionary<Player, IPlayerAgent>
{
    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _inner;
    private readonly ControlPlayerRegistry _control;

    public ControlAwareAgentMap(
        IReadOnlyDictionary<Player, IPlayerAgent> inner,
        ControlPlayerRegistry control)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    /// <summary>
    /// CR 720.1 — return the agent that currently makes
    /// <paramref name="key"/>'s decisions: the controller's agent while
    /// <paramref name="key"/> is under another player's control, otherwise
    /// <paramref name="key"/>'s own agent.
    /// </summary>
    public IPlayerAgent this[Player key] => _inner[_control.EffectiveDecisionMaker(key)];

    // The remaining members project the underlying (un-rerouted) map — the
    // set of seats and their own agents is unchanged by control; only the
    // indexer / TryGetValue reroute the *active decision-maker*. Iterating
    // the map yields the real per-seat agents so diagnostics and wiring that
    // enumerate seats see the unmodified roster.
    public IEnumerable<Player> Keys => _inner.Keys;
    public IEnumerable<IPlayerAgent> Values => _inner.Values;
    public int Count => _inner.Count;
    public bool ContainsKey(Player key) => _inner.ContainsKey(key);

    public bool TryGetValue(Player key, out IPlayerAgent value)
    {
        // Reroute through the active decision-maker so callers that prefer
        // TryGetValue over the indexer (e.g. defensive lookups) honour
        // control too.
        var effective = _control.EffectiveDecisionMaker(key);
        return _inner.TryGetValue(effective, out value!);
    }

    public IEnumerator<KeyValuePair<Player, IPlayerAgent>> GetEnumerator() => _inner.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
}

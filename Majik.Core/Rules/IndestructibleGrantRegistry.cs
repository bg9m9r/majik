using Majik.Core.Cards;

namespace Majik.Core.Rules;

/// <summary>
/// CR 702.12 / 613.1f — process-level registry for "this permanent has
/// indestructible" grants imposed by other game objects (e.g. Darksteel
/// Forge: "Other artifacts you control have indestructible.").
///
/// Mirrors <see cref="FlashGrantRegistry"/>: predicates are registered
/// per source-token while the granting source is on the battlefield, and
/// removed when it leaves. A permanent is granted-indestructible iff at
/// least one registered predicate matches it.
///
/// The destroy gates consult <see cref="HasGrantedIndestructible(ICard)"/>:
/// <list type="bullet">
///   <item><see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>
///         after the layer-system / printed-keyword check (so creatures
///         picked up by the grant survive lethal-damage and destroy
///         SBAs).</item>
///   <item><see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard"/>'s
///         non-creature path after the printed-keyword check (so
///         artifacts / enchantments / lands picked up by the grant resist
///         "destroy target permanent" effects).</item>
/// </list>
///
/// Multiple sources stack additively — two Darksteel Forges each register
/// independent predicates keyed by their own source token; removing one
/// leaves the other's grant intact.
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture / dispose path to avoid leakage across cases.
/// </summary>
public static class IndestructibleGrantRegistry
{
    private static readonly List<(object Token, Func<ICard, bool> Predicate)> _grants = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Register an indestructible-grant predicate keyed by
    /// <paramref name="token"/>. Idempotent for the same token —
    /// re-registering replaces the prior predicate so the latest one
    /// wins (mirrors <see cref="FlashGrantRegistry.AddGrant"/>).
    /// </summary>
    public static void AddGrant(object token, Func<ICard, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_gate)
        {
            for (int i = 0; i < _grants.Count; i++)
            {
                if (ReferenceEquals(_grants[i].Token, token))
                {
                    _grants[i] = (token, predicate);
                    return;
                }
            }
            _grants.Add((token, predicate));
        }
    }

    /// <summary>
    /// Remove the indestructible-grant registered under
    /// <paramref name="token"/>. Used when a source permanent leaves the
    /// battlefield. Idempotent (no-op if the token isn't registered).
    /// </summary>
    public static void RemoveGrant(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _grants.RemoveAll(g => ReferenceEquals(g.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered predicate matches the given card —
    /// i.e. the card currently has indestructible via an external grant.
    /// Returns false for null input.
    /// </summary>
    public static bool HasGrantedIndestructible(ICard? card)
    {
        if (card == null) return false;
        lock (_gate)
        {
            foreach (var (_, predicate) in _grants)
            {
                bool match;
                try { match = predicate(card); }
                catch { match = false; }
                if (match) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _grants.Clear();
        }
    }
}

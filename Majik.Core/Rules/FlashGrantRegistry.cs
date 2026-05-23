using Majik.Core.Cards;

namespace Majik.Core.Rules;

/// <summary>
/// CR 117.1 / 702.8 / 113.3c — process-level registry for "this card has
/// flash" grants imposed by other game objects (e.g. Sigarda's Aid:
/// "Equipment and Auras you control have flash.").
///
/// Unlike <see cref="CastingRestrictions"/>, which gates a player into
/// sorcery speed, this registry whitelists *cards* into instant speed
/// regardless of which zone they currently occupy. A typical caller is
/// a static-ability lifecycle that registers a predicate while its
/// source is on the battlefield (Sigarda's Aid → "card is owned/controlled
/// by Sigarda's controller and is an Equipment or Aura").
///
/// Grants are tracked per source-token so multiple sources can stack
/// without trampling each other; a card is flash-granted iff at least
/// one registered predicate matches it. <see cref="TimingRules.CanCastAtInstantSpeed"/>
/// consults <see cref="HasGrantedFlash(ICard)"/> after the printed
/// Instant/Flash check.
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class FlashGrantRegistry
{
    private static readonly List<(object Token, Func<ICard, bool> Predicate)> _grants = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Register a flash-grant predicate keyed by <paramref name="token"/>.
    /// Idempotent for the same token — re-registering replaces the prior
    /// predicate so the latest one wins (mirrors the pattern Sigarda's Aid
    /// re-flickers would expect).
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
    /// Remove the flash-grant registered under <paramref name="token"/>.
    /// Used when a source permanent leaves the battlefield. Idempotent
    /// (no-op if the token isn't registered).
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
    /// i.e. the card currently has flash via an external grant. Returns
    /// false for null input.
    /// </summary>
    public static bool HasGrantedFlash(ICard card)
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

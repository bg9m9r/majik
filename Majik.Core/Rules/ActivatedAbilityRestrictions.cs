using Majik.Core.Abilities;
using Majik.Core.Cards;

namespace Majik.Core.Rules;

/// <summary>
/// CR 602.5c — process-level registry for activated-ability suppression
/// imposed by other game objects (Pithing Needle, Phyrexian Revoker,
/// Sorcerous Spyglass, Karn the Great Creator, …).
///
/// Two registration shapes are supported:
/// <list type="bullet">
///   <item><b>Name-targeted</b> (Pithing Needle, Phyrexian Revoker) —
///         <see cref="AddNameRestriction"/>. The chosen name is compared
///         against the ability's source card name.</item>
///   <item><b>Predicate-driven</b> (Karn the Great Creator, Drannith
///         Magistrate, etc.) — <see cref="AddPredicateRestriction"/>.
///         A user-supplied <see cref="Predicate{IActivatedAbility}"/>
///         decides per ability whether it is suppressed. Use this for
///         "all opponent artifact activated abilities" / "creature
///         abilities" / source-properties-based filters.</item>
/// </list>
///
/// Entries are tracked per suppressor-token so multiple sources stack
/// without trampling each other; reference equality on the token keeps
/// source lifecycles independent. Each lifecycle binder (e.g.
/// <see cref="Majik.Core.Effects.PithingNeedleStaticEffect"/>,
/// <see cref="Majik.Core.Effects.OpponentArtifactActivatedSuppressionEffect"/>)
/// registers as its source enters the battlefield and unregisters as it
/// leaves via <see cref="Majik.Core.Events.CardMovedEvent"/>.
///
/// <see cref="ActionValidator"/> consults
/// <see cref="IsActivatedAbilityRestricted(IActivatedAbility)"/> during
/// <c>ValidateActivateAbility</c>. The check pulls the source name from
/// the ability's <c>Source</c> when it implements <see cref="ICard"/>; if
/// the source is not a card (custom test fixtures, emblem-style sources),
/// only predicate restrictions can apply — name-targeting requires a
/// named source object.
///
/// Mana abilities (CR 605) are always exempt — both lookup paths short-
/// circuit for <see cref="IManaAbility"/>. ManaAbilityActivator further
/// routes mana abilities around <see cref="ActionValidator"/> entirely.
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class ActivatedAbilityRestrictions
{
    // Each entry: (token, chosen-name). The same token may register only
    // one chosen name in practice (one Needle = one chosen name) but the
    // structure tolerates re-registration without enforcing uniqueness.
    private static readonly List<(object Token, string Name)> _byName = new();
    // Each entry: (token, predicate). Predicate returns true when the
    // ability should be suppressed. Mana-ability exemption applies before
    // predicates are consulted (see IsActivatedAbilityRestricted).
    private static readonly List<(object Token, Predicate<IActivatedAbility> Match)> _byPredicate = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Register a name-targeted activated-ability suppression under
    /// <paramref name="token"/>. Idempotent for the same (token, name)
    /// pair — re-registering does not add a second entry. Names are
    /// matched case-sensitively (Magic card names are canonical).
    /// </summary>
    public static void AddNameRestriction(object token, string name)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Name required", nameof(name));
        }
        lock (_gate)
        {
            foreach (var entry in _byName)
            {
                if (ReferenceEquals(entry.Token, token)
                    && string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    return;
                }
            }
            _byName.Add((token, name));
        }
    }

    /// <summary>
    /// Remove every name-restriction registered under <paramref name="token"/>.
    /// Used when the suppressing source leaves the battlefield.
    /// </summary>
    public static void RemoveNameRestriction(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _byName.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if a registered entry targets the supplied card name. Used
    /// internally by <see cref="IsActivatedAbilityRestricted(IActivatedAbility)"/>
    /// and exposed for diagnostic / test assertions.
    /// </summary>
    public static bool IsNameRestricted(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        lock (_gate)
        {
            foreach (var entry in _byName)
            {
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Register a predicate-driven activated-ability suppression under
    /// <paramref name="token"/>. <paramref name="match"/> is invoked at
    /// validation time with each candidate <see cref="IActivatedAbility"/>;
    /// returning true rejects the activation (subject to the CR 605 mana-
    /// ability exemption applied by
    /// <see cref="IsActivatedAbilityRestricted(IActivatedAbility)"/>).
    ///
    /// Idempotent for the same (token, match) pair — re-registering does
    /// not add a duplicate entry. Multiple predicates may share a token.
    /// Used by Karn the Great Creator's "activated abilities of artifacts
    /// your opponents control can't be activated" static.
    /// </summary>
    public static void AddPredicateRestriction(object token, Predicate<IActivatedAbility> match)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(match);
        lock (_gate)
        {
            foreach (var entry in _byPredicate)
            {
                if (ReferenceEquals(entry.Token, token) && ReferenceEquals(entry.Match, match))
                {
                    return;
                }
            }
            _byPredicate.Add((token, match));
        }
    }

    /// <summary>
    /// Remove every predicate-restriction registered under
    /// <paramref name="token"/>. Used when the suppressing source leaves
    /// the battlefield.
    /// </summary>
    public static void RemovePredicateRestriction(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _byPredicate.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// Decide whether activation of <paramref name="ability"/> should be
    /// rejected by any registered suppression (name or predicate). Returns
    /// true iff:
    /// <list type="bullet">
    ///   <item>The ability is not a mana ability (CR 605 exemption —
    ///         applied first so it short-circuits both lookup paths), AND</item>
    ///   <item>Either (a) the ability's <c>Source</c> is an
    ///         <see cref="ICard"/> whose name matches a registered name
    ///         restriction, OR (b) at least one registered predicate
    ///         returns true for the ability.</item>
    /// </list>
    /// </summary>
    public static bool IsActivatedAbilityRestricted(IActivatedAbility ability)
    {
        if (ability == null) return false;
        // CR 605 — mana abilities are exempt from Pithing-Needle-style
        // suppression. Mana abilities take ManaAbilityActivator path and
        // don't reach ActionValidator.ValidateActivateAbility, but defend
        // in depth in case a caller routes one through here. Karn the
        // Great Creator's printed text similarly exempts mana abilities
        // implicitly — "activated abilities" excludes mana abilities
        // under CR 605.1a.
        if (ability is IManaAbility) return false;

        if (ability.Source is ICard card && IsNameRestricted(card.Name))
        {
            return true;
        }

        lock (_gate)
        {
            foreach (var entry in _byPredicate)
            {
                if (entry.Match(ability)) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _byName.Clear();
            _byPredicate.Clear();
        }
    }
}

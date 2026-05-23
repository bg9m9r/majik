using Majik.Core.Abilities;
using Majik.Core.Cards;

namespace Majik.Core.Rules;

/// <summary>
/// CR 602.5c — process-level registry for name-targeted activated-ability
/// suppression imposed by other game objects (Pithing Needle, Phyrexian
/// Revoker, Sorcerous Spyglass, …).
///
/// Entries are tracked per (chosen-name, suppressor-token) so multiple
/// sources can stack without trampling each other; activation of an
/// activated ability whose source's name matches at least one registered
/// entry is rejected — unless the ability is a mana ability (CR 605, the
/// printed Pithing-Needle exception).
///
/// Reference equality on the suppressor token keeps source lifecycles
/// independent: Pithing Needle's <see cref="Majik.Core.Effects.PithingNeedleStaticEffect"/>
/// is the canonical caller — it registers as the Needle enters the
/// battlefield and unregisters as it leaves via
/// <see cref="Majik.Core.Events.CardMovedEvent"/>.
///
/// <see cref="ActionValidator"/> consults
/// <see cref="IsActivatedAbilityRestricted(IActivatedAbility)"/> during
/// <c>ValidateActivateAbility</c>. The check pulls the source name from
/// the ability's <c>Source</c> when it implements <see cref="ICard"/>; if
/// the source is not a card (custom test fixtures, emblem-style sources),
/// the restriction is treated as not applicable — name-targeting requires
/// a named source object.
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
    /// Decide whether activation of <paramref name="ability"/> should be
    /// rejected by name-targeted suppression. Returns true iff:
    /// <list type="bullet">
    ///   <item>The ability's <c>Source</c> is an <see cref="ICard"/> with
    ///         a non-empty <c>Name</c>, AND</item>
    ///   <item>That name has at least one registered entry, AND</item>
    ///   <item>The ability is not also a mana ability (CR 605 exemption —
    ///         Pithing Needle's printed rider).</item>
    /// </list>
    /// </summary>
    public static bool IsActivatedAbilityRestricted(IActivatedAbility ability)
    {
        if (ability == null) return false;
        // CR 605 — mana abilities are exempt from Pithing Needle. Mana
        // abilities live on a separate activator path (they don't reach
        // ActionValidator.ValidateActivateAbility), but defend in depth
        // in case a caller routes one through here.
        if (ability is IManaAbility) return false;

        if (ability.Source is not ICard card) return false;
        return IsNameRestricted(card.Name);
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _byName.Clear();
        }
    }
}

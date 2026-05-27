using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Rules;

/// <summary>
/// CR 601.3 — process-level registry for casting restrictions imposed by
/// other game objects (e.g. Teferi, Time Raveler's "Each opponent can cast
/// spells only any time they could cast a sorcery").
///
/// Restrictions are tracked per (player, source-token) so multiple sources
/// can stack without trampling each other; a player is restricted iff at
/// least one entry targeting them is currently registered.
///
/// The registry is a singleton-style static service keyed by reference
/// equality on the source token. <see cref="SorcerySpeedRestrictionEffect"/>
/// is the canonical caller — it registers/unregisters as its source
/// permanent enters/leaves the battlefield via
/// <see cref="Majik.Core.Events.CardMovedEvent"/>.
///
/// <see cref="ActionValidator"/> consults
/// <see cref="MustCastAtSorcerySpeed(Player)"/> during cast validation:
/// when the casting player is restricted and the action's
/// <c>SorcerySpeedAvailable</c> flag is false and the card cannot otherwise
/// be cast at instant speed (e.g. via Flash), the cast is rejected.
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class CastingRestrictions
{
    // Each entry: (token, player). A player is restricted while at least
    // one entry targeting them exists.
    private static readonly List<(object Token, Player Player)> _sorcerySpeed = new();
    // "Spells <player> controls can't be countered" turn-scoped rider
    // (Veil of Summer). Stored as a flat set of player IDs; cleared at
    // end of turn by the caller (or via <see cref="Clear"/> in tests).
    private static readonly HashSet<Guid> _uncounterableControllers = new();
    // CR 113.6 — "<player> can't cast spells from anywhere other than
    // their hand" (Drannith Magistrate, Aven Mindcensor, Ethersworn
    // Canonist's cousin). Same (token, player) shape as the sorcery-
    // speed list so multiple sources can stack without trampling.
    private static readonly List<(object Token, Player Player)> _castFromHandOnly = new();
    // CR 601.3 — "<named card> can't be cast" (Meddling Mage). Stored as
    // (token, cardName) entries; a name is blocked while at least one entry
    // targeting it exists.
    private static readonly List<(object Token, string Name)> _namedCardBlocks = new();
    // CR 601.3 — per-player named-card block (Reflector Mage: "That player
    // can't cast spells with the same name as that creature until your
    // next turn"). Stored as (token, playerId, cardName); a name is
    // blocked for a player while at least one entry matching their id +
    // that name exists. Distinct from the global _namedCardBlocks rail
    // above (which gates the name for every player — Meddling Mage's
    // shape) so the two surfaces compose without trampling each other.
    private static readonly List<(object Token, Guid PlayerId, string Name)> _namedCardBlocksByPlayer = new();
    // CR 601.3 — turn-scoped "<player> can't cast noncreature spells this
    // turn" rider (Ranger-Captain of Eos's sacrifice ability). Stored as a
    // flat set of player IDs; cleared by the caller (or via
    // <see cref="Clear"/> in tests). Same lifecycle posture as the
    // turn-scoped uncounterable rider.
    private static readonly HashSet<Guid> _noncreatureRestrictedPlayers = new();
    // CR 601.3 — global "no player may cast spells from this zone" rail
    // (Grafdigger's Cage: "Players can't cast spells from graveyards or
    // libraries."). Stored as (token, zone); a zone is blocked for every
    // player while at least one entry targeting it exists. Distinct from
    // <see cref="_castFromHandOnly"/>, which is per-player and inverts the
    // gate (allow Hand only); this rail blocklists specific zones for
    // everyone.
    private static readonly List<(object Token, ZoneType Zone)> _globalCastZoneBlocks = new();
    private static readonly object _gate = new();

    /// <summary>
    /// Register a sorcery-speed restriction on <paramref name="player"/>,
    /// keyed by <paramref name="token"/>. Idempotent for the same (token,
    /// player) pair — re-registering does not add a second entry.
    /// </summary>
    public static void AddSorcerySpeedRestriction(object token, Player player)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate)
        {
            foreach (var entry in _sorcerySpeed)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Player, player))
                {
                    return;
                }
            }
            _sorcerySpeed.Add((token, player));
        }
    }

    /// <summary>
    /// Remove every sorcery-speed restriction registered under
    /// <paramref name="token"/> (across all players). Used when a source
    /// permanent leaves the battlefield.
    /// </summary>
    public static void RemoveSorcerySpeedRestriction(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _sorcerySpeed.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered restriction currently requires
    /// <paramref name="player"/> to cast spells only at sorcery speed.
    /// </summary>
    public static bool MustCastAtSorcerySpeed(Player player)
    {
        if (player == null) return false;
        lock (_gate)
        {
            foreach (var entry in _sorcerySpeed)
            {
                if (ReferenceEquals(entry.Player, player)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Register a turn-scoped "spells <paramref name="player"/> controls
    /// can't be countered" rider (Veil of Summer, Vexing Shusher, etc.).
    /// Structural for v1 — sets a flag that counter-effect resolvers can
    /// consult via <see cref="SpellsCannotBeCountered"/>. Wiring the flag
    /// into every counter primitive is a follow-up. Cleared by
    /// <see cref="ClearUncounterableForTurn"/> at end of turn.
    /// </summary>
    public static void AddUncounterableForTurn(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate) _uncounterableControllers.Add(player.Id);
    }

    /// <summary>
    /// True if a turn-scoped "spells this player controls can't be
    /// countered" rider is active for <paramref name="player"/>.
    /// </summary>
    public static bool SpellsCannotBeCountered(Player player)
    {
        if (player == null) return false;
        lock (_gate) return _uncounterableControllers.Contains(player.Id);
    }

    /// <summary>
    /// Clear the turn-scoped uncounterable set. Called at end of turn /
    /// cleanup; tests may also call this directly via <see cref="Clear"/>.
    /// </summary>
    public static void ClearUncounterableForTurn()
    {
        lock (_gate) _uncounterableControllers.Clear();
    }

    /// <summary>
    /// Register a "<paramref name="player"/> can't cast spells from
    /// anywhere other than their hand" restriction (CR 113.6 — Drannith
    /// Magistrate et al.), keyed by <paramref name="token"/>. Idempotent
    /// for the same (token, player) pair.
    /// </summary>
    public static void AddCastFromHandOnlyRestriction(object token, Player player)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate)
        {
            foreach (var entry in _castFromHandOnly)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Player, player))
                {
                    return;
                }
            }
            _castFromHandOnly.Add((token, player));
        }
    }

    /// <summary>
    /// Remove every cast-from-hand-only restriction registered under
    /// <paramref name="token"/>. Used when a source permanent leaves the
    /// battlefield.
    /// </summary>
    public static void RemoveCastFromHandOnlyRestriction(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _castFromHandOnly.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered restriction currently confines
    /// <paramref name="player"/> to casting spells only from their hand
    /// (CR 113.6).
    /// </summary>
    public static bool MustCastFromHand(Player player)
    {
        if (player == null) return false;
        lock (_gate)
        {
            foreach (var entry in _castFromHandOnly)
            {
                if (ReferenceEquals(entry.Player, player)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Register a "spells with the named card's name can't be cast"
    /// restriction (CR 601.3 — Meddling Mage), keyed by
    /// <paramref name="token"/>. Idempotent for the same (token, name)
    /// pair. The name comparison is ordinal-case-insensitive to match
    /// Scryfall oracle naming conventions.
    /// </summary>
    public static void AddNamedCardBlock(object token, string cardName)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrEmpty(cardName)) return;
        lock (_gate)
        {
            foreach (var entry in _namedCardBlocks)
            {
                if (ReferenceEquals(entry.Token, token)
                    && string.Equals(entry.Name, cardName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            _namedCardBlocks.Add((token, cardName));
        }
    }

    /// <summary>
    /// Remove every named-card block registered under
    /// <paramref name="token"/>. Used when the Meddling Mage (or similar
    /// source) leaves the battlefield. Clears entries on both the global
    /// rail (<see cref="AddNamedCardBlock"/>) and the per-player rail
    /// (<see cref="AddNamedCardBlockForPlayer"/>) keyed by the same token,
    /// so a single source-token cleanup tears down both shapes.
    /// </summary>
    public static void RemoveNamedCardBlock(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _namedCardBlocks.RemoveAll(e => ReferenceEquals(e.Token, token));
            _namedCardBlocksByPlayer.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered block currently prevents casting a
    /// spell named <paramref name="cardName"/> (CR 601.3).
    /// </summary>
    public static bool IsCardNameBlocked(string cardName)
    {
        if (string.IsNullOrEmpty(cardName)) return false;
        lock (_gate)
        {
            foreach (var entry in _namedCardBlocks)
            {
                if (string.Equals(entry.Name, cardName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Register a per-player "spells with name <paramref name="cardName"/>
    /// can't be cast" restriction (CR 601.3 — Reflector Mage shape),
    /// keyed by <paramref name="token"/>. Idempotent for the same
    /// (token, player, name) triple. Distinct from
    /// <see cref="AddNamedCardBlock"/>, which blocks the name globally
    /// (Meddling Mage's shape) — Reflector Mage's restriction only binds
    /// the bounced creature's owner. The name comparison is
    /// ordinal-case-insensitive.
    /// </summary>
    public static void AddNamedCardBlockForPlayer(object token, Player player, string cardName)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(player);
        if (string.IsNullOrEmpty(cardName)) return;
        lock (_gate)
        {
            foreach (var entry in _namedCardBlocksByPlayer)
            {
                if (ReferenceEquals(entry.Token, token)
                    && entry.PlayerId == player.Id
                    && string.Equals(entry.Name, cardName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            _namedCardBlocksByPlayer.Add((token, player.Id, cardName));
        }
    }

    /// <summary>
    /// True if at least one registered per-player block currently prevents
    /// <paramref name="player"/> from casting a spell named
    /// <paramref name="cardName"/> (CR 601.3 — Reflector Mage shape).
    /// </summary>
    public static bool IsCardNameBlockedForPlayer(Player player, string cardName)
    {
        if (player == null) return false;
        if (string.IsNullOrEmpty(cardName)) return false;
        lock (_gate)
        {
            foreach (var entry in _namedCardBlocksByPlayer)
            {
                if (entry.PlayerId == player.Id
                    && string.Equals(entry.Name, cardName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Register a turn-scoped "noncreature spells <paramref name="player"/>
    /// casts are prohibited" rider (CR 601.3 — Ranger-Captain of Eos's
    /// sacrifice ability: "Your opponents can't cast noncreature spells
    /// this turn."). Cleared by the caller at end of turn via
    /// <see cref="ClearNoncreatureRestrictionForTurn"/> (or
    /// <see cref="Clear"/> in tests). Idempotent.
    /// </summary>
    public static void AddNoncreatureSpellRestrictionForTurn(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate) _noncreatureRestrictedPlayers.Add(player.Id);
    }

    /// <summary>
    /// True if a turn-scoped noncreature-spell restriction is currently
    /// active against <paramref name="player"/> (CR 601.3 — Ranger-Captain
    /// of Eos). Consulted by <see cref="ActionValidator.ValidateCastSpell"/>.
    /// </summary>
    public static bool CannotCastNoncreatureSpell(Player player)
    {
        if (player == null) return false;
        lock (_gate) return _noncreatureRestrictedPlayers.Contains(player.Id);
    }

    /// <summary>
    /// Clear the turn-scoped noncreature-spell restriction set. Called at
    /// end of turn / cleanup; tests may also call this directly via
    /// <see cref="Clear"/>.
    /// </summary>
    public static void ClearNoncreatureRestrictionForTurn()
    {
        lock (_gate) _noncreatureRestrictedPlayers.Clear();
    }

    /// <summary>
    /// Register a global "no player may cast spells from
    /// <paramref name="zone"/>" restriction (CR 601.3 — Grafdigger's Cage:
    /// "Players can't cast spells from graveyards or libraries."), keyed
    /// by <paramref name="token"/>. Idempotent for the same (token, zone)
    /// pair — re-registering does not add a second entry. Multiple zones
    /// per source register as separate entries under the same token.
    /// </summary>
    public static void AddGlobalCastZoneBlock(object token, ZoneType zone)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            foreach (var entry in _globalCastZoneBlocks)
            {
                if (ReferenceEquals(entry.Token, token) && entry.Zone == zone)
                {
                    return;
                }
            }
            _globalCastZoneBlocks.Add((token, zone));
        }
    }

    /// <summary>
    /// Remove every global cast-zone block registered under
    /// <paramref name="token"/>. Used when the source permanent leaves the
    /// battlefield. Scoped by token, so removing one source does not tear
    /// down blocks contributed by other sources.
    /// </summary>
    public static void RemoveGlobalCastZoneBlock(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _globalCastZoneBlocks.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered global block currently prevents
    /// every player from casting spells from <paramref name="zone"/>
    /// (CR 601.3 — Grafdigger's Cage shape). Consulted by
    /// <see cref="ActionValidator.ValidateCastSpell"/>.
    /// </summary>
    public static bool IsCastFromZoneGloballyBlocked(ZoneType zone)
    {
        lock (_gate)
        {
            foreach (var entry in _globalCastZoneBlocks)
            {
                if (entry.Zone == zone) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the registry. Test-only.</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _sorcerySpeed.Clear();
            _uncounterableControllers.Clear();
            _castFromHandOnly.Clear();
            _namedCardBlocks.Clear();
            _namedCardBlocksByPlayer.Clear();
            _noncreatureRestrictedPlayers.Clear();
            _globalCastZoneBlocks.Clear();
        }
    }
}

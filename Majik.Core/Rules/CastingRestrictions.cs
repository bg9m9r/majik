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
    // CR 601.3 — "noncreature spells with mana value N can't be cast" rail
    // (Sanctum Prelate: "Noncreature spells with mana value equal to the
    // chosen number can't be cast."). Stored as (token, manaValue) entries;
    // a mana value is blocked for noncreature spells while at least one entry
    // targeting it exists. Symmetric — applies to every player's noncreature
    // spells (same global posture as _namedCardBlocks, but keyed on mana
    // value and gated to noncreature spells by ActionValidator). Distinct
    // from the per-player turn-scoped _noncreatureRestrictedPlayers rail
    // above (Ranger-Captain), which blocks ALL noncreature spells for a
    // specific player; this rail blocks a SINGLE mana value for everyone.
    private static readonly List<(object Token, int ManaValue)> _noncreatureManaValueBlocks = new();
    // CR 601.3 — global "no player may cast spells from this zone" rail
    // (Grafdigger's Cage: "Players can't cast spells from graveyards or
    // libraries."). Stored as (token, zone); a zone is blocked for every
    // player while at least one entry targeting it exists. Distinct from
    // <see cref="_castFromHandOnly"/>, which is per-player and inverts the
    // gate (allow Hand only); this rail blocklists specific zones for
    // everyone.
    private static readonly List<(object Token, ZoneType Zone)> _globalCastZoneBlocks = new();
    // CR 601.3 — "<player> can't cast spells" total cast block, keyed by
    // source token (Voice of Victory / Grand Abolisher: "Your opponents
    // can't cast spells during your turn."). Same (token, player) shape as
    // the sorcery-speed list so multiple sources stack without trampling.
    // The "during your turn" gating is the caller's responsibility — the
    // source registers an entry per opponent when its controller's turn
    // begins and removes them (via RemoveCannotCastAnySpell) when the turn
    // ends, so a registered entry already means the block is active.
    private static readonly List<(object Token, Player Player)> _cannotCastAnySpell = new();
    // CR 701.5b — "The next spell you cast this turn can't be countered."
    // One-shot per-player flag: consumed (cleared) on the first spell the
    // registered player casts. Distinct from the all-turn uncounterable rail
    // (_uncounterableControllers) used by Veil of Summer — Mistrise Village's
    // oracle is explicitly "the next spell", not "spells you control this
    // turn". SpellCastFlow consumes this flag at cast-time via
    // <see cref="ConsumeNextSpellUncounterableForTurn"/> and stamps
    // <see cref="Majik.Core.Spells.Spell.CannotBeCountered"/> when it fires.
    private static readonly HashSet<Guid> _nextSpellUncounterable = new();
    // CR 601.3 — "You can cast only N more spell(s) this turn" cap (Irencrag
    // Feat: "You can cast only one more spell this turn."). Stored as a per-
    // player remaining-spells counter; a player with a registered entry of 0
    // is blocked from casting further spells this turn. SpellCastFlow
    // decrements the counter after each successful cast via
    // <see cref="ConsumeAdditionalSpellAllowance"/>. Cleared by the caller at
    // end of turn or via <see cref="Clear"/> in tests.
    private static readonly Dictionary<Guid, int> _maxAdditionalSpells = new();
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
    /// Register a one-shot "the next spell <paramref name="player"/> casts
    /// this turn can't be countered" rider (CR 701.5b — Mistrise Village,
    /// Vexing Shusher per-activation shape). Idempotent. Consumed by
    /// <see cref="ConsumeNextSpellUncounterableForTurn"/>: the first call
    /// after this registration returns true and clears the entry; subsequent
    /// casts by the same player are unaffected. Cleared by <see cref="Clear"/>
    /// in tests.
    /// </summary>
    public static void AddNextSpellUncounterableForTurn(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate) _nextSpellUncounterable.Add(player.Id);
    }

    /// <summary>
    /// Consume the one-shot next-spell-uncounterable rider for
    /// <paramref name="player"/>. Returns true (and clears the entry) if the
    /// rider was registered, false otherwise. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> at cast-time to stamp
    /// <see cref="Majik.Core.Spells.Spell.CannotBeCountered"/> on the newly
    /// created spell — the one-shot is consumed on the first cast so only
    /// that spell benefits. CR 514.2 — "this turn" effects expire at
    /// cleanup; the one-shot is structurally limited to a single spell
    /// anyway so no explicit end-of-turn clear is required beyond the
    /// general <see cref="Clear"/> call in tests.
    /// </summary>
    public static bool ConsumeNextSpellUncounterableForTurn(Player player)
    {
        if (player == null) return false;
        lock (_gate)
        {
            if (!_nextSpellUncounterable.Contains(player.Id)) return false;
            _nextSpellUncounterable.Remove(player.Id);
            return true;
        }
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
    /// Register a "noncreature spells with mana value
    /// <paramref name="manaValue"/> can't be cast" restriction (CR 601.3 —
    /// Sanctum Prelate: "Noncreature spells with mana value equal to the
    /// chosen number can't be cast."), keyed by <paramref name="token"/>.
    /// Idempotent for the same (token, manaValue) pair. Global / symmetric:
    /// applies to every player's noncreature spells (gating to noncreature
    /// is <see cref="ActionValidator"/>'s responsibility — it only consults
    /// <see cref="IsNoncreatureManaValueBlocked"/> for non-creature cards).
    /// Removed when the source permanent leaves the battlefield via
    /// <see cref="RemoveNoncreatureManaValueBlock"/>.
    /// </summary>
    public static void AddNoncreatureManaValueBlock(object token, int manaValue)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (manaValue < 0) return;
        lock (_gate)
        {
            foreach (var entry in _noncreatureManaValueBlocks)
            {
                if (ReferenceEquals(entry.Token, token) && entry.ManaValue == manaValue)
                {
                    return;
                }
            }
            _noncreatureManaValueBlocks.Add((token, manaValue));
        }
    }

    /// <summary>
    /// Remove every noncreature-mana-value block registered under
    /// <paramref name="token"/>. Used when the Sanctum Prelate (or similar
    /// source) leaves the battlefield. Scoped by token, so removing one
    /// source does not tear down blocks contributed by other sources.
    /// </summary>
    public static void RemoveNoncreatureManaValueBlock(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _noncreatureManaValueBlocks.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered block currently prevents casting a
    /// noncreature spell with mana value <paramref name="manaValue"/>
    /// (CR 601.3 — Sanctum Prelate). The caller (<see cref="ActionValidator"/>)
    /// is responsible for gating this check to noncreature spells.
    /// </summary>
    public static bool IsNoncreatureManaValueBlocked(int manaValue)
    {
        lock (_gate)
        {
            foreach (var entry in _noncreatureManaValueBlocks)
            {
                if (entry.ManaValue == manaValue) return true;
            }
            return false;
        }
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

    /// <summary>
    /// Register a "<paramref name="player"/> can't cast spells at all"
    /// restriction (CR 601.3 — Voice of Victory / Grand Abolisher's "Your
    /// opponents can't cast spells during your turn"), keyed by
    /// <paramref name="token"/>. Idempotent for the same (token, player)
    /// pair. The "during your turn" window is managed by the caller: the
    /// source registers each opponent at the start of its controller's turn
    /// and tears the entries down at end of turn via
    /// <see cref="RemoveCannotCastAnySpell"/>, so any registered entry is an
    /// active block. <see cref="ActionValidator.ValidateCastSpell"/> consults
    /// <see cref="CannotCastAnySpell"/> and rejects every cast — creature and
    /// noncreature alike (distinct from the noncreature-only Ranger-Captain
    /// rail above).
    /// </summary>
    public static void AddCannotCastAnySpell(object token, Player player)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate)
        {
            foreach (var entry in _cannotCastAnySpell)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Player, player))
                {
                    return;
                }
            }
            _cannotCastAnySpell.Add((token, player));
        }
    }

    /// <summary>
    /// Remove every total cast block registered under
    /// <paramref name="token"/> (across all players). Called when the
    /// controller's turn ends (the "during your turn" window closes) or when
    /// the source leaves the battlefield.
    /// </summary>
    public static void RemoveCannotCastAnySpell(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_gate)
        {
            _cannotCastAnySpell.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered total cast block currently prevents
    /// <paramref name="player"/> from casting any spell (CR 601.3 — Voice of
    /// Victory / Grand Abolisher shape). Consulted by
    /// <see cref="ActionValidator.ValidateCastSpell"/>.
    /// </summary>
    public static bool CannotCastAnySpell(Player player)
    {
        if (player == null) return false;
        lock (_gate)
        {
            foreach (var entry in _cannotCastAnySpell)
            {
                if (ReferenceEquals(entry.Player, player)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Register a turn-scoped "you can cast only N more spell(s) this turn"
    /// cap (CR 601.3 — Irencrag Feat: "You can cast only one more spell this
    /// turn."). The <paramref name="remaining"/> value is the number of
    /// ADDITIONAL spells the player may still cast after this registration
    /// (Irencrag Feat passes 1). If an entry for <paramref name="player"/>
    /// already exists, the LOWER of the existing and incoming values is kept
    /// so multiple caps compose correctly (the tighter restriction wins).
    /// <see cref="ConsumeAdditionalSpellAllowance"/> is called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> after each successful cast
    /// to decrement the counter. <see cref="ActionValidator"/> rejects the
    /// cast when the counter reaches zero.
    /// </summary>
    public static void SetMaxAdditionalSpellsThisTurn(Player player, int remaining)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (remaining < 0) remaining = 0;
        lock (_gate)
        {
            if (_maxAdditionalSpells.TryGetValue(player.Id, out var existing))
            {
                _maxAdditionalSpells[player.Id] = Math.Min(existing, remaining);
            }
            else
            {
                _maxAdditionalSpells[player.Id] = remaining;
            }
        }
    }

    /// <summary>
    /// True if <paramref name="player"/> is currently barred from casting
    /// another spell because their turn-scoped spell-count cap has been
    /// exhausted (counter == 0). Returns false (no restriction) when no cap
    /// is registered for this player. Consulted by
    /// <see cref="ActionValidator.ValidateCastSpell"/>.
    /// </summary>
    public static bool HasExhaustedAdditionalSpellAllowance(Player player)
    {
        if (player == null) return false;
        lock (_gate)
        {
            return _maxAdditionalSpells.TryGetValue(player.Id, out var v) && v <= 0;
        }
    }

    /// <summary>
    /// Decrement the per-player additional-spell counter by one (floor 0).
    /// Called by <see cref="Majik.Core.Game.SpellCastFlow"/> immediately after
    /// a successful cast to track progress toward the cap. No-op when no cap
    /// is registered for <paramref name="player"/>.
    /// </summary>
    public static void ConsumeAdditionalSpellAllowance(Player player)
    {
        if (player == null) return;
        lock (_gate)
        {
            if (_maxAdditionalSpells.TryGetValue(player.Id, out var v))
            {
                _maxAdditionalSpells[player.Id] = Math.Max(0, v - 1);
            }
        }
    }

    /// <summary>
    /// Clear the turn-scoped additional-spell cap for all players. Called at
    /// end of turn / cleanup; tests may also call this directly via
    /// <see cref="Clear"/>.
    /// </summary>
    public static void ClearMaxAdditionalSpellsThisTurn()
    {
        lock (_gate) _maxAdditionalSpells.Clear();
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
            _noncreatureManaValueBlocks.Clear();
            _globalCastZoneBlocks.Clear();
            _cannotCastAnySpell.Clear();
            _nextSpellUncounterable.Clear();
            _maxAdditionalSpells.Clear();
        }
    }
}

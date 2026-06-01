using Majik.Core.Game;
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
/// <para>
/// The backing state is <b>not</b> a single process-global static. It lives
/// in an <see cref="AmbientRegistryStore{TStore}"/> scoped per-game via
/// <see cref="GameRegistryScope.PushForGame"/> (installed at game start in
/// <c>GameDriver.RunGameAsync</c> / <c>GameFacade</c>, mirroring the four
/// player-keyed registries and the logical clock). Concurrent matches in one
/// process see independent restriction state — token-/turn-scoped rails no
/// longer leak across games — and a finished match's entries are reclaimed
/// when its scope ends. Outside any game scope (direct-construction unit
/// tests) the static API resolves a process-wide fallback store, so the
/// existing call sites keep working unchanged.
/// </para>
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture/dispose path to avoid leakage across cases.
/// </summary>
public static class CastingRestrictions
{
    /// <summary>
    /// Per-game store: every restriction rail plus the lock that guards
    /// them. Replaces the former bag of process-global statics; one instance
    /// is minted per game scope (and one process-wide fallback backs
    /// direct-construction call sites).
    /// </summary>
    public sealed class Store
    {
        // Each entry: (token, player). A player is restricted while at least
        // one entry targeting them exists.
        internal readonly List<(object Token, Player Player)> SorcerySpeed = new();
        // "Spells <player> controls can't be countered" turn-scoped rider
        // (Veil of Summer). Stored as a flat set of player IDs; cleared at
        // end of turn by the caller (or via Clear in tests).
        internal readonly HashSet<Guid> UncounterableControllers = new();
        // CR 113.6 — "<player> can't cast spells from anywhere other than
        // their hand" (Drannith Magistrate, Aven Mindcensor, Ethersworn
        // Canonist's cousin). Same (token, player) shape as the sorcery-
        // speed list so multiple sources can stack without trampling.
        internal readonly List<(object Token, Player Player)> CastFromHandOnly = new();
        // CR 601.3 — "<named card> can't be cast" (Meddling Mage). Stored as
        // (token, cardName) entries; a name is blocked while at least one entry
        // targeting it exists.
        internal readonly List<(object Token, string Name)> NamedCardBlocks = new();
        // CR 601.3 — per-player named-card block (Reflector Mage: "That player
        // can't cast spells with the same name as that creature until your
        // next turn"). Stored as (token, playerId, cardName); a name is
        // blocked for a player while at least one entry matching their id +
        // that name exists. Distinct from the global NamedCardBlocks rail
        // above (which gates the name for every player — Meddling Mage's
        // shape) so the two surfaces compose without trampling each other.
        internal readonly List<(object Token, Guid PlayerId, string Name)> NamedCardBlocksByPlayer = new();
        // CR 601.3 — turn-scoped "<player> can't cast noncreature spells this
        // turn" rider (Ranger-Captain of Eos's sacrifice ability). Stored as a
        // flat set of player IDs; cleared by the caller (or via Clear in
        // tests). Same lifecycle posture as the turn-scoped uncounterable
        // rider.
        internal readonly HashSet<Guid> NoncreatureRestrictedPlayers = new();
        // CR 601.3 — "noncreature spells with mana value N can't be cast" rail
        // (Sanctum Prelate: "Noncreature spells with mana value equal to the
        // chosen number can't be cast."). Stored as (token, manaValue) entries;
        // a mana value is blocked for noncreature spells while at least one entry
        // targeting it exists. Symmetric — applies to every player's noncreature
        // spells (same global posture as NamedCardBlocks, but keyed on mana
        // value and gated to noncreature spells by ActionValidator). Distinct
        // from the per-player turn-scoped NoncreatureRestrictedPlayers rail
        // above (Ranger-Captain), which blocks ALL noncreature spells for a
        // specific player; this rail blocks a SINGLE mana value for everyone.
        internal readonly List<(object Token, int ManaValue)> NoncreatureManaValueBlocks = new();
        // CR 601.3 — global "no player may cast spells from this zone" rail
        // (Grafdigger's Cage: "Players can't cast spells from graveyards or
        // libraries."). Stored as (token, zone); a zone is blocked for every
        // player while at least one entry targeting it exists. Distinct from
        // CastFromHandOnly, which is per-player and inverts the gate (allow
        // Hand only); this rail blocklists specific zones for everyone.
        internal readonly List<(object Token, ZoneType Zone)> GlobalCastZoneBlocks = new();
        // CR 601.3 — "<player> can't cast spells" total cast block, keyed by
        // source token (Voice of Victory / Grand Abolisher: "Your opponents
        // can't cast spells during your turn."). Same (token, player) shape as
        // the sorcery-speed list so multiple sources stack without trampling.
        // The "during your turn" gating is the caller's responsibility — the
        // source registers an entry per opponent when its controller's turn
        // begins and removes them (via RemoveCannotCastAnySpell) when the turn
        // ends, so a registered entry already means the block is active.
        internal readonly List<(object Token, Player Player)> CannotCastAnySpellEntries = new();
        // CR 701.5b — "The next spell you cast this turn can't be countered."
        // One-shot per-player flag: consumed (cleared) on the first spell the
        // registered player casts. Distinct from the all-turn uncounterable rail
        // (UncounterableControllers) used by Veil of Summer — Mistrise Village's
        // oracle is explicitly "the next spell", not "spells you control this
        // turn". SpellCastFlow consumes this flag at cast-time via
        // ConsumeNextSpellUncounterableForTurn and stamps
        // Spell.CannotBeCountered when it fires.
        internal readonly HashSet<Guid> NextSpellUncounterable = new();
        // CR 601.3 — "You can cast only N more spell(s) this turn" cap (Irencrag
        // Feat: "You can cast only one more spell this turn."). Stored as a per-
        // player remaining-spells counter; a player with a registered entry of 0
        // is blocked from casting further spells this turn. SpellCastFlow
        // decrements the counter after each successful cast via
        // ConsumeAdditionalSpellAllowance. Cleared by the caller at end of turn
        // or via Clear in tests.
        internal readonly Dictionary<Guid, int> MaxAdditionalSpells = new();
        internal readonly object Gate = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>
    /// Install a fresh per-game store as the ambient store for the current
    /// async flow until the returned scope is disposed. Used at game start so
    /// concurrent matches are isolated. See <see cref="GameRegistryScope"/>.
    /// </summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>
    /// Register a sorcery-speed restriction on <paramref name="player"/>,
    /// keyed by <paramref name="token"/>. Idempotent for the same (token,
    /// player) pair — re-registering does not add a second entry.
    /// </summary>
    public static void AddSorcerySpeedRestriction(object token, Player player)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(player);
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.SorcerySpeed)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Player, player))
                {
                    return;
                }
            }
            store.SorcerySpeed.Add((token, player));
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
        var store = Current;
        lock (store.Gate)
        {
            store.SorcerySpeed.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered restriction currently requires
    /// <paramref name="player"/> to cast spells only at sorcery speed.
    /// </summary>
    public static bool MustCastAtSorcerySpeed(Player player)
    {
        if (player == null) return false;
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.SorcerySpeed)
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
        var store = Current;
        lock (store.Gate) store.UncounterableControllers.Add(player.Id);
    }

    /// <summary>
    /// True if a turn-scoped "spells this player controls can't be
    /// countered" rider is active for <paramref name="player"/>.
    /// </summary>
    public static bool SpellsCannotBeCountered(Player player)
    {
        if (player == null) return false;
        var store = Current;
        lock (store.Gate) return store.UncounterableControllers.Contains(player.Id);
    }

    /// <summary>
    /// Clear the turn-scoped uncounterable set. Called at end of turn /
    /// cleanup; tests may also call this directly via <see cref="Clear"/>.
    /// </summary>
    public static void ClearUncounterableForTurn()
    {
        var store = Current;
        lock (store.Gate) store.UncounterableControllers.Clear();
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
        var store = Current;
        lock (store.Gate) store.NextSpellUncounterable.Add(player.Id);
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
        var store = Current;
        lock (store.Gate)
        {
            if (!store.NextSpellUncounterable.Contains(player.Id)) return false;
            store.NextSpellUncounterable.Remove(player.Id);
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.CastFromHandOnly)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Player, player))
                {
                    return;
                }
            }
            store.CastFromHandOnly.Add((token, player));
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
        var store = Current;
        lock (store.Gate)
        {
            store.CastFromHandOnly.RemoveAll(e => ReferenceEquals(e.Token, token));
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.CastFromHandOnly)
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.NamedCardBlocks)
            {
                if (ReferenceEquals(entry.Token, token)
                    && string.Equals(entry.Name, cardName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            store.NamedCardBlocks.Add((token, cardName));
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
        var store = Current;
        lock (store.Gate)
        {
            store.NamedCardBlocks.RemoveAll(e => ReferenceEquals(e.Token, token));
            store.NamedCardBlocksByPlayer.RemoveAll(e => ReferenceEquals(e.Token, token));
        }
    }

    /// <summary>
    /// True if at least one registered block currently prevents casting a
    /// spell named <paramref name="cardName"/> (CR 601.3).
    /// </summary>
    public static bool IsCardNameBlocked(string cardName)
    {
        if (string.IsNullOrEmpty(cardName)) return false;
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.NamedCardBlocks)
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.NamedCardBlocksByPlayer)
            {
                if (ReferenceEquals(entry.Token, token)
                    && entry.PlayerId == player.Id
                    && string.Equals(entry.Name, cardName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            store.NamedCardBlocksByPlayer.Add((token, player.Id, cardName));
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.NamedCardBlocksByPlayer)
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
        var store = Current;
        lock (store.Gate) store.NoncreatureRestrictedPlayers.Add(player.Id);
    }

    /// <summary>
    /// True if a turn-scoped noncreature-spell restriction is currently
    /// active against <paramref name="player"/> (CR 601.3 — Ranger-Captain
    /// of Eos). Consulted by <see cref="ActionValidator.ValidateCastSpell"/>.
    /// </summary>
    public static bool CannotCastNoncreatureSpell(Player player)
    {
        if (player == null) return false;
        var store = Current;
        lock (store.Gate) return store.NoncreatureRestrictedPlayers.Contains(player.Id);
    }

    /// <summary>
    /// Clear the turn-scoped noncreature-spell restriction set. Called at
    /// end of turn / cleanup; tests may also call this directly via
    /// <see cref="Clear"/>.
    /// </summary>
    public static void ClearNoncreatureRestrictionForTurn()
    {
        var store = Current;
        lock (store.Gate) store.NoncreatureRestrictedPlayers.Clear();
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.NoncreatureManaValueBlocks)
            {
                if (ReferenceEquals(entry.Token, token) && entry.ManaValue == manaValue)
                {
                    return;
                }
            }
            store.NoncreatureManaValueBlocks.Add((token, manaValue));
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
        var store = Current;
        lock (store.Gate)
        {
            store.NoncreatureManaValueBlocks.RemoveAll(e => ReferenceEquals(e.Token, token));
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.NoncreatureManaValueBlocks)
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.GlobalCastZoneBlocks)
            {
                if (ReferenceEquals(entry.Token, token) && entry.Zone == zone)
                {
                    return;
                }
            }
            store.GlobalCastZoneBlocks.Add((token, zone));
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
        var store = Current;
        lock (store.Gate)
        {
            store.GlobalCastZoneBlocks.RemoveAll(e => ReferenceEquals(e.Token, token));
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.GlobalCastZoneBlocks)
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.CannotCastAnySpellEntries)
            {
                if (ReferenceEquals(entry.Token, token)
                    && ReferenceEquals(entry.Player, player))
                {
                    return;
                }
            }
            store.CannotCastAnySpellEntries.Add((token, player));
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
        var store = Current;
        lock (store.Gate)
        {
            store.CannotCastAnySpellEntries.RemoveAll(e => ReferenceEquals(e.Token, token));
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
        var store = Current;
        lock (store.Gate)
        {
            foreach (var entry in store.CannotCastAnySpellEntries)
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
        var store = Current;
        lock (store.Gate)
        {
            if (store.MaxAdditionalSpells.TryGetValue(player.Id, out var existing))
            {
                store.MaxAdditionalSpells[player.Id] = Math.Min(existing, remaining);
            }
            else
            {
                store.MaxAdditionalSpells[player.Id] = remaining;
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
        var store = Current;
        lock (store.Gate)
        {
            return store.MaxAdditionalSpells.TryGetValue(player.Id, out var v) && v <= 0;
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
        var store = Current;
        lock (store.Gate)
        {
            if (store.MaxAdditionalSpells.TryGetValue(player.Id, out var v))
            {
                store.MaxAdditionalSpells[player.Id] = Math.Max(0, v - 1);
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
        var store = Current;
        lock (store.Gate) store.MaxAdditionalSpells.Clear();
    }

    /// <summary>Reset the active store. Test-only.</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Gate)
        {
            store.SorcerySpeed.Clear();
            store.UncounterableControllers.Clear();
            store.CastFromHandOnly.Clear();
            store.NamedCardBlocks.Clear();
            store.NamedCardBlocksByPlayer.Clear();
            store.NoncreatureRestrictedPlayers.Clear();
            store.NoncreatureManaValueBlocks.Clear();
            store.GlobalCastZoneBlocks.Clear();
            store.CannotCastAnySpellEntries.Clear();
            store.NextSpellUncounterable.Clear();
            store.MaxAdditionalSpells.Clear();
        }
    }
}

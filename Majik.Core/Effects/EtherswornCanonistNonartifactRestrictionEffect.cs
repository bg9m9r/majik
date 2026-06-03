using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Lifecycle binder for Ethersworn Canonist's printed static
/// (CR 605/616 / CR 601.3):
///   "Each player who has cast a nonartifact spell this turn can't cast
///    additional nonartifact spells."
///
/// Two cooperating pieces back this restriction, both living on the per-game
/// <see cref="CastingRestrictions"/> store:
/// <list type="number">
///   <item>An always-on per-player counter of NONARTIFACT spells cast this
///         turn (<see cref="CastingRestrictions.RecordNonartifactSpellCast"/>),
///         incremented by <see cref="Majik.Core.Game.SpellCastFlow"/> on every
///         nonartifact cast. This is the "has cast a nonartifact spell this
///         turn" looked-back state — tracked unconditionally so a Canonist
///         entering mid-turn still sees who already cast one
///         (CR 608.2 — a static gate reads the live game state).</item>
///   <item>A battlefield-gated SYMMETRIC active flag this binder installs: while
///         the Canonist is on the battlefield, an entry is registered for every
///         player via
///         <see cref="CastingRestrictions.AddCanonistNonartifactRestriction"/>.
///         </item>
/// </list>
///
/// <see cref="Majik.Core.Rules.ActionValidator"/> rejects a NONARTIFACT
/// <c>CastSpellAction</c> only when BOTH the active flag is registered for the
/// caster AND the caster has already cast a nonartifact spell this turn
/// (<see cref="CastingRestrictions.IsRestrictedByCanonistNonartifact"/>). An
/// ARTIFACT spell is never restricted by this rail (CR 605/616) — the caster's
/// own artifact spells stay castable, and casting one does NOT increment the
/// nonartifact counter.
///
/// ## Per-turn reset (CR 514.2)
/// The "this turn" tally refreshes each turn, so this binder subscribes to
/// <see cref="TurnStartedEvent"/> and clears the per-player nonartifact-cast
/// counter at every turn start (only while the Canonist is on the
/// battlefield — when no Canonist is out the counter is inert).
///
/// ## Symmetry (CR 109.5 — "Each player")
/// The static is symmetric: it applies to the Canonist's controller as well as
/// their opponents. The all-players resolver supplies the full player list.
///
/// ## Lifecycle (ETB / LTB)
/// Mirrors <see cref="ArchonOfEmeriaOneSpellPerTurnEffect"/>: register on
/// Attach, re-sync on every zone move of the source, tear the entries down when
/// the Canonist leaves the battlefield so the restriction lifts automatically.
/// </summary>
public sealed class EtherswornCanonistNonartifactRestrictionEffect
{
    private readonly ICard _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<IReadOnlyList<Player>> _allPlayersResolver;
    private readonly object _token = new();
    private readonly Action<TurnStartedEvent> _onTurnStarted;
    private readonly Action<CardMovedEvent> _onCardMoved;
    private bool _attached;
    private bool _active;

    /// <param name="source">The Ethersworn Canonist permanent gating the
    /// static. The restriction is only enforced while the source is on the
    /// battlefield.</param>
    /// <param name="eventBus">Event bus for per-turn reset
    /// (<see cref="TurnStartedEvent"/>) and ETB/LTB tracking
    /// (<see cref="CardMovedEvent"/>). May be null — Attach still installs the
    /// active flag once, but the per-turn counter reset relies on the
    /// bus.</param>
    /// <param name="allPlayersResolver">Returns every player in the game (the
    /// Canonist's controller included — the static is symmetric, CR 109.5). May
    /// not be null.</param>
    public EtherswornCanonistNonartifactRestrictionEffect(
        ICard source,
        IEventBus? eventBus,
        Func<IReadOnlyList<Player>> allPlayersResolver)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eventBus = eventBus;
        _allPlayersResolver = allPlayersResolver
            ?? throw new ArgumentNullException(nameof(allPlayersResolver));
        _onTurnStarted = OnTurnStarted;
        _onCardMoved = OnCardMoved;
    }

    /// <summary>True while the restriction is currently registered (source on
    /// the battlefield).</summary>
    public bool IsActive => _active;

    /// <summary>
    /// Subscribe to turn-start + zone-move events and install the active flag
    /// if the source is already on the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;

        if (_eventBus != null)
        {
            _eventBus.Subscribe(_onTurnStarted);
            _eventBus.Subscribe(_onCardMoved);
        }

        Sync();
    }

    private void OnCardMoved(CardMovedEvent e)
    {
        if (!ReferenceEquals(e.Card, _source)) return;
        Sync();
    }

    private void OnTurnStarted(TurnStartedEvent _)
    {
        // CR 514.2 — the per-turn nonartifact-cast tally refreshes each turn.
        // Only while the Canonist is on the battlefield (when no Canonist is
        // out the counter is inert and clearing it is harmless either way).
        if (_source.Zone != ZoneType.Battlefield) return;
        CastingRestrictions.ClearNonartifactSpellsCastThisTurn();
    }

    private void Sync()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;

        if (shouldBeActive && !_active)
        {
            _active = true;
            RegisterForAllPlayers();
        }
        else if (!shouldBeActive && _active)
        {
            _active = false;
            // Canonist left the battlefield — lift the restriction (CR 611.2g
            // static stops applying). Remove only this source's entries.
            CastingRestrictions.RemoveCanonistNonartifactRestriction(_token);
        }
    }

    private void RegisterForAllPlayers()
    {
        var players = _allPlayersResolver();
        if (players is null) return;

        foreach (var player in players)
        {
            if (player is null) continue;
            CastingRestrictions.AddCanonistNonartifactRestriction(_token, player);
        }
    }
}

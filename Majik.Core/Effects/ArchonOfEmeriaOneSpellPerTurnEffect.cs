using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Lifecycle binder for Archon of Emeria's printed static (CR 604.3 /
/// CR 601.3):
///   "Each player can't cast more than one spell each turn."
///
/// While the source permanent (Archon of Emeria) is on the battlefield, every
/// player's turn-scoped additional-spell cap is set to <c>1</c> via
/// <see cref="CastingRestrictions.SetMaxAdditionalSpellsThisTurn"/>. That rail
/// is the same one Irencrag Feat uses for "you can cast only one more spell
/// this turn": <see cref="Majik.Core.Game.SpellCastFlow"/> decrements each
/// player's counter after every successful cast via
/// <see cref="CastingRestrictions.ConsumeAdditionalSpellAllowance"/>, and
/// <see cref="ActionValidator.ValidateCastSpell"/> rejects the cast once the
/// counter reaches zero. So with the cap seeded at 1, the first spell a player
/// casts in a turn is allowed and every subsequent one is blocked (CR 601.3).
///
/// ## Per-turn reset (CR 514.2)
/// The cap is "each turn", so it must be re-seeded at the start of every turn.
/// This binder subscribes to <see cref="TurnStartedEvent"/> and, on each turn
/// start, clears the consumed counters and re-seeds every player to 1.
///
/// Reset uses <see cref="CastingRestrictions.ClearMaxAdditionalSpellsThisTurn"/>
/// before re-seeding because the underlying rail keeps the TIGHTER of an
/// existing and an incoming cap (the <c>Math.Min</c> in
/// <see cref="CastingRestrictions.SetMaxAdditionalSpellsThisTurn"/>) — without
/// a clear, a counter consumed to 0 on the previous turn would stay pinned at 0
/// and Archon would lock the player out entirely. Clearing at the very start of
/// the turn is safe: no other source (e.g. Irencrag Feat, which resolves later
/// in the turn) has registered a cap yet at that instant. The first re-seed
/// also happens immediately on <see cref="Attach"/> so the static is live the
/// moment Archon enters mid-turn.
///
/// ## Symmetry (CR 109.5 — "Each player")
/// The static is symmetric: it applies to Archon's controller as well as their
/// opponents. The all-players resolver supplies the full player list.
///
/// ## Lifecycle (ETB / LTB)
/// Mirrors <see cref="ThaliaHereticCatharEntersTappedEffect"/> /
/// Narset's draw-restriction lifecycle: register on Attach, re-sync on every
/// zone move of the source, tear the caps down when Archon leaves the
/// battlefield so the restriction lifts automatically.
/// </summary>
public sealed class ArchonOfEmeriaOneSpellPerTurnEffect
{
    /// <summary>
    /// "Each player can't cast more than ONE spell each turn." The cap is the
    /// number of spells a player may still cast this turn (CR 601.3).
    /// </summary>
    public const int SpellsPerTurnCap = 1;

    private readonly ICard _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<IReadOnlyList<Player>> _allPlayersResolver;
    private readonly Action<TurnStartedEvent> _onTurnStarted;
    private readonly Action<CardMovedEvent> _onCardMoved;
    private bool _attached;
    private bool _active;

    /// <param name="source">The Archon permanent gating the static. The cap
    /// is only enforced while the source is on the battlefield.</param>
    /// <param name="eventBus">Event bus for per-turn reset
    /// (<see cref="TurnStartedEvent"/>) and ETB/LTB tracking
    /// (<see cref="CardMovedEvent"/>). May be null — Attach still seeds the cap
    /// once, but the per-turn reset relies on the bus.</param>
    /// <param name="allPlayersResolver">Returns every player in the game
    /// (Archon's controller included — the static is symmetric, CR 109.5).
    /// May not be null.</param>
    public ArchonOfEmeriaOneSpellPerTurnEffect(
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

    /// <summary>True while the cap is currently being enforced (source on the
    /// battlefield).</summary>
    public bool IsActive => _active;

    /// <summary>
    /// Subscribe to turn-start + zone-move events and seed the cap if the
    /// source is already on the battlefield. Idempotent.
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
        // Re-seed every player's cap at turn start (CR 514.2 — the "each turn"
        // allowance refreshes). Only while Archon is out.
        if (_source.Zone != ZoneType.Battlefield) return;
        ReseedCaps();
    }

    private void Sync()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;

        if (shouldBeActive && !_active)
        {
            _active = true;
            ReseedCaps();
        }
        else if (!shouldBeActive && _active)
        {
            _active = false;
            // Archon left the battlefield — lift the restriction (CR 614 /
            // 611.2g static stops applying). Clearing the rail removes the
            // per-player caps this source installed.
            CastingRestrictions.ClearMaxAdditionalSpellsThisTurn();
        }
    }

    private void ReseedCaps()
    {
        // Clear first so a counter consumed to 0 on a prior turn can be raised
        // back to the cap — SetMaxAdditionalSpellsThisTurn keeps the tighter
        // (Math.Min) value, so a bare Set could not lift a 0 back to 1.
        CastingRestrictions.ClearMaxAdditionalSpellsThisTurn();

        var players = _allPlayersResolver();
        if (players is null) return;

        foreach (var player in players)
        {
            if (player is null) continue;
            CastingRestrictions.SetMaxAdditionalSpellsThisTurn(player, SpellsPerTurnCap);
        }
    }
}

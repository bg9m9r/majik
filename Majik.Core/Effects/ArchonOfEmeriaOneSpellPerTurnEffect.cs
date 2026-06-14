using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Lifecycle binder for the printed static shared by Archon of Emeria and
/// Eidolon of Rhetoric (CR 601.3 / 611):
///   "Each player can't cast more than one spell each turn."
///
/// While the source permanent is on the battlefield, every player gets a
/// token-scoped spells-per-turn cap entry of <c>1</c> on the
/// <see cref="CastingRestrictions"/> static-cap rail
/// (<see cref="CastingRestrictions.AddSpellsPerTurnCap"/>). The cap is a TRUE
/// static (CR 611): it reads an explicit per-player spells-cast-this-turn
/// counter (<see cref="CastingRestrictions.SpellsCastThisTurn"/>, incremented by
/// <see cref="Majik.Core.Game.SpellCastFlow"/> on every cast) and is never
/// consumed. <see cref="ActionValidator.ValidateCastSpell"/> rejects a cast once
/// <see cref="CastingRestrictions.IsAtSpellsPerTurnCap"/> reports the player has
/// reached the cap.
///
/// ## Why a dedicated rail (deferral pay-down)
/// This static used to ride the SAME consumable
/// <c>MaxAdditionalSpells</c> ledger that Irencrag Feat ("you can cast only one
/// more spell this turn") uses, and re-seeded it by CLEARING the whole shared
/// dictionary at every turn start. That coupling created the
/// <c>eidolon-archon-shared-cap-turn-start-reseed</c> race: the static-cap
/// turn-start reseed could wipe a same-turn Irencrag-Feat allowance (and vice
/// versa). The two effects now live on SEPARATE ledgers — the static cap reads
/// its own explicit spells-cast counter and never touches the Feat's consumable
/// allowance — so the reset point and the extra-cast grant can no longer race.
///
/// ## Per-turn reset (CR 514/500 turn boundary)
/// Because the cap is a true static reading the spells-cast counter, the ONLY
/// turn-boundary action needed is clearing that counter — done here on
/// <see cref="TurnStartedEvent"/>. The cap entries themselves persist while the
/// source stays on the battlefield; there is no entry re-seed and, critically,
/// no clear of any shared field that another effect might own.
///
/// ## Symmetry (CR 109.5 — "Each player")
/// The static is symmetric: it applies to the source's controller as well as
/// their opponents. The all-players resolver supplies the full player list.
///
/// ## Lifecycle (ETB / LTB)
/// Mirrors <see cref="EtherswornCanonistNonartifactRestrictionEffect"/>:
/// register an entry per player on Attach / on every zone move of the source
/// while it is on the battlefield, and tear this source's entries down (scoped
/// by token) when it leaves so the restriction lifts automatically.
/// </summary>
public sealed class ArchonOfEmeriaOneSpellPerTurnEffect
{
    /// <summary>
    /// "Each player can't cast more than ONE spell each turn." The cap is the
    /// maximum number of spells a player may cast this turn (CR 601.3).
    /// </summary>
    public const int SpellsPerTurnCap = 1;

    private readonly ICard _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<IReadOnlyList<Player>> _allPlayersResolver;
    private readonly object _token = new();
    private readonly Action<TurnStartedEvent> _onTurnStarted;
    private readonly Action<CardMovedEvent> _onCardMoved;
    private bool _attached;
    private bool _active;

    /// <param name="source">The permanent gating the static (Archon of Emeria /
    /// Eidolon of Rhetoric). The cap is only enforced while the source is on the
    /// battlefield.</param>
    /// <param name="eventBus">Event bus for per-turn reset
    /// (<see cref="TurnStartedEvent"/>) and ETB/LTB tracking
    /// (<see cref="CardMovedEvent"/>). May be null — Attach still seeds the cap
    /// once, but the per-turn counter reset relies on the bus.</param>
    /// <param name="allPlayersResolver">Returns every player in the game
    /// (the source's controller included — the static is symmetric, CR 109.5).
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
        // CR 514/500 — the per-turn spells-cast tally refreshes each turn. The
        // static cap (a true static reading this counter) needs nothing else:
        // its entries persist while the source is out. Only clear while the
        // source is on the battlefield (when none is out the counter is inert).
        if (_source.Zone != ZoneType.Battlefield) return;
        CastingRestrictions.ClearSpellsCastThisTurn();
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
            // Source left the battlefield — lift the restriction (CR 611.2g —
            // the static stops applying). Remove only this source's entries so a
            // second cap source (another Archon / an Eidolon) is untouched.
            CastingRestrictions.RemoveSpellsPerTurnCap(_token);
        }
    }

    private void RegisterForAllPlayers()
    {
        var players = _allPlayersResolver();
        if (players is null) return;

        foreach (var player in players)
        {
            if (player is null) continue;
            CastingRestrictions.AddSpellsPerTurnCap(_token, player, SpellsPerTurnCap);
        }
    }
}

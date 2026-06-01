using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spirit of the Labyrinth (Born of the Gods,
/// {1}{W}). Enchantment Creature — Spirit, 3/1.
///
/// Oracle text (verified against Scryfall):
///   "Each player can't draw more than one card each turn."
///
/// This is the <b>symmetric sibling</b> of Narset, Parter of Veils'
/// printed static (CR 117.1a): Narset caps only opponents, Spirit's
/// "each player" caps every player — its own controller included — at one
/// draw per turn. It reuses the same engine primitive (no new infra): a
/// <see cref="DrawCardIntent"/> replacement (CR 614) registered on each
/// affected player's <see cref="ReplacementBus"/>, reset on
/// <see cref="TurnStartedEvent"/>.
///
/// The base shape (name, Creature+Enchantment types, Spirit subtype,
/// {1}{W}, 3/1) is materialised from the embedded JSON definition
/// (<c>spirit-of-the-labyrinth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed static is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express draw-cap statics (same posture as
/// <see cref="StormscaleScionFactory"/> and
/// <see cref="NarsetParterOfVeilsFactory"/>, whose statics also live in
/// the factory).
///
/// ## Implemented (v1)
/// - Enchantment Creature 3/1 with Spirit subtype, mana cost {1}{W}.
/// - <b>Printed static (CR 117.1a)</b>: "Each player can't draw more than
///   one card each turn." Wired via
///   <see cref="SpiritDrawRestrictionReplacement"/> registered on EVERY
///   affected player's <see cref="ReplacementBus"/> while Spirit is on the
///   battlefield. The replacement tracks how many times that player has
///   drawn this turn — the first <see cref="DrawCardIntent"/> per turn is
///   let through unchanged, subsequent draw intents are cancelled
///   (CR 614 — replacement returns null). Reset is driven by
///   <see cref="TurnStartedEvent"/> on the supplied <see cref="IEventBus"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Draw-watcher coverage</b>: the static gates on
///   <see cref="DrawCardIntent"/> — any draw path that bypasses
///   <see cref="ReplacementBus"/> also bypasses Spirit (same gap as
///   Narset / Sheoldred). Production draw paths route through
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/>, which DOES route
///   through the bus when one is attached.
/// </summary>
[CardName("Spirit of the Labyrinth")]
public static class SpiritOfTheLabyrinthFactory
{
    public const string CardName = "Spirit of the Labyrinth";
    public const string Slug = "spirit-of-the-labyrinth";
    public const int Power = 3;
    public const int Toughness = 1;
    public const int MaxDrawsPerTurn = 1;

    /// <summary>
    /// Construct Spirit of the Labyrinth with no live wiring. Shape /
    /// dispatcher posture — the printed static silently no-ops (no player
    /// resolver / bus). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, playerResolver: null, eventBus: null);

    /// <summary>
    /// Construct Spirit of the Labyrinth with the printed-static lifecycle
    /// wired against <paramref name="eventBus"/> and per-player
    /// <see cref="ReplacementBus"/> registration via
    /// <paramref name="playerResolver"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="playerResolver">Returns the set of players the
    /// restriction applies to. Per the printed "each player" this should be
    /// ALL players in the game (including Spirit's controller). Each player
    /// must have a non-null <see cref="Player.Replacements"/> bus for the
    /// restriction to take effect; players without a bus are silently
    /// skipped. Called when Spirit enters the battlefield. May be null —
    /// restriction simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking of the printed
    /// static and for per-turn draw-counter reset. May be null — Attach
    /// will still sync once but per-turn reset relies on the bus.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? playerResolver,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name,
        // Creature+Enchantment, Spirit subtype, {1}{W}, 3/1). The JSON
        // carries no abilities — the printed static is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // -- Printed static (CR 117.1a) — "Each player can't draw more than
        //    one card each turn." ----------------------------------------
        if (playerResolver != null)
        {
            var lifecycle = new SpiritDrawRestrictionEffect(
                source: card,
                eventBus: eventBus,
                playerResolver: playerResolver);
            lifecycle.Attach();
        }

        return card;
    }
}

/// <summary>
/// Lifecycle binder for Spirit of the Labyrinth's printed static —
/// "Each player can't draw more than one card each turn."
///
/// While Spirit is on the battlefield, registers a
/// <see cref="SpiritDrawRestrictionReplacement"/> on EVERY affected
/// player's <see cref="ReplacementBus"/> (symmetric — controller
/// included). The replacement tracks draws-this-turn and cancels every
/// <see cref="DrawCardIntent"/> beyond the first per turn per player.
///
/// Per-turn reset is driven by <see cref="TurnStartedEvent"/> on the
/// supplied event bus. LTB unregisters every player registration. Mirrors
/// <c>NarsetDrawRestrictionEffect</c>; the only difference is the resolver
/// returns all players rather than opponents-only.
/// </summary>
public sealed class SpiritDrawRestrictionEffect
{
    private readonly ICard _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<IReadOnlyList<Player>> _playerResolver;
    private readonly Dictionary<Guid, (Player Player, SpiritDrawRestrictionReplacement Replacement)> _registered = new();
    private bool _attached;
    private bool _currentlyActive;

    public SpiritDrawRestrictionEffect(
        ICard source,
        IEventBus? eventBus,
        Func<IReadOnlyList<Player>> playerResolver)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eventBus = eventBus;
        _playerResolver = playerResolver ?? throw new ArgumentNullException(nameof(playerResolver));
    }

    /// <summary>Register the restriction on every affected player's bus if
    /// Spirit is on the battlefield. Subscribes to zone-change + turn-start
    /// events for lifecycle tracking. Idempotent.</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;

        if (_eventBus != null)
        {
            _eventBus.Subscribe<CardMovedEvent>(OnCardMoved);
            _eventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }
        SyncRegistration();
    }

    private void OnCardMoved(CardMovedEvent e)
    {
        if (!ReferenceEquals(e.Card, _source)) return;
        SyncRegistration();
    }

    private void OnTurnStarted(TurnStartedEvent _)
    {
        // Reset every registered player's draws-this-turn counter.
        foreach (var (_, entry) in _registered)
        {
            entry.Replacement.ResetDrawCount();
        }
    }

    private void SyncRegistration()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;

        if (shouldBeActive && !_currentlyActive)
        {
            var players = _playerResolver();
            if (players != null)
            {
                foreach (var player in players)
                {
                    if (player is null) continue;
                    if (player.Replacements is null) continue;
                    if (_registered.ContainsKey(player.Id)) continue;

                    var replacement = new SpiritDrawRestrictionReplacement(player);
                    player.Replacements.Register(replacement);
                    _registered[player.Id] = (player, replacement);
                }
            }
            _currentlyActive = true;
        }
        else if (!shouldBeActive && _currentlyActive)
        {
            foreach (var (_, entry) in _registered)
            {
                entry.Player.Replacements?.Unregister(entry.Replacement);
            }
            _registered.Clear();
            _currentlyActive = false;
        }
    }

    /// <summary>True while the restriction is registered against any
    /// player's bus.</summary>
    public bool IsRestrictionActive => _currentlyActive;
}

/// <summary>
/// Replacement effect for "Each player can't draw more than one card each
/// turn." Self-counts draws via this player's bus: the first
/// <see cref="DrawCardIntent"/> per turn passes through, subsequent ones
/// return null (cancel). <see cref="ResetDrawCount"/> is called at
/// turn-start by the owning lifecycle effect. Mirrors
/// <c>NarsetDrawRestrictionReplacement</c> — only the set of affected
/// players differs (symmetric "each player" vs opponents-only).
/// </summary>
public sealed class SpiritDrawRestrictionReplacement : IReplacementEffect<DrawCardIntent>
{
    private readonly Player _affected;
    private int _drawsThisTurn;

    public SpiritDrawRestrictionReplacement(Player affected)
    {
        _affected = affected ?? throw new ArgumentNullException(nameof(affected));
    }

    public bool OneShot => false;
    public object? Tag => null;

    public bool Applies(DrawCardIntent intent, IReadOnlyList<object> history)
    {
        if (intent is null) return false;
        if (!ReferenceEquals(intent.Player, _affected)) return false;
        return true;
    }

    public DrawCardIntent? Replace(DrawCardIntent intent, IReadOnlyList<object> history)
    {
        // First draw this turn passes through unchanged; subsequent draws
        // are cancelled (CR 614 — replacement returns null).
        if (_drawsThisTurn < SpiritOfTheLabyrinthFactory.MaxDrawsPerTurn)
        {
            _drawsThisTurn++;
            return intent;
        }
        _drawsThisTurn++;
        return null;
    }

    /// <summary>Reset the per-turn counter. Called by the owning lifecycle
    /// effect on every <see cref="TurnStartedEvent"/>.</summary>
    public void ResetDrawCount() => _drawsThisTurn = 0;

    /// <summary>Inspect the per-turn counter (for tests).</summary>
    public int DrawsThisTurn => _drawsThisTurn;
}

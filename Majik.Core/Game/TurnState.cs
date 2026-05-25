using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Game;

/// <summary>
/// Per-turn event tally. Reset at the start of each turn (Rule 800 series).
/// Cards and abilities consult this to enable conditional triggers and costs
/// (revolt — Rule 702.104, connive X, opponent-draw watchers, etc.).
///
/// Owned by <see cref="TurnDriver"/>; counters are incremented by subscribing
/// to <see cref="Majik.Core.Events.CardMovedEvent"/> and
/// <see cref="Majik.Core.Events.CardDrawnEvent"/> on the game's event bus.
/// </summary>
public sealed class TurnState
{
    /// <summary>Total number of creatures that died this turn (Rule 702.104b).</summary>
    public int CreaturesDiedThisTurn { get; private set; }

    /// <summary>Total number of permanents that left the battlefield this turn.</summary>
    public int PermanentsLeftBattlefieldThisTurn { get; private set; }

    private readonly Dictionary<Guid, int> _creaturesDiedByController = new();
    private readonly Dictionary<Guid, int> _permanentsLeftByController = new();
    private readonly Dictionary<Guid, int> _cardsDrawnByPlayer = new();

    // Per-player set of colours of spells they have cast this turn (CR 105).
    // Veil of Summer + similar "opponent has cast a {colour} spell this turn"
    // riders read this. A colourless spell (no entry) contributes no colours.
    private readonly Dictionary<Guid, HashSet<ManaColor>> _spellColorsCastByPlayer = new();

    // Per-player count of spells cast this turn (any colour, including
    // colourless). Damping Sphere's cost rider ("Each spell a player casts
    // costs {1} more to cast for each other spell that player has cast this
    // turn") reads this.
    private readonly Dictionary<Guid, int> _spellsCastByPlayer = new();

    // Per-player count of lands that have entered the battlefield under
    // their control this turn. Read by landfall-conditional spells
    // (Searing Blaze — CR 702.142 / "Whenever a land you control enters")
    // that need to ask the question "did a land enter under this player's
    // control this turn?" at spell resolution rather than via a printed
    // landfall trigger.
    private readonly Dictionary<Guid, int> _landsEnteredByController = new();

    // Per-player count of cards cycled this turn (CR 702.32). Driven by
    // CardCycledEvent subscribers (TurnDriver). Read by Hollow One's
    // self-cost-reduction reducer ("This spell costs {2} less to cast for
    // each card you've cycled or discarded this turn").
    private readonly Dictionary<Guid, int> _cyclesByPlayer = new();

    // Per-player count of cards discarded this turn (CR 701.16). Driven by
    // CardMovedEvent(Hand → Graveyard) subscribers (TurnDriver) — the
    // discarder is the moved card's owner. Read by Hollow One's
    // self-cost-reduction reducer alongside _cyclesByPlayer. Counts every
    // hand → graveyard move, regardless of source (player discard, opponent-
    // forced discard, "discard a card" cost payments — all qualify per CR
    // 701.16a).
    private readonly Dictionary<Guid, int> _discardsByPlayer = new();

    // Per-permanent set of references that entered the battlefield this turn.
    // Read by "creatures that entered the battlefield this turn" effects
    // (Force of Despair — CR 109.5 / CR 700.6) at spell resolution. Reference
    // equality (HashSet default) is the right comparer because the rule
    // text targets the SPECIFIC permanent that ETB'd this turn — a creature
    // that left + re-entered counts only for its newest ETB (cleared via
    // re-ETB; see RecordPermanentEnteredBattlefield).
    private readonly HashSet<Permanent> _permanentsEnteredThisTurn = new();

    /// <summary>
    /// How many creatures controlled by <paramref name="player"/> died this turn.
    /// </summary>
    public int CreaturesDiedByController(Player player) =>
        _creaturesDiedByController.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// How many permanents controlled by <paramref name="player"/> left the
    /// battlefield this turn (all permanent types, not just creatures).
    /// </summary>
    public int PermanentsLeftByController(Player player) =>
        _permanentsLeftByController.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// How many cards <paramref name="player"/> has drawn this turn.
    /// </summary>
    public int CardsDrawnByPlayer(Player player) =>
        _cardsDrawnByPlayer.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// Whether revolt is active for <paramref name="player"/> — i.e. at least one
    /// permanent they controlled left the battlefield this turn (Rule 702.104a).
    /// </summary>
    public bool RevoltActive(Player player) => PermanentsLeftByController(player) > 0;

    /// <summary>
    /// Called when a creature dies (moves to any zone from the battlefield
    /// while it has the Creature card type). Increments both the global
    /// counter and the per-controller bucket.
    /// </summary>
    public void RecordCreatureDied(Player? formerController)
    {
        CreaturesDiedThisTurn++;
        if (formerController != null)
        {
            _creaturesDiedByController[formerController.Id] =
                _creaturesDiedByController.GetValueOrDefault(formerController.Id) + 1;
        }
    }

    /// <summary>
    /// Called when any permanent leaves the battlefield (to any zone).
    /// Increments both the global counter and the per-controller bucket.
    /// </summary>
    public void RecordPermanentLeftBattlefield(Player? formerController)
    {
        PermanentsLeftBattlefieldThisTurn++;
        if (formerController != null)
        {
            _permanentsLeftByController[formerController.Id] =
                _permanentsLeftByController.GetValueOrDefault(formerController.Id) + 1;
        }
    }

    /// <summary>
    /// Called when a player draws a card.
    /// </summary>
    public void RecordCardDrawn(Player player)
    {
        _cardsDrawnByPlayer[player.Id] =
            _cardsDrawnByPlayer.GetValueOrDefault(player.Id) + 1;
    }

    /// <summary>
    /// Called when <paramref name="caster"/> casts a spell with the given
    /// <paramref name="colors"/> (CR 105). Read by "opponent cast a [colour]
    /// spell this turn" riders such as Veil of Summer.
    /// </summary>
    public void RecordSpellCast(Player caster, IReadOnlySet<ManaColor> colors)
    {
        if (caster == null) return;
        _spellsCastByPlayer[caster.Id] =
            _spellsCastByPlayer.GetValueOrDefault(caster.Id) + 1;
        if (colors == null || colors.Count == 0) return;
        if (!_spellColorsCastByPlayer.TryGetValue(caster.Id, out var set))
        {
            set = new HashSet<ManaColor>();
            _spellColorsCastByPlayer[caster.Id] = set;
        }
        foreach (var c in colors) set.Add(c);
    }

    /// <summary>
    /// Called when a land enters the battlefield under
    /// <paramref name="controller"/>'s control. Increments the per-controller
    /// landfall tally. Read by Searing Blaze and other "if you had a land
    /// enter the battlefield under your control this turn" gates.
    /// </summary>
    public void RecordLandEnteredBattlefield(Player? controller)
    {
        if (controller == null) return;
        _landsEnteredByController[controller.Id] =
            _landsEnteredByController.GetValueOrDefault(controller.Id) + 1;
    }

    /// <summary>
    /// How many lands have entered under <paramref name="player"/>'s control
    /// this turn. Returns 0 if <paramref name="player"/> is null or has not
    /// had any lands enter this turn.
    /// </summary>
    public int LandsEnteredByController(Player player) =>
        player == null
            ? 0
            : _landsEnteredByController.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// Convenience predicate: "did <paramref name="player"/> have a land
    /// enter the battlefield under their control this turn?" Read by
    /// Searing Blaze's landfall gate at resolution.
    /// </summary>
    public bool LandEnteredThisTurn(Player player) =>
        LandsEnteredByController(player) > 0;

    /// <summary>
    /// Called when a permanent enters the battlefield. Records the specific
    /// <see cref="Permanent"/> reference in the per-turn ETB set so
    /// resolution-time scans ("creatures that entered the battlefield this
    /// turn" — Force of Despair) can filter precisely. Idempotent: adding
    /// the same reference twice is a no-op (HashSet semantics).
    /// </summary>
    public void RecordPermanentEnteredBattlefield(Permanent permanent)
    {
        if (permanent == null) return;
        _permanentsEnteredThisTurn.Add(permanent);
    }

    /// <summary>
    /// Snapshot of all permanents that entered the battlefield this turn,
    /// regardless of controller / current zone. Callers filter further
    /// (e.g. Force of Despair — still on battlefield + Creature card type).
    /// Returned as a read-only view; mutating the underlying set requires
    /// going through <see cref="RecordPermanentEnteredBattlefield"/> /
    /// <see cref="Reset"/>.
    /// </summary>
    public IReadOnlyCollection<Permanent> PermanentsEnteredThisTurn =>
        _permanentsEnteredThisTurn;

    /// <summary>
    /// Convenience predicate: did <paramref name="permanent"/> enter the
    /// battlefield this turn? Reference-equality check against the per-turn
    /// ETB set.
    /// </summary>
    public bool DidEnterThisTurn(Permanent permanent) =>
        permanent != null && _permanentsEnteredThisTurn.Contains(permanent);

    /// <summary>
    /// Called when <paramref name="player"/> cycles a card (CR 702.32).
    /// Fed by <see cref="Majik.Core.Events.CardCycledEvent"/> subscribers
    /// in <see cref="TurnDriver"/>. Read by Hollow One's self-cost-
    /// reduction reducer ("for each card you've cycled or discarded this
    /// turn").
    /// </summary>
    public void RecordCardCycled(Player? player)
    {
        if (player == null) return;
        _cyclesByPlayer[player.Id] =
            _cyclesByPlayer.GetValueOrDefault(player.Id) + 1;
    }

    /// <summary>
    /// How many cards <paramref name="player"/> has cycled this turn.
    /// </summary>
    public int CyclesByPlayer(Player player) =>
        player == null
            ? 0
            : _cyclesByPlayer.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// Called when <paramref name="player"/> discards a card (CR 701.16).
    /// Fed by <see cref="Majik.Core.Events.CardMovedEvent"/> Hand →
    /// Graveyard subscribers in <see cref="TurnDriver"/> — discarder is
    /// the moved card's owner. Read by Hollow One's self-cost-reduction
    /// reducer alongside <see cref="RecordCardCycled"/>.
    /// </summary>
    public void RecordCardDiscarded(Player? player)
    {
        if (player == null) return;
        _discardsByPlayer[player.Id] =
            _discardsByPlayer.GetValueOrDefault(player.Id) + 1;
    }

    /// <summary>
    /// How many cards <paramref name="player"/> has discarded this turn.
    /// </summary>
    public int DiscardsByPlayer(Player player) =>
        player == null
            ? 0
            : _discardsByPlayer.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// Number of spells <paramref name="player"/> has cast this turn (CR 700.6
    /// per-turn tally). Read by Damping Sphere's "+{1} per other spell cast
    /// this turn" rider — cost calculation runs before the rider increments
    /// the count for the announcing spell, so the value naturally reports
    /// the count of OTHER spells already cast earlier this turn.
    /// </summary>
    public int SpellsCastByPlayer(Player player) =>
        player == null
            ? 0
            : _spellsCastByPlayer.TryGetValue(player.Id, out var v) ? v : 0;

    /// <summary>
    /// True if any player other than <paramref name="viewer"/> has cast a
    /// spell of at least one of the given <paramref name="colors"/> this
    /// turn. Used by Veil of Summer's conditional draw clause.
    /// </summary>
    public bool OpponentCastSpellOfColor(Player viewer, params ManaColor[] colors)
    {
        if (viewer == null || colors == null || colors.Length == 0) return false;
        foreach (var kvp in _spellColorsCastByPlayer)
        {
            if (kvp.Key == viewer.Id) continue;
            foreach (var c in colors)
            {
                if (kvp.Value.Contains(c)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Reset all counters at the start of each turn (called by
    /// <see cref="TurnDriver"/> before the untap step).
    /// </summary>
    public void Reset()
    {
        CreaturesDiedThisTurn = 0;
        PermanentsLeftBattlefieldThisTurn = 0;
        _creaturesDiedByController.Clear();
        _permanentsLeftByController.Clear();
        _cardsDrawnByPlayer.Clear();
        _spellColorsCastByPlayer.Clear();
        _spellsCastByPlayer.Clear();
        _landsEnteredByController.Clear();
        _permanentsEnteredThisTurn.Clear();
        _cyclesByPlayer.Clear();
        _discardsByPlayer.Clear();
    }
}

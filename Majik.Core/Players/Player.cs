using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players;

/// <summary>
/// Represents a player in the game.
/// Encapsulates player state and enforces invariants.
/// </summary>
public class Player
{
    private LifeTotal _lifeTotal;
    private ValueObjects.ManaPool _manaPool;
    private bool _hasLost;

    /// <summary>
    /// Stable per-instance identifier. Used by DTO/web layer to reference
    /// players across requests without serializing object graphs.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// The player's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The player's current life total. Use <see cref="GainLife"/> /
    /// <see cref="LoseLife"/> for in-game changes; the setter is reserved
    /// for engine-level resets and tests.
    /// </summary>
    public int LifeTotal
    {
        get => _lifeTotal.Value;
        internal set => _lifeTotal = ValueObjects.LifeTotal.Create(value);
    }

    /// <summary>
    /// The player's mana pool.
    /// </summary>
    public ValueObjects.ManaPool ManaPool => _manaPool;

    /// <summary>
    /// The player's zone manager.
    /// </summary>
    public ZoneManager Zones { get; }

    /// <summary>
    /// Whether this player has lost the game. Set via <see cref="MarkLost"/>
    /// or by the SBA loop.
    /// </summary>
    public bool HasLost
    {
        get => _hasLost;
        internal set => _hasLost = value;
    }

    /// <summary>Mark this player as having lost the game (CR 104.2).
    /// Idempotent.</summary>
    public void MarkLost() => _hasLost = true;

    /// <summary>
    /// CR 704.5b — sticky flag: true whenever the player attempted to draw
    /// a card from an empty library. Set via
    /// <see cref="MarkTriedToDrawFromEmptyLibrary"/>; SBA picks this up.
    /// </summary>
    public bool TriedToDrawFromEmptyLibrary { get; internal set; }

    /// <summary>Record an attempted draw from an empty library (CR 704.5b).</summary>
    public void MarkTriedToDrawFromEmptyLibrary() => TriedToDrawFromEmptyLibrary = true;

    /// <summary>
    /// CR 119.3 / 702.118 — total life lost this turn from any source. Reset
    /// at turn start by <see cref="ResetTurnTrackers"/>. Consulted by
    /// alt-costs like Spectacle ("if an opponent lost life this turn") and
    /// by Revolt-style effects that key on life loss. Incremented inside
    /// <see cref="LoseLife"/> so every life-loss path (combat damage,
    /// direct-damage spells, shock lands, drain effects, etc.) is captured
    /// automatically.
    /// </summary>
    public int LifeLostThisTurn { get; private set; }

    /// <summary>Reset per-turn life-loss tracker (and any future per-turn
    /// per-player counters). Called by <see cref="Game.TurnDriver"/> at
    /// turn start so the prior turn's loss doesn't bleed forward.</summary>
    public void ResetTurnTrackers()
    {
        LifeLostThisTurn = 0;
    }

    /// <summary>CR 704.5c — poison counters; 10+ → lose.</summary>
    public int PoisonCounters { get; internal set; }

    /// <summary>Add poison counters (CR 122 / 704.5c).</summary>
    public void AddPoisonCounters(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        PoisonCounters += amount;
    }

    /// <summary>CR 106.13 — Energy is a player-scoped resource. Gained
    /// via "you get {E}" effects, spent via "Pay {E}{E}: …" costs.</summary>
    public int EnergyCounters { get; private set; }

    public void GainEnergy(int amount)
    {
        if (amount <= 0) return;
        EnergyCounters += amount;
    }

    /// <summary>Spend N energy if available. Atomic — returns false if
    /// EnergyCounters &lt; amount and changes nothing.</summary>
    public bool PayEnergy(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount > EnergyCounters) return false;
        EnergyCounters -= amount;
        return true;
    }

    /// <summary>
    /// CR 702.139c — Companion once-per-game ledger. Set to <c>true</c>
    /// by <see cref="Majik.Core.Game.SpellCastFlow.CastCompanionAsync"/>
    /// when the player pays the {3} tax to move the nominated companion
    /// from sideboard to hand. Once latched, subsequent attempts to cast
    /// the companion from outside the game are rejected.
    /// </summary>
    public bool CompanionUsedThisGame { get; private set; }

    /// <summary>
    /// Mark that this player has used their once-per-game companion
    /// cast-from-outside-the-game slot (CR 702.139c). Idempotent — once
    /// latched the flag never returns to <c>false</c> for the remainder
    /// of the game.
    /// </summary>
    public void MarkCompanionUsed() => CompanionUsedThisGame = true;

    /// <summary>
    /// CR 702.139 / CR 100.4 — the player's sideboard zone (holds the
    /// nominated companion plus any 15-card MTG sideboard). Convenience
    /// proxy to <see cref="ZoneManager.Sideboard"/>.
    /// </summary>
    public Majik.Core.Zones.IZone Sideboard => Zones.Sideboard;

    /// <summary>CR 903 — per-player commander tracking. Set by Commander
    /// format setup via <see cref="AssignCommander"/>; null otherwise.</summary>
    public Majik.Core.Formats.Commander.CommanderState? Commander { get; internal set; }

    /// <summary>Attach a commander tracker (CR 903). Once per player.</summary>
    public void AssignCommander(Majik.Core.Formats.Commander.CommanderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Commander = state;
    }

    // ── Emblems (CR 114) ────────────────────────────────────────────────────

    private readonly List<Majik.Core.Cards.Emblem> _emblems = new();

    /// <summary>Emblems controlled by this player, living in the command zone
    /// for the rest of the game (CR 114).</summary>
    public IReadOnlyList<Majik.Core.Cards.Emblem> Emblems => _emblems.AsReadOnly();

    /// <summary>Add an emblem to this player's command zone (CR 114).
    /// Callers are responsible for registering any triggered abilities on the
    /// emblem with <c>TriggerManager</c> before or after this call.</summary>
    public void AddEmblem(Majik.Core.Cards.Emblem emblem)
    {
        ArgumentNullException.ThrowIfNull(emblem);
        _emblems.Add(emblem);
    }

    /// <summary>
    /// CR 614 — optional <see cref="ReplacementBus"/> the player routes
    /// life-change intents through. Attached via
    /// <see cref="AttachReplacementBus"/>; when null,
    /// <see cref="GainLife"/> takes the direct mutation path it has
    /// always taken (every pre-existing call site keeps its semantics).
    /// Used by static "players can't gain life" effects (Roiling Vortex /
    /// Sulfuric Vortex / Leyline of Punishment).
    /// </summary>
    public ReplacementBus? Replacements { get; private set; }

    /// <summary>Attach a replacement bus so subsequent life-change
    /// intents route through it. Idempotent — re-attaching the same bus
    /// is a no-op; swapping busses replaces the prior reference.</summary>
    public void AttachReplacementBus(ReplacementBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        Replacements = bus;
    }

    // ── CR 702.131 — Ascend / city's blessing ───────────────────────────────

    private bool _hasCitysBlessing;
    private IEventBus? _citysBlessingBus;

    /// <summary>
    /// CR 702.131c — once the player has had 10 or more permanents at the
    /// same time, they have the city's blessing "for the rest of the game".
    /// Latched: once true, never returns to false. Updated on every
    /// battlefield ETB/LTB tick when <see cref="AttachEventBus"/> has
    /// wired the listener.
    /// </summary>
    public bool HasCitysBlessing
    {
        get => _hasCitysBlessing;
        private set => _hasCitysBlessing = value;
    }

    /// <summary>
    /// Attach the game's event bus so the player can listen for
    /// <see cref="CardMovedEvent"/>s involving the battlefield and
    /// re-evaluate Ascend / city's blessing (CR 702.131). Idempotent —
    /// subsequent calls with the same bus are a no-op; calling with a
    /// different bus rewires to the new one. Also primes the latch from
    /// the player's current battlefield count so attaching post-ETB still
    /// catches up.
    /// </summary>
    public void AttachEventBus(IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        if (ReferenceEquals(_citysBlessingBus, bus))
        {
            EvaluateCitysBlessing();
            return;
        }

        _citysBlessingBus = bus;
        bus.Subscribe<CardMovedEvent>(OnCardMovedForCitysBlessing);
        EvaluateCitysBlessing();
    }

    /// <summary>
    /// Manually re-evaluate the Ascend latch — called by
    /// <see cref="OnCardMovedForCitysBlessing"/> and exposed for tests /
    /// callers that mutate the battlefield directly without going through
    /// the event bus (e.g. raw <c>Zone.AddCard</c>).
    /// </summary>
    public void EvaluateCitysBlessing()
    {
        if (_hasCitysBlessing) return;
        if (Zones.Battlefield.Count < 10) return;

        _hasCitysBlessing = true;
        _citysBlessingBus?.Publish(new GainedCitysBlessingEvent(this));
    }

    private void OnCardMovedForCitysBlessing(CardMovedEvent e)
    {
        if (_hasCitysBlessing) return;
        // Only re-evaluate on moves involving the battlefield + this player.
        if (e.ToZone != ZoneType.Battlefield && e.FromZone != ZoneType.Battlefield) return;
        if (!ReferenceEquals(e.Card.Controller, this)) return;
        EvaluateCitysBlessing();
    }

    public Player(string name, int startingLife = 20, ZoneManager? zoneManager = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Player name cannot be null or empty", nameof(name));
        }

        Name = name;
        _lifeTotal = ValueObjects.LifeTotal.Create(startingLife);
        _manaPool = ValueObjects.ManaPool.Empty;
        _hasLost = false;
        Zones = zoneManager ?? new ZoneManager(this);
    }

    /// <summary>
    /// Gain life.
    /// </summary>
    public void GainLife(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount must be non-negative", nameof(amount));
        }

        if (_hasLost)
        {
            throw new Domain.Exceptions.InvalidPlayerActionException("Cannot gain life after losing the game");
        }

        // CR 614 — when a replacement bus is attached, route the intent
        // through it so static "players can't gain life" effects (Roiling
        // Vortex / Sulfuric Vortex / Leyline of Punishment) can rewrite
        // the gain amount before commit. Players without a bus take the
        // direct path — every pre-existing caller keeps its semantics.
        var resolvedAmount = amount;
        if (Replacements != null)
        {
            var replaced = Replacements.Apply(new LifeGainIntent(this, amount));
            if (replaced == null) return; // cancelled entirely
            resolvedAmount = Math.Max(0, replaced.Amount);
        }

        if (resolvedAmount == 0) return;
        _lifeTotal = _lifeTotal.Add(resolvedAmount);
    }

    /// <summary>
    /// Lose life.
    /// </summary>
    public void LoseLife(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount must be non-negative", nameof(amount));
        }

        if (_hasLost)
        {
            throw new Domain.Exceptions.InvalidPlayerActionException("Cannot lose life after losing the game");
        }

        _lifeTotal = _lifeTotal.Subtract(amount);

        // CR 119.3 — track life lost this turn for alt-costs/triggers
        // (Spectacle, Revolt, etc.). amount==0 doesn't count as "losing
        // life" per CR 119.4, so guard before bumping.
        if (amount > 0)
        {
            LifeLostThisTurn += amount;
        }

        // Check if player has lost
        if (_lifeTotal.HasLost)
        {
            _hasLost = true;
        }
    }

    /// <summary>
    /// Add mana to the player's mana pool.
    /// </summary>
    public void AddManaToPool(ValueObjects.ManaCost mana)
    {
        if (mana == null)
        {
            throw new ArgumentNullException(nameof(mana));
        }

        if (_hasLost)
        {
            throw new Domain.Exceptions.InvalidPlayerActionException("Cannot add mana after losing the game");
        }

        _manaPool = _manaPool.Add(mana);
    }

    /// <summary>
    /// Pay mana from the player's mana pool.
    /// </summary>
    public bool PayMana(ValueObjects.ManaCost cost)
    {
        if (cost == null)
        {
            throw new ArgumentNullException(nameof(cost));
        }

        if (_hasLost)
        {
            return false;
        }

        var (newPool, success) = _manaPool.Pay(cost);
        if (success)
        {
            _manaPool = newPool;
        }

        return success;
    }

    /// <summary>
    /// Empty the mana pool (happens at end of steps/phases per Rule 500.4).
    /// </summary>
    public void EmptyManaPool()
    {
        _manaPool = _manaPool.EmptyPool();
    }

    public override string ToString()
    {
        return $"{Name} ({_lifeTotal.Value} life, {_manaPool} mana)";
    }
}

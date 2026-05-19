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

    /// <summary>CR 903 — per-player commander tracking. Set by Commander
    /// format setup via <see cref="AssignCommander"/>; null otherwise.</summary>
    public Majik.Core.Formats.Commander.CommanderState? Commander { get; internal set; }

    /// <summary>Attach a commander tracker (CR 903). Once per player.</summary>
    public void AssignCommander(Majik.Core.Formats.Commander.CommanderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Commander = state;
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

        _lifeTotal = _lifeTotal.Add(amount);
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

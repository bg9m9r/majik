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
    /// players across requests without serializing object graphs (portal's
    /// <c>controllerId</c>, and the key for the per-game ambient registries).
    /// PLAN 08 — per-game deterministic id when a game scope is installed;
    /// falls back to <see cref="Guid.NewGuid"/> for scope-less construction.
    /// </summary>
    public Guid Id { get; } = Majik.Core.Game.DeterministicIdScope.NewId();

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

    /// <summary>
    /// CR 702.11 — player-hexproof query. True iff at least one active
    /// player-hexproof grant (registered through
    /// <see cref="Majik.Core.Rules.PlayerStaticAbilities"/>) targets this
    /// player. Lights up while Leyline of Sanctity (or any future player-
    /// hexproof source) is on the battlefield. Consulted by
    /// <see cref="Majik.Core.Rules.ActionValidator"/> at cast / activation
    /// time and by <see cref="Majik.Core.Targeting.TargetLegality"/> at
    /// resolution time (CR 608.2b) to reject opponent-controlled spells
    /// and abilities that name this player as a target.
    /// </summary>
    public bool HasHexproof =>
        Majik.Core.Rules.PlayerStaticAbilities.HasHexproof(this);

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

    /// <summary>
    /// CR 408 / CR 100.4 — the player's wishboard: the queryable surface
    /// for wish-tutor effects ("a card you own from outside the game")
    /// such as Burning Wish, Cunning Wish, Glittering Wish, Living Wish,
    /// Mastermind's Acquisition mode 2, and Karn, the Great Creator's
    /// -2. Physically the same pile as <see cref="Sideboard"/> — the
    /// distinction is semantic, not structural: the deck-builder is
    /// responsible for marking which cards are in the sideboard, and
    /// every card in the sideboard is automatically reachable as part
    /// of the wishboard pool. Distinct from the Companion slot (CR
    /// 702.139c — once-per-game tax to bring a single nominated
    /// sideboard card into hand) which has its own latching ledger via
    /// <see cref="CompanionUsedThisGame"/>.
    /// </summary>
    public Majik.Core.Zones.IZone Wishboard => Zones.Sideboard;

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

    // ── The Ring (CR 701.54) ─────────────────────────────────────────────────

    /// <summary>
    /// CR 701.54 — this player's Ring state (the emblem named The Ring + tempt
    /// counter + Ring-bearer designation), or null until the Ring first tempts
    /// them (CR 701.54c — the emblem is created on the first tempt). Reusable
    /// across every LOTR card that tempts.
    /// </summary>
    public RingState? Ring { get; private set; }

    /// <summary>
    /// CR 701.54a/c — "the Ring tempts you." Creates the emblem named The Ring
    /// on the first tempt, increments the tempt count, and (when a creature is
    /// offered) designates it the Ring-bearer. The optional services let the
    /// emblem's staged triggered abilities drive themselves off the live event
    /// bus; supply them once and they persist for the rest of the game.
    /// Subsequent calls reuse the existing <see cref="Ring"/> and ignore later
    /// service args (the Ring is created at most once).
    /// </summary>
    public void TheRingTemptsYou(
        Majik.Core.Cards.Permanent? chosenBearer,
        IEventBus? eventBus = null,
        Majik.Core.Abilities.TriggerManager? triggers = null,
        Func<IReadOnlyList<Player>>? allPlayersResolver = null)
    {
        Ring ??= new RingState(this, eventBus, triggers, allPlayersResolver);
        Ring.Tempt(chosenBearer);
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
    /// CR 106.4 / CR 702.10 — minimal mana-provenance side-channel for the
    /// "if that mana is spent on a creature spell, it gains haste" rider
    /// (Arena of Glory's exert ability). Counts units of red mana currently
    /// floating in the pool that, if spent on a creature spell, grant that
    /// creature haste until end of turn.
    ///
    /// <para><b>Why a side-channel and not per-slot pool tags:</b>
    /// <see cref="ValueObjects.ManaPool"/> stores bucketed colour counts
    /// with no slot-level provenance, and rewriting it to a list-of-tags is
    /// a separate slice (see <see cref="Majik.Core.Mana.ManaTag"/> /
    /// <see cref="Majik.Core.Mana.SpendRestriction"/> xmldoc). This counter
    /// is the least-invasive correct mechanism: the exert ability stamps it
    /// when it adds {R}{R}, <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// consumes it at the next spell cast, and it dies with the floating
    /// mana when the pool empties (CR 500.4 — see
    /// <see cref="EmptyManaPool"/>). v1 consumes the provenance on the first
    /// spell cast after the exert (granting haste only when that spell is a
    /// creature spell); per-pip "exactly which mana paid which pip"
    /// accounting is the same deferred slice as ManaTag.</para>
    /// </summary>
    public int PendingHasteGrantingRedMana { get; private set; }

    /// <summary>
    /// Record <paramref name="amount"/> units of haste-granting red mana
    /// floating in the pool (CR 702.10 rider). Additive across multiple
    /// exert activations in the same step. Negative amounts are rejected.
    /// </summary>
    public void AddHasteGrantingRedMana(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        PendingHasteGrantingRedMana += amount;
    }

    /// <summary>
    /// Consume the pending haste-granting provenance — clears the counter and
    /// returns whether any was pending. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when a spell is cast: when
    /// the spell is a creature spell and this returns <c>true</c>, the
    /// resulting creature gains haste until end of turn (CR 702.10).
    /// </summary>
    public bool ConsumeHasteGrantingMana()
    {
        var had = PendingHasteGrantingRedMana > 0;
        PendingHasteGrantingRedMana = 0;
        return had;
    }

    /// <summary>
    /// Empty the mana pool (happens at end of steps/phases per Rule 500.4).
    /// The haste-granting provenance dies with the floating mana.
    /// </summary>
    public void EmptyManaPool()
    {
        _manaPool = _manaPool.EmptyPool();
        PendingHasteGrantingRedMana = 0;
    }

    public override string ToString()
    {
        return $"{Name} ({_lifeTotal.Value} life, {_manaPool} mana)";
    }
}

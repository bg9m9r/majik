using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Base class for permanent cards (cards that stay on the battlefield).
/// Includes: Creatures, Lands, Enchantments, Artifacts, Planeswalkers.
/// </summary>
public class Permanent : Card
{
    private bool _isTapped;
    private bool _hasSummoningSickness;
    private DateTime? _enteredBattlefieldTimestamp;
    private readonly List<ICard> _imprintedCards = new();

    /// <summary>
    /// Counters on this permanent (CR 122). +1/+1 and -1/-1 modify P/T
    /// via the layer system; others are opaque markers.
    /// </summary>
    public Majik.Core.Counters.CounterCollection Counters { get; } = new();

    /// <summary>True if this is a token (CR 111). Tokens cease to exist
    /// off battlefield via SBA 704.5d. Set via <see cref="MarkAsToken"/>.</summary>
    public bool IsToken { get; internal set; }

    /// <summary>Optional Battle-card tracker. SBA 704.5n consults this.
    /// Set via <see cref="AttachBattleState"/>.</summary>
    public Majik.Core.CardData.Battles.BattleState? BattleState { get; internal set; }

    /// <summary>Optional Saga-card tracker. SBA 704.5r consults this.
    /// Set via <see cref="AttachSagaState"/>.</summary>
    public Majik.Core.CardData.Sagas.SagaState? SagaState { get; internal set; }

    /// <summary>Optional Class-enchantment leveling tracker (CR 716).
    /// Set via <see cref="AttachClassState"/>; consulted by per-level
    /// activated / triggered abilities to gate on
    /// <see cref="Majik.Core.CardData.Classes.ClassState.CurrentLevel"/>.</summary>
    public Majik.Core.CardData.Classes.ClassState? ClassState { get; internal set; }

    /// <summary>Mark this permanent as a token (CR 111). Tokens are
    /// removed off-battlefield by SBA 704.5d.</summary>
    public void MarkAsToken() => IsToken = true;

    // -----------------------------------------------------------------------
    // CR 702.49 — Imprint
    // -----------------------------------------------------------------------

    /// <summary>CR 702.49 — cards exiled "with" this permanent via an
    /// imprint ability. Other abilities (e.g. Isochron Scepter, Chrome Mox,
    /// Agatha's Soul Cauldron) reference these exiled cards.</summary>
    public IReadOnlyList<ICard> ImprintedCards => _imprintedCards.AsReadOnly();

    /// <summary>Record <paramref name="card"/> as imprinted on this permanent
    /// (CR 702.49). Called by the exile effect when the imprint keyword or an
    /// equivalent ability moves a card to exile "with" this permanent.</summary>
    public void AddImprinted(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _imprintedCards.Add(card);
    }

    /// <summary>Remove all imprinted cards from this permanent. Used when the
    /// permanent leaves the battlefield or its imprint state is reset.</summary>
    public void ClearImprinted() => _imprintedCards.Clear();

    /// <summary>Attach a Battle tracker (CR 310) to this permanent.</summary>
    public void AttachBattleState(Majik.Core.CardData.Battles.BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        BattleState = state;
    }

    /// <summary>Attach a Saga tracker (CR 714) to this permanent.</summary>
    public void AttachSagaState(Majik.Core.CardData.Sagas.SagaState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SagaState = state;
    }

    /// <summary>Attach a Class-leveling tracker (CR 716) to this permanent.</summary>
    public void AttachClassState(Majik.Core.CardData.Classes.ClassState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ClassState = state;
    }

    /// <summary>
    /// CR 301.5 / 303.4 — the permanent this one is attached to (Aura
    /// enchanting its target, Equipment equipped to a creature). Null when
    /// not attached.
    /// </summary>
    public Permanent? AttachedTo { get; private set; }

    private readonly List<Permanent> _attachments = new();

    /// <summary>Permanents currently attached TO this one (auras, equipment,
    /// fortifications). Mirror of <see cref="AttachedTo"/>.</summary>
    public IReadOnlyList<Permanent> Attachments => _attachments.AsReadOnly();

    /// <summary>Attach this permanent to another. Idempotent if already
    /// attached to the target; throws if asked to attach to itself.</summary>
    public void AttachTo(Permanent target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (ReferenceEquals(target, this))
            throw new InvalidOperationException("Permanent cannot attach to itself");
        if (ReferenceEquals(AttachedTo, target)) return;

        Unattach();
        AttachedTo = target;
        target._attachments.Add(this);
    }

    /// <summary>Detach this permanent from its current parent (if any).
    /// Used when bearer leaves the battlefield, attach is illegal, etc.</summary>
    public void Unattach()
    {
        if (AttachedTo == null) return;
        AttachedTo._attachments.Remove(this);
        AttachedTo = null;
    }

    /// <summary>
    /// Whether the permanent is tapped.
    /// </summary>
    public bool IsTapped
    {
        get => _isTapped;
        private set => _isTapped = value;
    }

    /// <summary>
    /// Whether this permanent has summoning sickness (CR 302.1).
    /// </summary>
    public bool HasSummoningSickness
    {
        get => _hasSummoningSickness;
        internal set => _hasSummoningSickness = value;
    }

    /// <summary>Clear summoning sickness once this permanent has been
    /// controlled continuously since the controller's most recent untap step.</summary>
    public void ClearSummoningSickness() => _hasSummoningSickness = false;

    /// <summary>
    /// Timestamp when this permanent entered the battlefield.
    /// Used for Legend rule (Rule 704.5k).
    /// </summary>
    public DateTime? EnteredBattlefieldTimestamp
    {
        get => _enteredBattlefieldTimestamp;
        private set => _enteredBattlefieldTimestamp = value;
    }

    public Permanent(string name, string manaCost, IEnumerable<CardType> cardTypes, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, cardTypes, supertypes, subtypes)
    {
        _isTapped = false;
        _hasSummoningSickness = true;
        _enteredBattlefieldTimestamp = null;
    }

    /// <summary>
    /// Mark this permanent as having entered the battlefield.
    /// Called when the permanent enters the battlefield.
    /// </summary>
    internal void MarkEnteredBattlefield()
    {
        if (_enteredBattlefieldTimestamp == null)
        {
            _enteredBattlefieldTimestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Tap the permanent.
    /// </summary>
    public void Tap()
    {
        if (_isTapped)
        {
            throw new InvalidOperationException("Permanent is already tapped");
        }

        _isTapped = true;
    }

    /// <summary>
    /// Untap the permanent.
    /// </summary>
    public void Untap()
    {
        if (!_isTapped)
        {
            throw new InvalidOperationException("Permanent is not tapped");
        }

        _isTapped = false;
    }

    /// <summary>True if a loyalty ability (CR 606.5) has been activated
    /// on this permanent during the current turn. Reset by
    /// <see cref="ResetTurnState"/>.</summary>
    public bool LoyaltyAbilityActivatedThisTurn { get; internal set; }

    // -----------------------------------------------------------------------
    // CR 701.15 — Regeneration shields
    //
    // A "regeneration effect" applies a replacement: the next time this
    // permanent would be destroyed this turn, instead tap it, remove all
    // damage, and remove it from combat. Shields stack (CR 701.15a — each
    // regeneration creates a separate shield) and clear during cleanup
    // (CR 514.2 — "until end of turn" effects end).
    //
    // Modeled as a counter on the permanent rather than a ReplacementBus
    // entry so the destroy gate in
    // <see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard(Majik.Core.Cards.ICard, Majik.Core.Zones.ZoneMoveReason)"/>
    // can consume shields without needing a Replacements reference (the
    // many destroy-spell factories that route through MoveToGraveyard are
    // called from effect closures that don't capture the bus). State-based
    // destroys still go through the bus-driven
    // <see cref="Majik.Core.Effects.RegenerationShieldEffect"/> so the
    // existing SBA-side flow is unchanged.
    // -----------------------------------------------------------------------

    private int _regenerationShields;

    /// <summary>True if this permanent has at least one active regeneration
    /// shield queued up (CR 701.15a).</summary>
    public bool HasRegenerationShield => _regenerationShields > 0;

    /// <summary>Number of regeneration shields queued on this permanent
    /// (CR 701.15a — multiple regeneration effects stack as separate
    /// shields, each consumed independently by the next destroy).</summary>
    public int RegenerationShieldCount => _regenerationShields;

    /// <summary>
    /// CR 701.15a — add one regeneration shield to this permanent. The
    /// next time it would be destroyed this turn,
    /// <see cref="ConsumeRegenerationShield"/> taps it, clears damage,
    /// and removes the shield instead of moving it to the graveyard.
    /// Shields stack and clear during cleanup.
    /// </summary>
    public void AddRegenerationShield() => _regenerationShields++;

    /// <summary>
    /// CR 701.15c — consume one regeneration shield. Taps the permanent
    /// (if untapped), clears marked damage (creatures only), and
    /// decrements the shield count. Returns <c>true</c> if a shield was
    /// available and consumed.
    /// </summary>
    public bool ConsumeRegenerationShield()
    {
        if (_regenerationShields <= 0) return false;
        _regenerationShields--;
        if (!IsTapped) Tap();
        if (this is Creature c) c.ClearDamage();
        // CR 701.15c also removes the permanent from combat; the combat
        // manager owns per-turn attacker/blocker plans and doesn't expose
        // a per-creature removal hook yet (same gap as
        // RegenerationShieldEffect). Followup when CombatFlow surfaces it.
        return true;
    }

    /// <summary>
    /// CR 514.2 — clear regeneration shields during the cleanup step.
    /// Called by <see cref="Majik.Core.Game.TurnDriver"/> alongside the
    /// damage-clear / continuous-effect / replacement-bus EOT sweep.
    /// </summary>
    public void ClearRegenerationShields() => _regenerationShields = 0;

    /// <summary>
    /// Clear per-turn flags (loyalty-once-per-turn, attacked-this-turn,
    /// summoning sickness). Called by the engine during the untap step.
    /// </summary>
    public virtual void ResetTurnState()
    {
        LoyaltyAbilityActivatedThisTurn = false;
        HasSummoningSickness = false;
    }
}

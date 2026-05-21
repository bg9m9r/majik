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

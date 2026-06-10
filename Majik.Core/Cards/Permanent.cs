using Majik.Core.Cards.Types;
using Majik.Core.Game;
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

    /// <summary>
    /// Optional reference to the
    /// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> that owns
    /// this permanent. When set, characteristic lookups that go through the
    /// layer system (P/T and keywords on creatures via
    /// <see cref="Creature"/>; effective colour via
    /// <see cref="GetEffectiveColors"/> on any permanent) consult it (CR
    /// 613). When null, printed/static values are returned.
    /// </summary>
    public Majik.Core.Effects.ContinuousEffectsService? ActiveEffects { get; set; }

    /// <summary>
    /// CR 105.3 / CR 613.1e — this permanent's <em>effective</em> colour
    /// set after the Layer-5 colour-changing pass. When
    /// <see cref="ActiveEffects"/> is set this consults the layer system
    /// (so "is all colors" / "becomes blue" effects are honoured); when
    /// null — or when no Layer-5 colour effect is active — it falls back to
    /// the printed/static derivation in
    /// <see cref="Majik.Core.Cards.CardColors.GetColors(Majik.Core.Cards.ICard)"/>.
    /// Returns only the five real colours; an empty set is colourless.
    /// </summary>
    public IReadOnlySet<ValueObjects.ManaColor> GetEffectiveColors()
    {
        if (ActiveEffects == null)
        {
            return Majik.Core.Cards.CardColors.GetColors(this);
        }
        return ActiveEffects.Compute(this).Colors;
    }

    /// <summary>
    /// CR 205.4 / CR 613.1d — this permanent's <em>effective</em> supertype
    /// set after the Layer-4 type-changing pass. When
    /// <see cref="ActiveEffects"/> is set this consults the layer system
    /// (so a granted supertype — the Ring-bearer "is legendary" clause via
    /// <see cref="Majik.Core.Effects.GrantSupertypeEffect"/> — is honoured);
    /// when null it falls back to the printed supertypes. Mirrors
    /// <see cref="GetEffectiveColors"/>.
    /// </summary>
    public IReadOnlySet<Types.CardSupertype> GetEffectiveSupertypes()
    {
        if (ActiveEffects == null)
        {
            return Supertypes.ToHashSet();
        }
        return ActiveEffects.EffectiveSupertypes(this);
    }

    /// <summary>
    /// CR 205.4 — true iff this permanent currently has
    /// <paramref name="supertype"/> as an effective supertype (printed OR
    /// granted by an active <see cref="Majik.Core.Effects.GrantSupertypeEffect"/>).
    /// The legend-rule SBA reads through this so a granted Legendary counts.
    /// </summary>
    public bool HasEffectiveSupertype(Types.CardSupertype supertype) =>
        GetEffectiveSupertypes().Contains(supertype);

    /// <summary>
    /// CR 205.3 / CR 613.1d — this permanent's <em>effective</em> subtype set
    /// after the Layer-4 type-changing pass. When <see cref="ActiveEffects"/>
    /// is set this consults the layer system (so a granted creature type — a
    /// "becomes a Dragon" Layer-4 effect, Changeling printed-subtype stamping —
    /// is honoured); when null it falls back to the printed subtypes. Mirrors
    /// <see cref="GetEffectiveColors"/> / <see cref="GetEffectiveSupertypes"/>.
    /// Read by protection-from-subtype
    /// (<see cref="Majik.Core.Rules.Protection.HasProtectionFromSubtype"/>).
    /// </summary>
    public IReadOnlySet<Types.CardSubtype> GetEffectiveSubtypes()
    {
        if (ActiveEffects == null)
        {
            return Subtypes.ToHashSet();
        }
        return ActiveEffects.EffectiveSubtypes(this);
    }

    /// <summary>
    /// CR 707.2 / CR 613.2 (Layer 1) — this permanent's <em>effective</em> name
    /// after the copy-effect pass. When <see cref="ActiveEffects"/> is set this
    /// consults the layer system (so a clone of a permanent named X reports
    /// "X" via an active
    /// <see cref="Majik.Core.Effects.CopyCharacteristicsEffect"/>); when null it
    /// falls back to the printed <see cref="Card.Name"/>. Same-name matching
    /// ("another permanent named X", Izzet Staticaster) reads through this so a
    /// clone counts. Mirrors <see cref="GetEffectiveColors"/> /
    /// <see cref="GetEffectiveSupertypes"/>.
    /// </summary>
    public string GetEffectiveName()
    {
        if (ActiveEffects == null)
        {
            return Name;
        }
        return ActiveEffects.EffectiveName(this);
    }

    /// <summary>
    /// CR 707.2 / CR 202.3 — this permanent's <em>effective</em> mana-cost
    /// string after the copy-effect pass (mana value is derived from it). When
    /// <see cref="ActiveEffects"/> is set this consults the layer system (a
    /// clone reports the copied source's mana cost); when null it falls back to
    /// the printed <see cref="Card.ManaCost"/>. Mirrors
    /// <see cref="GetEffectiveName"/>.
    /// </summary>
    public string GetEffectiveManaCost()
    {
        if (ActiveEffects == null)
        {
            return ManaCost;
        }
        return ActiveEffects.EffectiveManaCost(this);
    }

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
    // CR 708 — Face-down spells / permanents (Morph / Manifest / Disguise /
    // Cloak family). Minimal primitive shipped for Manifest dread
    // (CR 701.59) — see <see cref="MarkFaceDown"/> / <see cref="TurnFaceUp"/>.
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 708.2 — true while this permanent is face-down. Face-down
    /// permanents are 2/2 colourless creatures with no name, mana cost,
    /// or abilities (CR 708.2). Effective P/T overrides on
    /// <see cref="Creature.GetPower"/> / <see cref="Creature.GetToughness"/>
    /// consult this flag.
    /// </summary>
    public bool IsFaceDown { get; private set; }

    /// <summary>
    /// CR 708.2 — flip this permanent to face-down. Native abilities are
    /// suppressed via the <see cref="IsFaceDown"/> gate (callers read
    /// <see cref="EffectiveAbilities"/>); P/T overrides to 2/2. Idempotent.
    /// </summary>
    public void MarkFaceDown()
    {
        IsFaceDown = true;
        // CR 708.2 — face-down flips the permanent to a 2/2 with no other
        // characteristics. Creature.GetPower/GetToughness short-circuit on
        // IsFaceDown, but the layer pipeline's seed (types/subtypes/keywords/
        // colours) also changes, so invalidate the memoization cache.
        ActiveEffects?.BumpGeneration();
    }

    /// <summary>
    /// CR 708.6 — turn this permanent face-up, restoring native
    /// characteristics. Idempotent. Caller (typically the
    /// "turn face up for mana cost" activated ability resolution body)
    /// is responsible for paying any cost; this method only flips the
    /// flag.
    /// </summary>
    public void TurnFaceUp()
    {
        IsFaceDown = false;
        // CR 708.6 — restoring native characteristics changes the layer
        // pipeline's seed; invalidate the memoization cache (see MarkFaceDown).
        ActiveEffects?.BumpGeneration();
    }

    /// <summary>
    /// Abilities that are intrinsic to this permanent's face-down state —
    /// they survive the CR 708.2 ability-suppression that hides the
    /// underlying card's printed abilities. The only current member is the
    /// <b>ward {2}</b> a cloaked permanent has (CR 702.168a / CR 708.4):
    /// cloak's definition gives the face-down 2/2 ward {2}, so that ward is
    /// one of the face-down permanent's abilities even though the
    /// underlying card's printed abilities are hidden. Populated by
    /// <see cref="MarkFaceDownIntrinsicAbility"/>.
    /// </summary>
    private readonly HashSet<Majik.Core.Abilities.IAbility> _faceDownIntrinsicAbilities = new();

    /// <summary>
    /// Flag <paramref name="ability"/> (which must already be attached via
    /// <see cref="Card.AddAbility"/>) as intrinsic to this permanent's
    /// face-down state so it is surfaced by <see cref="EffectiveAbilities"/>
    /// while <see cref="IsFaceDown"/> is true. Used for the cloak ward {2}
    /// (CR 702.168a) — see <see cref="EffectiveAbilities"/>.
    /// </summary>
    public void MarkFaceDownIntrinsicAbility(Majik.Core.Abilities.IAbility ability)
    {
        if (ability is null) throw new ArgumentNullException(nameof(ability));
        _faceDownIntrinsicAbilities.Add(ability);
    }

    /// <summary>
    /// CR 708.2 — abilities visible to the engine for this permanent.
    /// While <see cref="IsFaceDown"/> is true, this returns the empty
    /// set even if native abilities are attached, AND any abilities
    /// granted by manifest / morph (e.g. "turn face up for cost") plus
    /// any abilities intrinsic to the face-down state (the cloak ward {2},
    /// CR 702.168a — see <see cref="MarkFaceDownIntrinsicAbility"/>)
    /// remain queryable via <see cref="Abilities"/> — those are the
    /// face-down permanent's *only* abilities. Callers that need to
    /// query a face-down permanent's playable abilities should use
    /// the face-down-aware ability filter rather than
    /// <see cref="Card.Abilities"/> directly.
    /// </summary>
    public IReadOnlyList<Majik.Core.Abilities.IAbility> EffectiveAbilities
        => IsFaceDown
            ? Abilities
                .Where(a => a is Majik.Core.Abilities.FaceDownActivatedAbility
                            || _faceDownIntrinsicAbilities.Contains(a))
                .ToList()
                .AsReadOnly()
            : Abilities;

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
        // CR 613 — equipment/aura attach changes which permanent a Layer-6
        // grant (Sword of Fire and Ice protection, etc.) reconciles onto. The
        // grant lifecycle runs as a side effect of the Compute pipeline, so a
        // stale cache hit would skip it; invalidate both sides.
        ActiveEffects?.BumpGeneration();
        target.ActiveEffects?.BumpGeneration();
    }

    /// <summary>Detach this permanent from its current parent (if any).
    /// Used when bearer leaves the battlefield, attach is illegal, etc.</summary>
    public void Unattach()
    {
        if (AttachedTo == null) return;
        var formerHost = AttachedTo;
        AttachedTo._attachments.Remove(this);
        AttachedTo = null;
        // CR 613.6e — detach lifts grants the equipment fed onto its host;
        // invalidate so the next Compute re-runs the grant reconciliation.
        ActiveEffects?.BumpGeneration();
        formerHost.ActiveEffects?.BumpGeneration();
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
        // CR 613 — counter mutations gate layered effects (counter-threshold
        // statics, CDAs) as well as the per-Compute P/T arithmetic; invalidate
        // the memoization cache whenever the bag changes.
        Counters.OnMutated = () => ActiveEffects?.BumpGeneration();
    }

    /// <summary>
    /// Simulation copy constructor. Chains to <see cref="Card(Card)"/> for the
    /// base state, then copies Permanent-level runtime state.
    /// Tap state, counters, summoning sickness, and attachment links are copied
    /// here as shallow scalars. <see cref="ActiveEffects"/> is intentionally
    /// left null — the cloned game does not wire up the layer service
    /// (simulation reads base characteristics directly).
    /// </summary>
    protected Permanent(Permanent src) : base(src)
    {
        // permanent runtime state copied here (Task 4 will expand counters/tap)
        _isTapped = src._isTapped;
        _hasSummoningSickness = src._hasSummoningSickness;
        _enteredBattlefieldTimestamp = src._enteredBattlefieldTimestamp;
        IsToken = src.IsToken;
        IsFaceDown = src.IsFaceDown;
        LoyaltyAbilityActivatedThisTurn = src.LoyaltyAbilityActivatedThisTurn;
        AdditionalLandPlaysGranted = src.AdditionalLandPlaysGranted;
        WasDealtDamageThisTurn = src.WasDealtDamageThisTurn;
        _transientLoyalty = src._transientLoyalty;
        _regenerationShields = src._regenerationShields;
        // Counters: deep-copy each per-type count from the source bag into this
        // permanent's own (already-initialised) CounterCollection.
        foreach (var (type, n) in src.Counters.All)
        {
            Counters.Add(type, n);
        }
        // ActiveEffects: left null — cloned game does not wire up the layer service.
        // BattleState / SagaState / ClassState: complex attached trackers — skipped for sim v1.
        // AttachedTo / _attachments: re-linked in a later pass (Task 5).
        // _imprintedCards: re-linked in a later pass (Task 5).
        Counters.OnMutated = () => ActiveEffects?.BumpGeneration();
    }

    /// <summary>
    /// Simulation pass-2c override: re-links Controller/Owner (via base), then
    /// re-links attachment references through the card remap table so that
    /// cloned permanents point at each other instead of at originals.
    /// <para>
    /// Sets backing fields directly (bypassing <see cref="AttachTo"/> event
    /// hooks) because <see cref="Permanent.ActiveEffects"/> is null on all
    /// cloned permanents — no cache bump is needed.
    /// </para>
    /// </summary>
    internal override void RelinkReferences(
        Card src,
        IReadOnlyDictionary<Guid, ICard> cards,
        IReadOnlyDictionary<Player, Player> players)
    {
        base.RelinkReferences(src, cards, players);
        var sp = (Permanent)src;

        // Re-link AttachedTo: if the source permanent was attached to something,
        // point this clone at the cloned host.
        if (sp.AttachedTo is { } host && cards.TryGetValue(host.InstanceId, out var clonedHost))
        {
            AttachedTo = (Permanent)clonedHost;
        }

        // Re-link _attachments: rebuild the attachments list by looking up each
        // source attachment in the card map. preserves: AttachedTo → _attachments mirror.
        _attachments.Clear();
        foreach (var attached in sp._attachments)
        {
            if (cards.TryGetValue(attached.InstanceId, out var clonedAttached))
            {
                _attachments.Add((Permanent)clonedAttached);
            }
        }

        // Re-link ManaAbility.Source: the copy-ctor shares ability refs by
        // reference (_abilities.AddRange). Any ManaAbility whose Source is the
        // original live permanent would tap the LIVE permanent when activated in
        // the sandbox, corrupting live game state across MCTS iterations (the
        // live land becomes tapped after each sandbox run, so subsequent
        // iterations can no longer pay the mana cost). Replace such abilities
        // with new instances bound to THIS clone so sandbox taps stay local.
        // ManaAbility.CloneForSim returns null for non-simple-shape abilities
        // (dynamic generators, additional-cost payers, etc.); those keep the
        // original ref — a known limitation tracked for future work.
        var manaAbilitiesToRelink = Abilities
            .OfType<Majik.Core.Abilities.ManaAbility>()
            .Where(ma => ReferenceEquals(ma.Source, src))
            .ToList();
        foreach (var old in manaAbilitiesToRelink)
        {
            var ctrl = old.Controller != null && players.TryGetValue(old.Controller, out var cp)
                ? cp
                : old.Controller;
            if (ctrl == null) continue; // no controller to bind — keep original
            var replacement = old.CloneForSim(this, ctrl);
            if (replacement != null)
            {
                RemoveAbility(old);
                AddAbility(replacement);
            }
            // else: keep original (complex ability shape; live-source tap is a
            // known limitation until those shapes are sim-clonable too).
        }
    }

    /// <summary>
    /// Mark this permanent as having entered the battlefield.
    /// Called when the permanent enters the battlefield.
    /// </summary>
    internal void MarkEnteredBattlefield()
    {
        if (_enteredBattlefieldTimestamp == null)
        {
            // Determinism (PLAN 08 prerequisite): the ETB timestamp that
            // decides WHICH legendary survives (LegendRuleCheck) / which
            // planeswalker survives (PlaneswalkerUniquenessCheck) — a true
            // game-decision divergence on replay — comes from the per-game
            // logical clock, not wall-clock. Assigned in ETB order.
            _enteredBattlefieldTimestamp = LogicalClockScope.Current.NextTimestamp();
        }
    }

    /// <summary>
    /// Tap the permanent (no attributed actor). CR 701.21.
    /// </summary>
    public void Tap() => Tap(causedBy: null);

    /// <summary>
    /// Tap the permanent, attributing the tap to <paramref name="causedBy"/>
    /// (the "you" in a "whenever you tap …" trigger, CR 603.2). Publishes a
    /// <see cref="Majik.Core.Domain.DomainEvents.PermanentTappedEvent"/> via
    /// the ambient <see cref="Majik.Core.Events.EventBusRegistry"/> — once per
    /// real tap, at every tap site. CR 701.21.
    /// </summary>
    public void Tap(Player? causedBy)
    {
        if (_isTapped)
        {
            throw new InvalidOperationException("Permanent is already tapped");
        }

        _isTapped = true;
        // CR 613 — tap state gates some continuous effects (Paradise Druid's
        // "hexproof as long as untapped", vehicles, etc.); invalidate.
        ActiveEffects?.BumpGeneration();

        // CR 701.21 — announce the tap so "whenever you tap a creature …"
        // triggers (Solitary Sanctuary) can observe it. Best-effort: the bus
        // is looked up via the per-game ambient registry (the leaf Permanent
        // carries no bus reference); null outside a game scope, where
        // publishing is a no-op (direct-construction unit tests).
        Majik.Core.Events.EventBusRegistry.Get(Controller ?? causedBy)
            ?.Publish(new Majik.Core.Domain.DomainEvents.PermanentTappedEvent(this, causedBy));
    }

    /// <summary>
    /// CR 400.7 / 613.7 / 614 — a permanent that changes zones becomes a NEW
    /// object and loses all status it had on the battlefield, including its
    /// tapped/untapped state. Called by the zone-move pipeline when a
    /// permanent leaves the battlefield. Clears the tapped flag directly
    /// (without going through <see cref="Untap"/>, which throws when the
    /// permanent is already untapped) so it is safe to call blindly on every
    /// battlefield exit regardless of the prior tap state.
    /// </summary>
    internal void ResetOnLeaveBattlefield()
    {
        _isTapped = false;
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
        // CR 613 — see Tap(): tap state gates some continuous effects.
        ActiveEffects?.BumpGeneration();
    }

    /// <summary>True if a loyalty ability (CR 606.5) has been activated
    /// on this permanent during the current turn. Reset by
    /// <see cref="ResetTurnState"/>.</summary>
    public bool LoyaltyAbilityActivatedThisTurn { get; internal set; }

    // -----------------------------------------------------------------------
    // CR 711 / 306.5b — transient ("effective") loyalty surface.
    //
    // A planeswalker CARD is a <see cref="Planeswalker"/> C# instance carrying
    // its own <see cref="Planeswalker.Loyalty"/> field. But a transform DFC
    // whose FRONT face is a creature (Ral, Monsoon Mage; Tamiyo, Compleated
    // Sage; etc.) is built as a <see cref="Creature"/> instance — flipping to
    // its planeswalker BACK face cannot re-class the runtime object without
    // touching zone/identity. Re-classing is the trap the v1-deferral
    // (#19a, planeswalker-back-and-copy-reclass-loyalty-body) calls out.
    //
    // Instead, a non-planeswalker permanent can carry a PARALLEL transient
    // loyalty body: a nullable loyalty value that the loyalty subsystem
    // consults through <see cref="GetEffectiveLoyalty"/> /
    // <see cref="IsEffectivePlaneswalker"/> rather than the concrete
    // <see cref="Planeswalker"/> subclass — mirroring how
    // <see cref="GetEffectiveColors"/> / supertypes decoupled those from the
    // subclass. The back-face Layer-0 seed (BackFaceCharacteristics) already
    // stamps the Planeswalker TYPE through Compute; this surface gives that
    // type a working loyalty body so the loyalty=0 death SBA (CR 704.5j) and
    // loyalty-removing damage (CR 120.3 / 306.7) apply to it.
    //
    // <see cref="Planeswalker"/> overrides the accessors to read its own
    // <see cref="Planeswalker.Loyalty"/>, so a real planeswalker keeps its
    // authoritative loyalty field and this transient surface is inert on it.
    // -----------------------------------------------------------------------

    private int? _transientLoyalty;

    /// <summary>
    /// CR 711 — grant (or clear, with <c>null</c>) a transient planeswalker
    /// loyalty body to a non-planeswalker permanent. Set on transform to a
    /// planeswalker back face (seeded from
    /// <see cref="Majik.Core.CardData.MDFCs.BackFaceCharacteristics.Loyalty"/>);
    /// cleared on transform back to the front face. No-op semantics for a real
    /// <see cref="Planeswalker"/> (which holds authoritative loyalty itself).
    /// </summary>
    public void SetTransientLoyalty(int? startingLoyalty)
    {
        if (startingLoyalty is < 0)
            throw new ArgumentException("Loyalty cannot be negative", nameof(startingLoyalty));
        _transientLoyalty = startingLoyalty;
    }

    /// <summary>
    /// CR 306.5b — this permanent's current loyalty when it carries a
    /// planeswalker body, else <c>null</c>. A real <see cref="Planeswalker"/>
    /// overrides this to return its own loyalty; a creature-front transform DFC
    /// flipped to a planeswalker back returns the transient value.
    /// </summary>
    public virtual int? GetEffectiveLoyalty() => _transientLoyalty;

    /// <summary>
    /// True when this permanent has a working loyalty body — either a real
    /// <see cref="Planeswalker"/> or a non-planeswalker carrying a transient
    /// loyalty surface (CR 711 back-face planeswalker).
    /// </summary>
    public bool IsEffectivePlaneswalker() => GetEffectiveLoyalty().HasValue;

    /// <summary>
    /// CR 704.5j — true when this permanent has a loyalty body that has dropped
    /// to 0 (so the planeswalker-death SBA destroys it). False when it carries
    /// no loyalty body.
    /// </summary>
    public bool IsLoyaltyDead() => GetEffectiveLoyalty() is { } loyalty && loyalty <= 0;

    /// <summary>
    /// CR 306.7 / 120.3 — remove <paramref name="amount"/> loyalty from a
    /// transient loyalty body (damage / loyalty-cost). Floors at 0. No-op when
    /// this permanent has no transient body (a real <see cref="Planeswalker"/>
    /// removes from its own field). Returns <c>true</c> if a transient body
    /// absorbed the removal.
    /// </summary>
    public virtual bool RemoveTransientLoyalty(int amount)
    {
        if (amount < 0)
            throw new ArgumentException("Loyalty removal cannot be negative", nameof(amount));
        if (_transientLoyalty is not { } current) return false;
        _transientLoyalty = Math.Max(0, current - amount);
        return true;
    }

    /// <summary>
    /// CR 305.2 / 720 — the number of <em>additional</em> land plays this
    /// permanent grants its controller each turn while it is on the
    /// battlefield ("you may play N additional land(s) on each of your
    /// turns"). 0 for the vast majority of permanents; +1 for Exploration /
    /// Dryad of the Ilysian Grove, +2 for Azusa, Lost but Seeking.
    ///
    /// This is a controller-scoped, battlefield-gated static (CR 603.6e —
    /// functions only while the source is on the battlefield). It is summed
    /// live by <see cref="Majik.Core.Game.LandDropTracker"/> over the
    /// permanents the active player controls, so it naturally appears when
    /// the permanent enters and disappears when it leaves (no per-turn
    /// re-application needed — the tracker recomputes on every land-play
    /// validation). Multiple sources stack additively (two Azusas = +4).
    /// </summary>
    public int AdditionalLandPlaysGranted { get; set; }

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
        OnRegenerationShieldConsumed();
        // CR 701.15c also removes the permanent from combat; the combat
        // manager owns per-turn attacker/blocker plans and doesn't expose
        // a per-creature removal hook yet (same gap as
        // RegenerationShieldEffect). Followup when CombatFlow surfaces it.
        return true;
    }

    /// <summary>
    /// CR 701.15c hook — called from <see cref="ConsumeRegenerationShield"/>
    /// after the permanent is tapped and the shield count is decremented.
    /// Defaults to a no-op; subclasses that hold per-turn combat damage
    /// (e.g. <see cref="Creature"/>) override to clear it.
    /// </summary>
    protected virtual void OnRegenerationShieldConsumed()
    {
    }

    /// <summary>
    /// CR 514.2 — clear regeneration shields during the cleanup step.
    /// Called by <see cref="Majik.Core.Game.TurnDriver"/> alongside the
    /// damage-clear / continuous-effect / replacement-bus EOT sweep.
    /// </summary>
    public void ClearRegenerationShields() => _regenerationShields = 0;

    /// <summary>
    /// CR 120.3 — true once this permanent has been dealt damage at least
    /// once during the current turn. Distinct from marked
    /// <see cref="Majik.Core.Cards.Creature.Damage"/>: a creature healed /
    /// regenerated (or whose damage became -1/-1 counters via wither) still
    /// "was dealt damage this turn", and a planeswalker that took loyalty-
    /// removing damage carries no marked-damage at all. The flag is stamped
    /// on every permanent-damage seam via <see cref="RecordDamageDealt"/>
    /// (combat + noncombat creature damage, wither/infect counter
    /// redirection, planeswalker damage) so payoffs that gate on "a creature
    /// that was dealt damage this turn" / "any target that was dealt damage
    /// this turn" (Needle Drop, CR 120.3) observe the precise condition.
    /// Cleared during the cleanup step (CR 514.2) alongside marked damage by
    /// <see cref="Majik.Core.Game.TurnDriver"/>.
    /// </summary>
    public bool WasDealtDamageThisTurn { get; private set; }

    /// <summary>
    /// CR 120.3 — record that this permanent was dealt
    /// <paramref name="amount"/> damage this turn, setting
    /// <see cref="WasDealtDamageThisTurn"/>. A non-positive amount is not
    /// "damage" and is ignored (CR 120.3 — 0 damage isn't dealt). The
    /// marked-damage / loyalty-removal / counter bookkeeping itself stays with
    /// the caller (e.g. <see cref="Majik.Core.Cards.Creature.TakeDamage"/>,
    /// the wither counter branches, planeswalker loyalty removal) — this only
    /// stamps the per-turn "was dealt damage" flag.
    /// </summary>
    public void RecordDamageDealt(int amount)
    {
        if (amount > 0) WasDealtDamageThisTurn = true;
    }

    /// <summary>
    /// CR 514.2 — clear the per-turn "was dealt damage this turn" flag during
    /// the cleanup step, alongside the marked-damage sweep.
    /// </summary>
    public void ClearWasDealtDamageThisTurn() => WasDealtDamageThisTurn = false;

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

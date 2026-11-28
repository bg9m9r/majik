namespace Majik.Core.Api.Dtos;

// PLAN 07 — Typed EventDto payload records.
//
// One record per CURRENTLY-EMITTED GameEvent type (the ~16 typed arms in
// EventPayloadBuilder.Build). These replace the string-keyed anonymous
// objects the builder used to construct: each record's property names,
// serialized through JsonNamingPolicy.CamelCase (the policy
// EventPayloadBuilder already uses), produce a BYTE-IDENTICAL JSON
// payload to the prior anonymous shape. The wire still carries the
// serialized JsonElement on EventDto.Payload; the records exist so the
// shapes are (a) a single source of truth shared with the frontend via
// OpenAPI (see EventSchemaCatalog + the /matches/_eventschemas anchor)
// and (b) lockable by golden-JSON tests.
//
// CR 706 hidden-information note: CardMovedPayload and CardDrawnPayload
// are DUAL-SHAPE. The revealed variant populates the identity/enrichment
// fields; the masked variant leaves them null and sets Hidden = true.
// They are ONE record each (not two) because the masked + revealed
// variants share a wire schema — a consumer keys on Hidden, not on the
// record type. Nullable fields + a default Hidden=false keep the revealed
// JSON identical to the legacy anonymous object (System.Text.Json omits
// nothing by default, but the legacy revealed object never carried a
// `hidden` key, so the masked object set it explicitly while the revealed
// one omitted it). To preserve that exactly, the builder constructs the
// records with the same field set the anonymous objects had — see
// EventPayloadBuilder for the per-variant construction.

/// <summary>
/// CR 400 / CR 706 — a card changing zones. Dual-shape:
/// <list type="bullet">
/// <item>Revealed: all fields populated, <see cref="Hidden"/> = false.</item>
/// <item>Masked (both zones hidden to a non-owner viewer): only
/// <see cref="OwnerId"/>, <see cref="From"/>, <see cref="To"/> +
/// <see cref="Hidden"/> = true; identity / permanent fields stay null.</item>
/// </list>
/// Field set mirrors the enriched payload built from
/// <c>StateSnapshotter.BuildPermanentFields</c> so a snapshot and a
/// CardMovedEvent agree.
/// </summary>
public sealed record CardMovedPayload(
    string From,
    string To,
    Guid? OwnerId = null,
    Guid? CardId = null,
    string? CardName = null,
    string? ManaCost = null,
    IReadOnlyList<string>? Types = null,
    int? Power = null,
    int? Toughness = null,
    bool? Tapped = null,
    bool? SummoningSickness = null,
    IReadOnlyList<AbilityDto>? Abilities = null,
    string? ProducedManaColors = null,
    IReadOnlyDictionary<string, int>? Counters = null,
    bool? Hidden = null,
    // CR 708.2 — true when a face-down permanent enters the battlefield
    // (manifest, cloak). The card fields are then what the viewer may see:
    // to anyone but its controller, "Face-down permanent", a 2/2 creature
    // with no mana cost and no abilities. Omitted otherwise.
    bool? IsFaceDown = null,
    // CR 601.2a — on a move to the stack (a spell being cast): the id of the
    // stack entry the card becomes, i.e. its StackObjectDto.Id, both while it
    // is being cast and once it is on the stack. Omitted for other moves.
    Guid? StackEntryId = null,
    // CR 708.9 — a face-down permanent left the battlefield and was revealed:
    // CardId is the face-down permanent that left (the id clients know on the
    // battlefield); this is the id of the card it was, now in To, face up,
    // whose name and characteristics the card fields carry. Omitted otherwise.
    Guid? RevealedCardId = null);

/// <summary>
/// CR 120 — a player drawing a card (Library → Hand). Dual-shape: the
/// owner sees identity (<see cref="CardId"/> etc.); a non-owner viewer
/// gets the count-only masked variant (<see cref="PlayerId"/> +
/// <see cref="Hidden"/> = true).
/// </summary>
public sealed record CardDrawnPayload(
    Guid PlayerId,
    Guid? CardId = null,
    string? CardName = null,
    string? ManaCost = null,
    IReadOnlyList<string>? Types = null,
    bool? Hidden = null);

/// <summary>CR 701.16 — a card revealed from a (possibly hidden) zone.
/// Always public (the controller chose to show it), so no masking.</summary>
public sealed record CardRevealedPayload(
    Guid CardId,
    string CardName,
    Guid PlayerId,
    string From,
    string Reason);

/// <summary>CR 119 — a player's life total changing.</summary>
public sealed record LifeChangedPayload(
    Guid PlayerId,
    int Previous,
    int Current);

/// <summary>Top-level game-lifecycle transition (Initializing / Mulligan /
/// Playing / GameOver). Distinct from the phase/step channel.</summary>
public sealed record GameStateChangedPayload(
    string? From,
    string To);

/// <summary>CR 500/505 — phase-level transition (TurnBeginning /
/// PreCombatMain / Combat / PostCombatMain / TurnEnding).</summary>
public sealed record PhaseStateChangedPayload(
    string? From,
    string To);

/// <summary>CR 500 — entry into a step. <see cref="Step"/> is the
/// resolved step label.</summary>
public sealed record StepStartedPayload(
    string Step,
    Guid PlayerId);

/// <summary>CR 500.1 — a player's turn beginning.</summary>
public sealed record TurnStartedPayload(
    Guid PlayerId,
    int Turn);

/// <summary>CR 500.1 — a player's turn ending.</summary>
public sealed record TurnEndedPayload(
    Guid PlayerId,
    int Turn);

/// <summary>CR 500.7–9 — an extra phase inserted into the turn sequence.</summary>
public sealed record ExtraPhaseAddedPayload(
    string Phase);

/// <summary>CR 104.3a — a player losing the game.</summary>
public sealed record PlayerLostPayload(
    Guid PlayerId);

/// <summary>
/// CR 405 — an object placed on or leaving the stack. Shared by
/// SpellCastEvent, StackObjectAddedEvent, and StackObjectResolvedEvent —
/// all three emit the same wire shape (mirroring <c>StackObjectDto</c>)
/// so the portal can patch <c>state.stack</c> uniformly.
/// <see cref="CardId"/> / <see cref="CardName"/> are populated only on
/// SpellCastEvent (where the stack object is a spell with a backing card).
/// </summary>
public sealed record StackObjectPayload(
    Guid StackId,
    Guid ControllerId,
    string Kind,
    string Description,
    Guid? CardId = null,
    string? CardName = null);

/// <summary>
/// CR 119 / CR 510 — damage dealt from a source to a target. Covers
/// every <c>DamageDealtEvent</c> subclass (Combat / Spell / Ability) in
/// one shape. <see cref="TargetIsPlayer"/> disambiguates a player victim
/// from a permanent victim.
/// </summary>
public sealed record DamageDealtPayload(
    Guid SourceInstanceId,
    Guid TargetInstanceId,
    bool TargetIsPlayer,
    int Amount,
    string DamageType,
    /// <summary>The source card's name (CR 120.2: the spell, ability source or
    /// creature that dealt the damage), masked for a face-down source
    /// (CR 708.2). Null when the source is a player (no object is known).</summary>
    string? SourceName = null);

/// <summary>
/// CR 121 / CR 614 — counters placed on a permanent. Lean payload so the
/// portal can bump the counter badge in place (P/T stays authoritative
/// via the next snapshot). <see cref="ControllerId"/> is null when the
/// placement has no controller context.
/// </summary>
public sealed record CounterAddedPayload(
    Guid TargetInstanceId,
    string CounterType,
    int Amount,
    Guid? ControllerId = null);

/// <summary>
/// CR 613 — a continuous (layer) effect entered the game's active-effects
/// set. Log-only surface for the portal action log: the source permanent's
/// identity, the CR-613 <c>Layer</c> name, and a human-readable
/// <see cref="Description"/>. Public information (battlefield), so no
/// per-viewer masking. <see cref="SourceInstanceId"/> is
/// <see cref="System.Guid.Empty"/> for a floating effect with no source.
/// </summary>
public sealed record ContinuousEffectAddedPayload(
    Guid SourceInstanceId,
    string SourceName,
    string Layer,
    string Description);

/// <summary>
/// CR 613 — twin of <see cref="ContinuousEffectAddedPayload"/>: a continuous
/// (layer) effect left the active-effects set (unregister / prune /
/// end-of-turn cleanup). Same shape, log-only, public.
/// </summary>
public sealed record ContinuousEffectRemovedPayload(
    Guid SourceInstanceId,
    string SourceName,
    string Layer,
    string Description);

/// <summary>
/// CR 602 — an activated ability put on the stack. Carries the controller
/// (who activated it) and the source permanent's identity so the portal can
/// render "Alice activated Bloodstained Mire" in the game log without
/// refetching /state. <see cref="SourceInstanceId"/> is
/// <see cref="System.Guid.Empty"/> and <see cref="SourceName"/> is empty
/// when the source is not an <c>ICard</c> (rare; non-permanent sources).
/// </summary>
public sealed record AbilityActivatedPayload(
    Guid ControllerId,
    Guid SourceInstanceId,
    string SourceName);

// Long-tail payloads for the player-facing log and animations. Each carries
// ids plus display names so a client needs no state lookup. Display names go
// through the same CR 708.2 rule everywhere: a face-down permanent is shown
// as "Face-down permanent" to anyone but its controller (CR 708.6), and its
// payload is then built per viewer (EventPayloadBuilder.RequiresPerViewerMasking).

/// <summary>CR 701.26a — a permanent became tapped. <see cref="CausedById"/>
/// is the player whose effect or cost tapped it, when known.</summary>
public sealed record PermanentTappedPayload(
    Guid PermanentId,
    string PermanentName,
    Guid? ControllerId,
    Guid? CausedById);

/// <summary>CR 603.2 — a triggered ability triggered (before it is put on the
/// stack). Dual-shape: when the source is in a zone hidden from the viewer
/// (CR 706: a library, or another player's hand) only <see cref="AbilityId"/>,
/// <see cref="ControllerId"/> and <see cref="Hidden"/> = true are sent.</summary>
public sealed record TriggeredAbilityTriggeredPayload(
    Guid AbilityId,
    Guid ControllerId,
    Guid? SourceId = null,
    string? SourceName = null,
    string? Text = null,
    bool? Hidden = null);

/// <summary>CR 508.1 — one declared attacker and what it attacks (a player, or
/// a planeswalker / battle permanent).</summary>
public sealed record DeclaredAttackerPayload(
    Guid AttackerId,
    string AttackerName,
    Guid? DefendingPlayerId,
    Guid? DefendingPermanentId);

/// <summary>CR 508.8 — attackers declared (the whole declaration at once).</summary>
public sealed record AttackersDeclaredPayload(
    Guid AttackingPlayerId,
    IReadOnlyList<DeclaredAttackerPayload> Attackers);

/// <summary>CR 509.1 — one declared blocker and the attacker it blocks.</summary>
public sealed record DeclaredBlockerPayload(
    Guid BlockerId,
    string BlockerName,
    Guid AttackerId);

/// <summary>CR 509.4 — blockers declared (the whole declaration at once).</summary>
public sealed record BlockersDeclaredPayload(
    Guid? DefendingPlayerId,
    IReadOnlyList<DeclaredBlockerPayload> Blockers);

/// <summary>CR 508.1 — a single creature attacks.</summary>
public sealed record CreatureAttacksPayload(
    Guid AttackerId,
    string AttackerName,
    Guid? ControllerId,
    Guid? DefendingPlayerId,
    Guid? DefendingPermanentId);

/// <summary>CR 509.1 — a single creature blocks an attacker.</summary>
public sealed record CreatureBlocksPayload(
    Guid BlockerId,
    string BlockerName,
    Guid? BlockerControllerId,
    Guid AttackerId,
    string AttackerName);

/// <summary>CR 701.9 — a card was discarded. Public: the card is revealed
/// on its way to the graveyard.</summary>
public sealed record DiscardedPayload(
    Guid PlayerId,
    Guid CardId,
    string CardName,
    bool WasCost);

/// <summary>CR 701.21 — a permanent was sacrificed.</summary>
public sealed record PermanentSacrificedPayload(
    Guid PlayerId,
    Guid CardId,
    string CardName,
    bool WasToken);

/// <summary>CR 701.6 — a spell was countered. <see cref="StackId"/> is the
/// spell's stack id.
/// <para><see cref="CounteringStackId"/> / <see cref="CounteringName"/> name the
/// spell or ability whose effect countered it: its StackObjectDto.Id and its
/// display name (a spell's card name; an ability's source name, "Face-down
/// permanent" to a player who may not look at that source, CR 708.2). Both
/// null when the rules countered it (CR 608.2b) or the source is unknown.</para>
/// <para><see cref="Reason"/> is one of a closed set: "CounteredByEffect" (CR
/// 701.6a, a counter effect) or "AllTargetsIllegal" (CR 608.2b, every target
/// was illegal on resolution).</para></summary>
public sealed record SpellCounteredPayload(
    Guid StackId,
    Guid? CardId,
    string CardName,
    Guid ControllerId,
    Guid? CounteringControllerId,
    Guid? CounteringStackId = null,
    string? CounteringName = null,
    string Reason = "CounteredByEffect");

/// <summary>CR 603.4 / CR 701.6 — a triggered ability was countered or removed
/// (e.g. its intervening-if condition was false on resolution).
/// <see cref="StackId"/> is the removed entry's StackObjectDto.Id (for an
/// ability that triggered twice, the firing that was removed).
/// <see cref="Reason"/> is a display string from a closed set; today its only
/// value is "intervening-if failed at resolution" (CR 603.4).</summary>
public sealed record TriggeredAbilityCounteredPayload(
    Guid StackId,
    Guid? SourceId,
    string SourceName,
    Guid ControllerId,
    string Reason);

/// <summary>CR 605 — a mana ability was activated. <see cref="Mana"/> is the
/// produced mana in mana-cost notation.</summary>
public sealed record ManaAbilityActivatedPayload(
    Guid PlayerId,
    Guid? SourceId,
    string SourceName,
    string Mana);

/// <summary>CR 119.4 — a player paid life.</summary>
public sealed record LifePaidPayload(
    Guid PlayerId,
    int Amount,
    bool WasCost);

/// <summary>CR 601.2h / CR 602.2b — the costs of a spell or ability were paid.
/// <see cref="Costs"/> lists each cost's description.</summary>
public sealed record CostsPaidPayload(
    Guid StackId,
    Guid ControllerId,
    IReadOnlyList<string> Costs);

/// <summary>CR 601.2c — targets were chosen for a spell or ability. A target
/// card in a zone hidden from the viewer is sent with an empty id (CR 706),
/// the same rule as <see cref="StackObjectDto.Targets"/>.</summary>
public sealed record TargetsChosenPayload(
    Guid StackId,
    Guid ControllerId,
    IReadOnlyList<TargetRefDto> Targets);

/// <summary>CR 117 — a player received or passed priority.</summary>
public sealed record PriorityPlayerPayload(Guid PlayerId);

/// <summary>CR 506 — the combat phase began or ended.</summary>
public sealed record CombatPhasePayload(Guid ActivePlayerId);

/// <summary>CR 701.20 — a library was shuffled.</summary>
public sealed record LibraryShuffledPayload(
    Guid PlayerId,
    int CardCount,
    string Reason);

/// <summary>A card identity inside a list payload.</summary>
public sealed record CardRefPayload(Guid CardId, string CardName);

/// <summary>CR 701.42 — a player surveilled N. Dual-shape (CR 706): every
/// viewer sees the cards put into the graveyard (a public zone) and how many
/// stayed on top; only the surveilling player sees <see cref="ToTop"/>.</summary>
public sealed record SurveilPayload(
    Guid PlayerId,
    int N,
    IReadOnlyList<CardRefPayload> ToGraveyard,
    int ToTopCount,
    IReadOnlyList<CardRefPayload>? ToTop = null);

/// <summary>CR 702.29 — a card was cycled.</summary>
public sealed record CardCycledPayload(
    Guid PlayerId,
    Guid CardId,
    string CardName);

/// <summary>CR 500.7 — an extra turn was added for a player.</summary>
public sealed record ExtraTurnAddedPayload(Guid PlayerId);

/// <summary>
/// PLAN 07 — OpenAPI schema anchor. The SignalR <c>event</c> channel
/// carries <see cref="EventDto.Payload"/> as a raw <c>JsonElement</c>, so
/// the payload record shapes are otherwise invisible to OpenAPI (SignalR
/// hubs aren't described by the spec). This catalog references every
/// currently-emitted <c>*Payload</c> record by name so that an
/// (unused-by-gameplay) REST endpoint returning it forces
/// <c>ng-openapi-gen</c> to emit a TypeScript interface per payload into
/// the portal's generated client — giving the reducer typed shapes to
/// cast to. The endpoint exists purely as a schema carrier; the portal
/// never calls it.
/// <para>
/// Every field here MUST be non-nullable so the referenced schema is
/// reachable from the document root even with
/// <c>ignoreUnusedModels: true</c> in <c>ng-openapi-gen.json</c>.
/// </para>
/// </summary>
public sealed record EventPayloadCatalog(
    CardMovedPayload CardMoved,
    CardDrawnPayload CardDrawn,
    CardRevealedPayload CardRevealed,
    LifeChangedPayload LifeChanged,
    GameStateChangedPayload GameStateChanged,
    PhaseStateChangedPayload PhaseStateChanged,
    StepStartedPayload StepStarted,
    TurnStartedPayload TurnStarted,
    TurnEndedPayload TurnEnded,
    ExtraPhaseAddedPayload ExtraPhaseAdded,
    PlayerLostPayload PlayerLost,
    StackObjectPayload StackObject,
    DamageDealtPayload DamageDealt,
    CounterAddedPayload CounterAdded,
    ContinuousEffectAddedPayload ContinuousEffectAdded,
    ContinuousEffectRemovedPayload ContinuousEffectRemoved,
    AbilityActivatedPayload AbilityActivated,
    PermanentTappedPayload PermanentTapped,
    TriggeredAbilityTriggeredPayload TriggeredAbilityTriggered,
    AttackersDeclaredPayload AttackersDeclared,
    BlockersDeclaredPayload BlockersDeclared,
    CreatureAttacksPayload CreatureAttacks,
    CreatureBlocksPayload CreatureBlocks,
    DiscardedPayload Discarded,
    PermanentSacrificedPayload PermanentSacrificed,
    SpellCounteredPayload SpellCountered,
    TriggeredAbilityCounteredPayload TriggeredAbilityCountered,
    ManaAbilityActivatedPayload ManaAbilityActivated,
    LifePaidPayload LifePaid,
    CostsPaidPayload CostsPaid,
    TargetsChosenPayload TargetsChosen,
    PriorityPlayerPayload PriorityPlayer,
    CombatPhasePayload CombatPhase,
    LibraryShuffledPayload LibraryShuffled,
    SurveilPayload Surveil,
    CardCycledPayload CardCycled,
    ExtraTurnAddedPayload ExtraTurnAdded);

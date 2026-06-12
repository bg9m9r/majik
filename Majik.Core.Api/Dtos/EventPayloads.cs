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
    bool? Hidden = null);

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
    string DamageType);

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
    ContinuousEffectRemovedPayload ContinuousEffectRemoved);

using System.Text.Json;
using System.Text.Json.Serialization;
using Majik.Core.Abilities;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Api;

/// <summary>
/// Maps engine <see cref="GameEvent"/> instances to lean JSON payloads
/// for transport. Engine events carry live object references (Player,
/// ICard) that we deliberately don't serialize directly — clients see
/// stable identifiers (Guids) and primitive fields only.
///
/// Unknown event types fall back to an empty payload; the client still
/// receives <c>Type</c> + <c>EventId</c> on the envelope so future
/// payload additions are non-breaking.
///
/// CR 706 hidden information: <see cref="CardMovedEvent"/> and
/// <see cref="CardDrawnEvent"/> sometimes describe a move between zones
/// that are hidden to a viewer (library / hand). The
/// <see cref="Build(GameEvent, Player)"/> overload masks card identity
/// for non-owner viewers when BOTH the source and destination zones are
/// hidden to opponents (Hand or Library) — i.e. the card was never
/// publicly visible at the time of the move. Any other transition
/// (movement that touches Battlefield, Graveyard, Exile, or Stack on
/// either side) reveals the card because the identity was already public
/// at the time of the move. The full-reveal overload (<c>viewer = null</c>)
/// keeps spectator / debug snapshot behavior.
/// </summary>
public static class EventPayloadBuilder
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // PLAN 07 — the dual-shape card payload records (CardMovedPayload /
    // CardDrawnPayload) carry the union of the masked + revealed field
    // sets, with the variant-specific fields nullable. Serializing with
    // WhenWritingNull collapses each constructed record back to EXACTLY
    // the historical key set:
    //   * masked CardMoved → {from, to, ownerId, hidden}
    //   * revealed CardMoved → identity + permanent fields, no `hidden`
    //     (a non-creature land's null power/toughness drop out, matching
    //     the CardSnapshotDto contract the portal reducer reads
    //     defensively via pickNumber).
    // Only these two records use it; every other payload keeps the
    // legacy Opts (null fields serialized verbatim) so non-dual-shape
    // shapes are byte-identical to the pre-PLAN-07 anonymous objects.
    private static readonly JsonSerializerOptions NullOmittingOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Full-reveal payload (no viewer scoping). Used for the
    /// spectator broadcast and any consumer outside the per-recipient
    /// SignalR routing path.</summary>
    public static JsonElement Build(GameEvent e) => Build(e, viewer: null, turnState: null);

    /// <summary>Viewer-scoped payload. <paramref name="viewer"/> = null
    /// means full reveal (spectator). Non-null applies CR 706 masking
    /// rules to <see cref="CardMovedEvent"/> / <see cref="CardDrawnEvent"/>;
    /// the rest of the payload set is public and ignores the viewer.</summary>
    public static JsonElement Build(GameEvent e, Player? viewer) => Build(e, viewer, turnState: null);

    /// <summary>Viewer-scoped payload with turn-state context. Since Slice 3
    /// the engine carries the precombat / postcombat distinction as
    /// first-class phase values, so phase / step labels are already
    /// disambiguated and the <paramref name="turnState"/> argument is retained
    /// only for call-site compatibility (it no longer affects the labels).</summary>
    public static JsonElement Build(GameEvent e, Player? viewer, TurnStateType? turnState) => e switch
    {
        CardMovedEvent x => BuildCardMoved(x, viewer),
        CardDrawnEvent x => BuildCardDrawn(x, viewer),
        // CardRevealedEvent: hand-reveal effects (CR 701.16) are public
        // by definition — the controller chose to show the card to all
        // players. No per-viewer masking required.
        CardRevealedEvent x => Serialize(new CardRevealedPayload(
            CardId: x.Card.InstanceId,
            CardName: x.Card.Name,
            PlayerId: x.Player.Id,
            From: x.From.ToString(),
            Reason: x.Reason)),
        LifeChangedEvent x => Serialize(new LifeChangedPayload(
            PlayerId: x.Player.Id,
            Previous: x.PreviousLife,
            Current: x.NewLife)),
        PhaseStartedEvent x => Serialize(new PhaseStartedPayload(
            Phase: PhaseLabelResolver.Resolve(x.PhaseType, turnState),
            PlayerId: x.Player.Id)),
        PhaseEndedEvent x => Serialize(new PhaseEndedPayload(
            Phase: PhaseLabelResolver.Resolve(x.PhaseType, turnState),
            PlayerId: x.Player.Id)),
        PhaseChangedEvent x => Serialize(new PhaseChangedPayload(
            From: x.PreviousPhase,
            To: x.CurrentPhase)),
        TurnStateChangedEvent x => Serialize(new TurnStateChangedPayload(
            From: x.PreviousState?.ToString(),
            To: x.CurrentState.ToString())),
        StepStartedEvent x => Serialize(new StepStartedPayload(
            Step: PhaseLabelResolver.Resolve(x.StepType, turnState),
            PlayerId: x.Player.Id)),
        StepEndedEvent x => Serialize(new StepEndedPayload(
            Step: PhaseLabelResolver.Resolve(x.StepType, turnState),
            PlayerId: x.Player.Id)),
        TurnStartedEvent x => Serialize(new TurnStartedPayload(
            PlayerId: x.Player.Id,
            Turn: x.TurnNumber)),
        TurnEndedEvent x => Serialize(new TurnEndedPayload(
            PlayerId: x.Player.Id,
            Turn: x.TurnNumber)),
        ExtraPhaseAddedEvent x => Serialize(new ExtraPhaseAddedPayload(
            Phase: x.PhaseType.ToString())),
        PlayerLostEvent x => Serialize(new PlayerLostPayload(
            PlayerId: x.Player.Id)),
        // SpellCastEvent / StackObject*Event payloads mirror StackObjectDto
        // (see Dtos.cs + StateSnapshotter.SnapshotStackObject) so the
        // frontend can patch `state.stack` from the wire delta without
        // re-fetching /state. `kind` matches the StackObjectDto.Kind
        // discriminator ("Spell" | "TriggeredAbility" | "ActivatedAbility")
        // and `description` mirrors the same composition rules used by the
        // snapshotter — keep these two builders in sync.
        // SpellCast carries the backing-card identity; StackObjectAdded /
        // Resolved deliberately do NOT (matching the historical wire) — the
        // null CardId/CardName drop out via NullOmittingStack serialization.
        SpellCastEvent x => SerializeStack(new StackObjectPayload(
            StackId: x.Spell.Id,
            ControllerId: x.Spell.Controller.Id,
            Kind: "Spell",
            Description: (x.Spell as ISpell)?.Card?.Name ?? "",
            CardId: (x.Spell as ISpell)?.Card?.InstanceId,
            CardName: (x.Spell as ISpell)?.Card?.Name)),
        StackObjectAddedEvent x => SerializeStack(new StackObjectPayload(
            StackId: x.StackObject.Id,
            ControllerId: x.StackObject.Controller.Id,
            Kind: StackKind(x.StackObject),
            Description: StackDescription(x.StackObject))),
        StackObjectResolvedEvent x => SerializeStack(new StackObjectPayload(
            StackId: x.StackObject.Id,
            ControllerId: x.StackObject.Controller.Id,
            Kind: StackKind(x.StackObject),
            Description: StackDescription(x.StackObject))),
        // Per-creature / per-player damage payload (CR 119, CR 510).
        // Frontend needs source + target Guids so it can draw a damage
        // animation from the attacker (or spell) to the victim — life
        // flash alone isn't enough at the per-creature level. We map
        // every DamageDealtEvent subclass (Combat / Spell / Ability)
        // to a single wire shape so the portal can render uniformly.
        DamageDealtEvent x => Serialize(new DamageDealtPayload(
            SourceInstanceId: x.SourceInstanceId,
            TargetInstanceId: x.TargetInstanceId,
            TargetIsPlayer: x.TargetIsPlayer,
            Amount: x.Amount,
            DamageType: x.DamageType.ToString())),
        // PLAN 04 — CR 121 / CR 614 counter placement. Lean payload so the
        // portal reducer can bump the target's counter badge in place
        // (display only — P/T are recomputed authoritatively by the next
        // snapshot, never derived from counters in the reducer). Counter
        // placement is public information (a counter on a battlefield
        // permanent is visible to all players), so no per-viewer masking.
        CounterAddedEvent x => Serialize(new CounterAddedPayload(
            TargetInstanceId: x.Target.InstanceId,
            CounterType: x.CounterType.Name,
            Amount: x.Amount,
            ControllerId: x.Controller?.Id)),
        GameStartedEvent => Empty(),
        _ => Empty(),
    };

    /// <summary>True iff <paramref name="e"/>'s payload varies per viewer
    /// (CR 706). Bridge code uses this to decide between group broadcast
    /// and per-recipient publish — group fan-out of a payload that masks
    /// for some viewers but not others would leak the unmasked variant
    /// to the wrong seat.</summary>
    public static bool RequiresPerViewerMasking(GameEvent e) => e switch
    {
        CardMovedEvent x => BothZonesHidden(x.FromZone, x.ToZone),
        CardDrawnEvent => true, // library → hand: always both hidden
        _ => false,
    };

    private static JsonElement BuildCardMoved(CardMovedEvent x, Player? viewer)
    {
        var ownerId = x.Card.Owner?.Id;
        bool mask = ShouldMaskCardForViewer(viewer, ownerId, x.FromZone, x.ToZone);

        if (mask)
        {
            // CR 706 — masked variant MUST stay exactly {ownerId, from, to,
            // hidden}. No enrichment ever crosses the masking boundary; an
            // opponent must not learn the card's identity or its permanent
            // fields from a Hand→Library (etc.) move. Constructing the
            // record with only these fields populated + NullOmittingOpts
            // drops every other (null) property so the wire stays exactly
            // four keys.
            return SerializeCard(new CardMovedPayload(
                From: x.FromZone.ToString(),
                To: x.ToZone.ToString(),
                OwnerId: ownerId,
                Hidden: true));
        }

        // PLAN 04 — REVEALED branch only. Enrich with the same permanent
        // fields a snapshot carries (shared StateSnapshotter.BuildPermanentFields,
        // so snapshot + event agree) so the portal reducer can apply an ETB in
        // place instead of forcing a full GET /state. A → Battlefield move
        // always touches a public zone, so it is already the revealed branch —
        // the enrichment never appears on a masked variant.
        var f = StateSnapshotter.BuildPermanentFields(x.Card);
        return SerializeCard(new CardMovedPayload(
            From: x.FromZone.ToString(),
            To: x.ToZone.ToString(),
            OwnerId: ownerId,
            CardId: x.Card.InstanceId,
            CardName: x.Card.Name,
            ManaCost: x.Card.ManaCost,
            Types: x.Card.CardTypes.Select(t => t.ToString()).ToList(),
            Power: f.Power,
            Toughness: f.Toughness,
            Tapped: f.Tapped,
            SummoningSickness: f.SummoningSickness,
            Abilities: f.Abilities,
            ProducedManaColors: f.ProducedManaColors,
            Counters: f.Counters));
    }

    private static JsonElement BuildCardDrawn(CardDrawnEvent x, Player? viewer)
    {
        var ownerId = x.Player.Id;
        // Library → Hand is always hidden→hidden. Owner sees the card;
        // opponent gets count-only.
        bool mask = viewer != null && viewer.Id != ownerId;

        if (mask)
        {
            return SerializeCard(new CardDrawnPayload(
                PlayerId: ownerId,
                Hidden: true));
        }

        return SerializeCard(new CardDrawnPayload(
            PlayerId: ownerId,
            CardId: x.Card.InstanceId,
            CardName: x.Card.Name,
            ManaCost: x.Card.ManaCost,
            Types: x.Card.CardTypes.Select(t => t.ToString()).ToList()));
    }

    private static bool ShouldMaskCardForViewer(Player? viewer, Guid? ownerId, ZoneType from, ZoneType to)
    {
        // Spectator / full-reveal view: never mask.
        if (viewer == null) return false;
        // Owner sees their own private zones.
        if (ownerId.HasValue && viewer.Id == ownerId.Value) return false;
        // Non-owner: mask only when BOTH zones are hidden information
        // (Hand or Library). Any transition that touches a public zone
        // reveals the card identity — it was already visible to the
        // opponent at the time of the move.
        return BothZonesHidden(from, to);
    }

    private static bool BothZonesHidden(ZoneType a, ZoneType b)
        => IsHiddenZone(a) && IsHiddenZone(b);

    private static bool IsHiddenZone(ZoneType z) =>
        z == ZoneType.Library || z == ZoneType.Hand;

    private static JsonElement Serialize<T>(T value)
        => JsonSerializer.SerializeToElement(value, Opts);

    /// <summary>Serialize a dual-shape card payload
    /// (<see cref="CardMovedPayload"/> / <see cref="CardDrawnPayload"/>)
    /// dropping null properties so the masked + revealed variants collapse
    /// to their exact historical key sets (CR 706).</summary>
    private static JsonElement SerializeCard<T>(T value)
        => JsonSerializer.SerializeToElement(value, NullOmittingOpts);

    /// <summary>Serialize a <see cref="StackObjectPayload"/> dropping null
    /// card-identity fields so StackObjectAdded / Resolved keep their lean
    /// {stackId, controllerId, kind, description} shape while SpellCast
    /// additionally carries the populated cardId / cardName.</summary>
    private static JsonElement SerializeStack(StackObjectPayload value)
        => JsonSerializer.SerializeToElement(value, NullOmittingOpts);

    private static JsonElement Empty() => JsonDocument.Parse("{}").RootElement;

    // The next two helpers must stay aligned with
    // StateSnapshotter.SnapshotStackObject — the wire-format `kind` +
    // `description` strings emitted on the stack DTO are the same the
    // event payload promises, so the frontend can treat
    // StackObjectAddedEvent as "append this StackItem" verbatim.
    private static string StackKind(IStackObject obj) => obj switch
    {
        ISpell => "Spell",
        ITriggeredAbility => "TriggeredAbility",
        IActivatedAbility => "ActivatedAbility",
        _ => obj.GetType().Name,
    };

    private static string StackDescription(IStackObject obj) => obj switch
    {
        ISpell spell => spell.Card?.Name ?? "",
        ITriggeredAbility t => ((t.Source as ICard)?.Name ?? "") + " trigger",
        IActivatedAbility => "ability",
        _ => obj.GetType().Name,
    };
}

using System.Text.Json;
using Majik.Core.Abilities;
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

    /// <summary>Full-reveal payload (no viewer scoping). Used for the
    /// spectator broadcast and any consumer outside the per-recipient
    /// SignalR routing path.</summary>
    public static JsonElement Build(GameEvent e) => Build(e, viewer: null, turnState: null);

    /// <summary>Viewer-scoped payload. <paramref name="viewer"/> = null
    /// means full reveal (spectator). Non-null applies CR 706 masking
    /// rules to <see cref="CardMovedEvent"/> / <see cref="CardDrawnEvent"/>;
    /// the rest of the payload set is public and ignores the viewer.</summary>
    public static JsonElement Build(GameEvent e, Player? viewer) => Build(e, viewer, turnState: null);

    /// <summary>Viewer-scoped payload with turn-state context. The
    /// <paramref name="turnState"/> argument lets the builder disambiguate
    /// <see cref="PhaseStateType.Main"/> into the wire labels
    /// "PreCombatMain" / "PostCombatMain" on phase / step events — the
    /// engine's PhaseStateMachine collapses both into <c>Main</c>, so the
    /// caller (typically <see cref="GameFacade"/>) supplies the outer
    /// TurnStateMachine state it tracks via <see cref="TurnStateChangedEvent"/>.
    /// Pass <c>null</c> to keep the legacy "Main" label.</summary>
    public static JsonElement Build(GameEvent e, Player? viewer, TurnStateType? turnState) => e switch
    {
        CardMovedEvent x => BuildCardMoved(x, viewer),
        CardDrawnEvent x => BuildCardDrawn(x, viewer),
        // CardRevealedEvent: hand-reveal effects (CR 701.16) are public
        // by definition — the controller chose to show the card to all
        // players. No per-viewer masking required.
        CardRevealedEvent x => Serialize(new
        {
            cardId = x.Card.InstanceId,
            cardName = x.Card.Name,
            playerId = x.Player.Id,
            from = x.From.ToString(),
            reason = x.Reason,
        }),
        LifeChangedEvent x => Serialize(new
        {
            playerId = x.Player.Id,
            previous = x.PreviousLife,
            current = x.NewLife,
        }),
        PhaseStartedEvent x => Serialize(new
        {
            phase = PhaseLabelResolver.Resolve(x.PhaseType, turnState),
            playerId = x.Player.Id,
        }),
        PhaseEndedEvent x => Serialize(new
        {
            phase = PhaseLabelResolver.Resolve(x.PhaseType, turnState),
            playerId = x.Player.Id,
        }),
        PhaseChangedEvent x => Serialize(new
        {
            from = RemapMainLabel(x.PreviousPhase, turnState),
            to = RemapMainLabel(x.CurrentPhase, turnState),
        }),
        TurnStateChangedEvent x => Serialize(new
        {
            from = x.PreviousState?.ToString(),
            to = x.CurrentState.ToString(),
        }),
        StepStartedEvent x => Serialize(new
        {
            step = PhaseLabelResolver.Resolve(x.StepType, turnState),
            playerId = x.Player.Id,
        }),
        StepEndedEvent x => Serialize(new
        {
            step = PhaseLabelResolver.Resolve(x.StepType, turnState),
            playerId = x.Player.Id,
        }),
        TurnStartedEvent x => Serialize(new
        {
            playerId = x.Player.Id,
            turn = x.TurnNumber,
        }),
        TurnEndedEvent x => Serialize(new
        {
            playerId = x.Player.Id,
            turn = x.TurnNumber,
        }),
        ExtraPhaseAddedEvent x => Serialize(new
        {
            phase = x.PhaseType.ToString(),
        }),
        PlayerLostEvent x => Serialize(new
        {
            playerId = x.Player.Id,
        }),
        // SpellCastEvent / StackObject*Event payloads mirror StackObjectDto
        // (see Dtos.cs + StateSnapshotter.SnapshotStackObject) so the
        // frontend can patch `state.stack` from the wire delta without
        // re-fetching /state. `kind` matches the StackObjectDto.Kind
        // discriminator ("Spell" | "TriggeredAbility" | "ActivatedAbility")
        // and `description` mirrors the same composition rules used by the
        // snapshotter — keep these two builders in sync.
        SpellCastEvent x => Serialize(new
        {
            stackId = x.Spell.Id,
            controllerId = x.Spell.Controller.Id,
            cardId = (x.Spell as ISpell)?.Card?.InstanceId,
            cardName = (x.Spell as ISpell)?.Card?.Name,
            kind = "Spell",
            description = (x.Spell as ISpell)?.Card?.Name ?? "",
        }),
        StackObjectAddedEvent x => Serialize(new
        {
            stackId = x.StackObject.Id,
            controllerId = x.StackObject.Controller.Id,
            kind = StackKind(x.StackObject),
            description = StackDescription(x.StackObject),
        }),
        StackObjectResolvedEvent x => Serialize(new
        {
            stackId = x.StackObject.Id,
            controllerId = x.StackObject.Controller.Id,
            kind = StackKind(x.StackObject),
            description = StackDescription(x.StackObject),
        }),
        // Per-creature / per-player damage payload (CR 119, CR 510).
        // Frontend needs source + target Guids so it can draw a damage
        // animation from the attacker (or spell) to the victim — life
        // flash alone isn't enough at the per-creature level. We map
        // every DamageDealtEvent subclass (Combat / Spell / Ability)
        // to a single wire shape so the portal can render uniformly.
        DamageDealtEvent x => Serialize(new
        {
            sourceInstanceId = x.SourceInstanceId,
            targetInstanceId = x.TargetInstanceId,
            targetIsPlayer = x.TargetIsPlayer,
            amount = x.Amount,
            damageType = x.DamageType.ToString(),
        }),
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
            return Serialize(new
            {
                ownerId,
                from = x.FromZone.ToString(),
                to = x.ToZone.ToString(),
                hidden = true,
            });
        }

        return Serialize(new
        {
            cardId = x.Card.InstanceId,
            cardName = x.Card.Name,
            ownerId,
            manaCost = x.Card.ManaCost,
            types = x.Card.CardTypes.Select(t => t.ToString()).ToList(),
            from = x.FromZone.ToString(),
            to = x.ToZone.ToString(),
        });
    }

    private static JsonElement BuildCardDrawn(CardDrawnEvent x, Player? viewer)
    {
        var ownerId = x.Player.Id;
        // Library → Hand is always hidden→hidden. Owner sees the card;
        // opponent gets count-only.
        bool mask = viewer != null && viewer.Id != ownerId;

        if (mask)
        {
            return Serialize(new
            {
                playerId = ownerId,
                hidden = true,
            });
        }

        return Serialize(new
        {
            cardId = x.Card.InstanceId,
            cardName = x.Card.Name,
            playerId = ownerId,
            manaCost = x.Card.ManaCost,
            types = x.Card.CardTypes.Select(t => t.ToString()).ToList(),
        });
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

    private static JsonElement Empty() => JsonDocument.Parse("{}").RootElement;

    // PhaseChangedEvent carries the raw IState.Name string from either the
    // TurnStateMachine ("PreCombatMain", "Combat", …) or the
    // PhaseStateMachine ("Main", "Untap", …). When we see the ambiguous
    // "Main" label and the caller has supplied a TurnStateType, lift the
    // wire string into the disambiguated form the frontend expects.
    private static string? RemapMainLabel(string? raw, TurnStateType? turnState)
    {
        if (raw != PhaseStateType.Main.ToString()) return raw;
        return PhaseLabelResolver.Resolve(PhaseStateType.Main, turnState);
    }

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

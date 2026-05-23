using System.Text.Json;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Spells;
using Majik.Core.Stack;

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
/// </summary>
public static class EventPayloadBuilder
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static JsonElement Build(GameEvent e) => e switch
    {
        CardMovedEvent x => Serialize(new
        {
            cardId = x.Card.InstanceId,
            cardName = x.Card.Name,
            from = x.FromZone.ToString(),
            to = x.ToZone.ToString(),
        }),
        CardDrawnEvent x => Serialize(new
        {
            cardId = x.Card.InstanceId,
            cardName = x.Card.Name,
            playerId = x.Player.Id,
        }),
        LifeChangedEvent x => Serialize(new
        {
            playerId = x.Player.Id,
            previous = x.PreviousLife,
            current = x.NewLife,
        }),
        PhaseStartedEvent x => Serialize(new
        {
            phase = x.PhaseType.ToString(),
            playerId = x.Player.Id,
        }),
        PhaseEndedEvent x => Serialize(new
        {
            phase = x.PhaseType.ToString(),
            playerId = x.Player.Id,
        }),
        PhaseChangedEvent x => Serialize(new
        {
            from = x.PreviousPhase,
            to = x.CurrentPhase,
        }),
        StepStartedEvent x => Serialize(new
        {
            step = x.StepType.ToString(),
            playerId = x.Player.Id,
        }),
        StepEndedEvent x => Serialize(new
        {
            step = x.StepType.ToString(),
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
        GameStartedEvent => Empty(),
        _ => Empty(),
    };

    private static JsonElement Serialize<T>(T value)
        => JsonSerializer.SerializeToElement(value, Opts);

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

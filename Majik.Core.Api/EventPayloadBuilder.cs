using System.Text.Json;
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
        SpellCastEvent x => Serialize(new
        {
            stackId = x.Spell.Id,
            controllerId = x.Spell.Controller.Id,
            cardName = (x.Spell as ISpell)?.Card?.Name,
        }),
        StackObjectAddedEvent x => Serialize(new
        {
            stackId = x.StackObject.Id,
            kind = x.StackObject.GetType().Name,
        }),
        StackObjectResolvedEvent x => Serialize(new
        {
            stackId = x.StackObject.Id,
            kind = x.StackObject.GetType().Name,
        }),
        GameStartedEvent => Empty(),
        _ => Empty(),
    };

    private static JsonElement Serialize<T>(T value)
        => JsonSerializer.SerializeToElement(value, Opts);

    private static JsonElement Empty() => JsonDocument.Parse("{}").RootElement;
}

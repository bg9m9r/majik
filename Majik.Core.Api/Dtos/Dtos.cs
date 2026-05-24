using System.Text.Json;

namespace Majik.Core.Api.Dtos;

/// <summary>Top-level read-only snapshot of a game suitable for JSON transport.</summary>
public sealed record GameStateDto(
    Guid GameId,
    int TurnNumber,
    string? Phase,
    Guid ActivePlayerId,
    IReadOnlyList<PlayerDto> Players,
    IReadOnlyList<StackObjectDto> Stack);

public sealed record PlayerDto(
    Guid Id,
    string Name,
    int Life,
    bool HasLost,
    ManaPoolDto Mana,
    ZoneDto Hand,
    ZoneDto Battlefield,
    ZoneDto Graveyard,
    ZoneDto Library,
    ZoneDto Exile);

public sealed record ZoneDto(IReadOnlyList<CardSnapshotDto> Cards);

public sealed record CardSnapshotDto(
    Guid InstanceId,
    string Name,
    string ManaCost,
    IReadOnlyList<string> Types,
    int? Power,
    int? Toughness,
    bool Tapped,
    bool SummoningSickness,
    IReadOnlyList<AbilityDto> Abilities,
    string ProducedManaColors = "");

public sealed record AbilityDto(string Kind, string Description);

public sealed record StackObjectDto(
    Guid Id,
    string Kind,
    Guid? ControllerId,
    string Description);

public sealed record ManaPoolDto(int Generic, int White, int Blue, int Black, int Red, int Green, int Colorless)
{
    public static readonly ManaPoolDto Empty = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Wire-format event. <see cref="Payload"/> holds type-specific data as raw JSON.</summary>
public sealed record EventDto(Guid EventId, string Type, DateTime At, JsonElement Payload);

/// <summary>
/// Per-emission envelope grouping a public <see cref="EventDto"/> with
/// optional per-player variants. <see cref="PerPlayer"/> is null for
/// events whose payload is identical for every viewer (the common case);
/// non-null for CR 706 hidden-information events
/// (<see cref="Majik.Core.Events.CardMovedEvent"/>,
/// <see cref="Majik.Core.Events.CardDrawnEvent"/> when both zones are
/// hidden — e.g. a draw, or a return-to-library effect) so the bridge
/// can route a per-recipient broadcast instead of a group fan-out. Keys
/// in <see cref="PerPlayer"/> are engine <c>Player.Id</c> guids; values
/// are pre-masked <see cref="EventDto"/>s scoped to that viewer.
///
/// <see cref="Public"/> is the spectator / debug variant (full reveal)
/// and is always populated. The <see cref="GameFacade.Subscribe(Action{EventDto})"/>
/// legacy subscription hands subscribers <see cref="Public"/>; bridge
/// code uses <see cref="GameFacade.SubscribeEnvelopes"/> to access the
/// per-player dictionary.
/// </summary>
public sealed record EventEnvelope(
    EventDto Public,
    IReadOnlyDictionary<Guid, EventDto>? PerPlayer);

/// <summary>
/// Server → client envelope signalling that the engine is awaiting a
/// command from <see cref="PlayerId"/>. The client renders the
/// appropriate UI and responds via POST /games/{id}/commands with a
/// command of one of the kinds in <see cref="ExpectedKinds"/>. The
/// envelope intentionally carries no card data — opponent visibility is
/// unaffected (the opponent already knows the prompted player is
/// thinking).
/// </summary>
public sealed record PromptDto(
    Guid GameId,
    Guid PlayerId,
    IReadOnlyList<string> ExpectedKinds);

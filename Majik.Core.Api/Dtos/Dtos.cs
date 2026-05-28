using System.Text.Json;

namespace Majik.Core.Api.Dtos;

/// <summary>Top-level read-only snapshot of a game suitable for JSON transport.</summary>
public sealed record GameStateDto(
    Guid GameId,
    int TurnNumber,
    string? Phase,
    Guid ActivePlayerId,
    IReadOnlyList<PlayerDto> Players,
    IReadOnlyList<StackObjectDto> Stack,
    Guid? YouPlayerId = null);

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

public sealed record AbilityDto(string Kind, string Description, Guid? Id = null);

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
/// command of one of the kinds in <see cref="ExpectedKinds"/>.
/// <para>
/// <see cref="Candidates"/> + <see cref="Label"/> are populated only for
/// prompts that carry an engine-pre-filtered card list the player picks
/// from (currently library-search via
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseLibraryPickAsync"/>).
/// Null on every other prompt kind. Including the candidate snapshot
/// here is necessary because the library is otherwise hidden in
/// <see cref="GameStateDto"/> (CR 706) — without it the portal has no
/// safe way to render the choice. Opponent visibility is unaffected
/// (opponents already know the searcher is thinking).
/// </para>
/// <para>
/// <see cref="LibraryView"/> is the full snapshot of the searching player's
/// library (top-to-bottom order) at the time the search prompt fires
/// (CR 701.19a — while searching, a player may look at their own library).
/// Non-null only on library-search prompts; null on every other prompt kind.
/// <c>Candidates.Select(c =&gt; c.InstanceId)</c> is the engine-filtered
/// eligible subset — the portal highlights those cards and mutes the rest
/// so it renders like flipping through the deck.
/// Serialized as <c>libraryView</c> (camelCase) on the wire via
/// System.Text.Json default policy.
/// </para>
/// </summary>
public sealed record PromptDto(
    Guid GameId,
    Guid PlayerId,
    IReadOnlyList<string> ExpectedKinds,
    IReadOnlyList<CardSnapshotDto>? Candidates = null,
    string? Label = null,
    IReadOnlyList<CardSnapshotDto>? LibraryView = null,
    /// <summary>
    /// CR 701.42 — peeked top N of the searching player's library on a
    /// surveil prompt, in top-to-bottom order. The client surfaces each
    /// card with two choices ("to graveyard" vs "keep on top") and assembles
    /// a <c>ChooseSurveilCommand</c> partitioning the peeked set.
    /// Non-null only on surveil prompts; null on every other prompt kind.
    /// Privacy: shipped per-recipient like <see cref="LibraryView"/>, never
    /// broadcast to opponents or spectators.
    /// </summary>
    IReadOnlyList<CardSnapshotDto>? SurveilView = null,
    /// <summary>
    /// CR 117.x / 605.1 — Yes/No prompt envelope (e.g. shock-land
    /// "pay 2 life?" choice). Non-null only on Yes/No prompts; null on
    /// every other prompt kind. The portal renders a modal showing
    /// <see cref="YesNoViewDto.Question"/> (and, when present,
    /// <see cref="YesNoViewDto.SourceCardName"/> as the modal header so
    /// the player knows which permanent triggered the prompt), and
    /// dispatches a <c>ChooseYesNoCommand</c> with the bool answer on
    /// click.
    /// </summary>
    YesNoViewDto? YesNoView = null);

/// <summary>
/// Per-prompt body for CR 117.x / 605.1 Yes/No prompts surfaced on
/// <see cref="PromptDto.YesNoView"/>. <see cref="YesLabel"/> /
/// <see cref="NoLabel"/> default to "Yes" / "No" for plain may-clause
/// prompts; callers (engine-side) may override when the wire UI benefits
/// from a card-specific phrasing ("Pay 2 life" / "Enter tapped").
/// <see cref="SourceCardName"/> is optional — set by binder-chain callers
/// that know the triggering permanent (e.g. shock-land replacement passes
/// the land's name), so the portal modal can be titled "Overgrown Tomb"
/// instead of just "Choose".
/// </summary>
public sealed record YesNoViewDto(
    string Question,
    string YesLabel = "Yes",
    string NoLabel = "No",
    string? SourceCardName = null);

/// <summary>
/// Per-viewer auto-pass policy. Mirrors the portal-side
/// <c>AutoPassDeps</c> contract so the engine can apply the SAME
/// "should I pass this dead priority window?" decision server-side and
/// skip the HTTP round-trip volley (Slice 5a).
///
/// <list type="bullet">
///   <item><see cref="FullControl"/> — when <c>true</c>, the user is
///     holding the Full Control modifier (Ctrl). Auto-pass is suppressed
///     for every priority window; the human MUST be prompted.</item>
///   <item><see cref="PhaseStops"/> — sparse map from wire phase label
///     ("Untap", "Upkeep", "PreCombatMain", "PostCombatMain", …) to the
///     side the stop applies to: <c>"mine"</c> (stop on the viewer's
///     own turn) or <c>"theirs"</c> (stop on the opponent's turn).
///     Keys/values match the portal's <c>PhaseStops</c> shape.</item>
/// </list>
///
/// <para>Defaults: <see cref="FullControl"/> = false,
/// <see cref="PhaseStops"/> = standard MTG-client opponent-turn stops
/// (<c>BeginningOfCombat</c> + <c>End</c>, both <c>"theirs"</c>). The
/// engine auto-passes every other window on the opponent's turn — the
/// user is woken up at the two windows where they realistically want to
/// respond (pre-attacker-declaration + end step). Own-turn windows have
/// no stops by default; the empty-action gate (PriorityKinds.IsPassOnly)
/// auto-passes any dead window and the user still drives every action
/// they actually have available.</para>
/// </summary>
public sealed record AutoPassPrefs(
    bool FullControl,
    IReadOnlyDictionary<string, string> PhaseStops)
    : Majik.Core.Game.IAutoPassPrefsView
{
    /// <summary>
    /// Default prefs: FullControl off, opponent-turn stops at
    /// <c>BeginningOfCombat</c> and <c>End</c>. Mirrors the default
    /// hold-priority pattern shipped by MTG Arena / MTGO when Full
    /// Control is off — the human is auto-passed everywhere except the
    /// two windows where they typically want to interact on the
    /// opponent's turn (cast an instant before attackers are declared,
    /// or before the turn ends).
    /// </summary>
    public static readonly AutoPassPrefs Default = new(
        FullControl: false,
        PhaseStops: new Dictionary<string, string>
        {
            [nameof(Majik.Core.StateMachine.PhaseStateType.BeginningOfCombat)] = "theirs",
            [nameof(Majik.Core.StateMachine.PhaseStateType.End)] = "theirs",
        });
}

using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// CR 702.95 — Leyline keyword opening-hand alt-cost. Subscribes to
/// <see cref="OpeningHandCheckEvent"/> and for each card in the player's
/// opening hand tagged with <see cref="KeywordAbility"/>
/// <c>"OpeningHandLeyline"</c>, prompts the player's agent
/// (<see cref="IPlayerAgent.ChooseYesNoAsync"/> /
/// <see cref="BotIntent.OpeningHandLeyline"/>) "begin with [name] on the
/// battlefield?". On a yes-answer the card is moved hand → battlefield
/// via <see cref="ZoneService.MoveCard"/> with
/// <see cref="Card.WasCast"/> stamped <c>false</c> (CR 113.5 — Leyline
/// is "put onto the battlefield", not cast — so Containment Priest's
/// non-token-creature exile rider sees it correctly).
///
/// ## Ordering
///
/// Iterates the snapshot in <see cref="OpeningHandCheckEvent.OpeningHand"/>
/// order — the snapshot is taken AFTER mulligan resolution, so all
/// London-mulligan bottom selections have already happened. Multiple
/// Leylines in the same hand each get their own prompt; a "no" on one
/// does not skip the others.
///
/// ## Why a subscriber rather than per-Leyline wiring
///
/// All five+ Leyline cards (Void, Sanctity, Anguish, Lightning,
/// Combustion, plus the rest of the cycle) print the identical opening-
/// hand alt-cost text. A shared subscriber keyed off the
/// <c>OpeningHandLeyline</c> keyword marker keeps the surface in one
/// place — adding a new Leyline becomes "tag the factory with
/// <c>KeywordAbility(\"OpeningHandLeyline\")</c>" with no further
/// per-card wiring required.
///
/// ## Lifecycle
///
/// Attach exactly once at game start (after <see cref="GameDriver"/>
/// constructs the event bus) via <see cref="Attach"/>. The subscriber
/// stays attached for the full game — events only fire once per player
/// during the mulligan-resolution boundary, so there's no per-turn
/// detach concern.
/// </summary>
public sealed class OpeningHandLeylineAlternativeCost
{
    /// <summary>Marker keyword that flags a card as a Leyline-cycle
    /// opening-hand candidate. Factories ship this via
    /// <c>card.AddAbility(new KeywordAbility(LeylineKeyword, card, owner))</c>.</summary>
    public const string LeylineKeyword = "OpeningHandLeyline";

    private readonly ZoneService _zoneService;
    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _agents;

    public OpeningHandLeylineAlternativeCost(
        ZoneService zoneService,
        IReadOnlyDictionary<Player, IPlayerAgent> agents)
    {
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
    }

    /// <summary>Wire this subscriber to the given event bus. Returns the
    /// async handler delegate so callers can unsubscribe if desired
    /// (tests typically just let it ride to scope end).</summary>
    public Func<OpeningHandCheckEvent, Task> Attach(IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        Func<OpeningHandCheckEvent, Task> handler = HandleAsync;
        bus.Subscribe(handler);
        return handler;
    }

    /// <summary>Handle an opening-hand check synchronously — drives the
    /// per-card prompt loop. Exposed for direct test invocation without
    /// constructing a full event bus.</summary>
    public async Task HandleAsync(OpeningHandCheckEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (!_agents.TryGetValue(evt.Player, out var agent) || agent == null)
        {
            // No agent bound for this player — silently skip. Matches
            // GameDriver's contract that every player has an agent, but
            // defensive against partially-wired test harnesses.
            return;
        }

        // Snapshot the candidates up front: HandleAsync may move cards
        // out of hand mid-iteration. The event already carries an
        // independent snapshot, but we also need the per-card Leyline
        // tag — filter to tagged cards once, then prompt in order.
        var candidates = new List<ICard>();
        foreach (var card in evt.OpeningHand)
        {
            if (IsLeyline(card))
            {
                candidates.Add(card);
            }
        }

        foreach (var card in candidates)
        {
            // Defensive: skip if the card somehow already left the hand
            // (a previous Leyline's resolution shouldn't pull other
            // Leylines, but stay safe against future shared-state quirks).
            if (card.Zone != ZoneType.Hand) continue;

            var answer = await agent.ChooseYesNoAsync(
                $"Begin with {card.Name} on the battlefield?",
                BotIntent.OpeningHandLeyline);

            if (!answer) continue;

            // CR 113.5 — the Leyline is "put onto the battlefield", not
            // cast. Stamp WasCast = false so Containment Priest's
            // "if it wasn't cast, exile it instead" rider and any
            // future cast-gated replacement see the correct posture.
            // (ZoneService.MoveCard mirrors the field onto the in-flight
            // intent, so both the live card and the intent agree.)
            if (card is Card concrete)
            {
                concrete.SetWasCast(false);
            }

            _zoneService.MoveCard(
                card,
                ZoneType.Hand,
                ZoneType.Battlefield,
                evt.Player);
        }
    }

    private static bool IsLeyline(ICard card)
    {
        foreach (var ability in card.Abilities)
        {
            if (ability is KeywordAbility kw &&
                string.Equals(kw.Keyword, LeylineKeyword, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

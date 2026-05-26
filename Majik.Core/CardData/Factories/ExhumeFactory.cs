using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Exhume (Urza's Saga, {1}{B}).
///
/// Sorcery. Oracle text:
///   "Each player returns a creature card from their graveyard to the
///    battlefield."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}.
/// - Resolve effect via <see cref="BuildResolveEffect"/>: for each player
///   supplied by <paramref name="allPlayersResolver"/>, that player picks
///   one creature card from their own graveyard and returns it to the
///   battlefield under their control (CR 701.20 — graveyard → battlefield).
///   - <b>Agent prompt</b>: per-player <see cref="IPlayerAgent.ChooseFromPileAsync"/>
///     via <see cref="AgentRegistry"/> (Intent: <see cref="BotIntent.None"/>
///     — "returning your own creature" is bilateral upside, not pure
///     removal). No agent registered → deterministic first-creature pick,
///     mirroring <see cref="ReanimateFactory"/>'s v1 posture.
///   - <b>Empty graveyard</b>: that player is skipped (CR 700.6 — "a
///     creature card" is a non-targeting pick, not a target; no fizzle
///     just because one player can't comply).
///   - <b>Single-arg overload</b>: scans the caster's graveyard only,
///     same shape as <see cref="ReanimateFactory.BuildResolveEffect(Player, ZoneService?, Func{IReadOnlyList{Player}}?)"/>.
/// - Each return routes through <see cref="ZoneService.MoveCard"/> when
///   supplied so ETB triggers fire on the reanimated creatures
///   (CR 603.6a).
///
/// ## Deferred (v1 gaps)
/// - <b>Simultaneous "each player"</b>: CR 101.4 — players act in APNAP
///   order for simultaneous decisions. v1 iterates the resolver-supplied
///   player list in order; the resolver's caller is expected to seed APNAP
///   ordering. The visible end-state is identical because the picks are
///   independent (each player's choice is bounded by their own graveyard
///   contents and isn't affected by the other player's pick).
/// - <b>"Returns" is non-targeting</b>: no <see cref="Majik.Core.Targeting.TargetRequest"/>
///   wiring — picks are made at resolution. Matches Animate Dead's
///   graveyard-card-as-target gap (the engine's target plumbing is
///   battlefield-permanent-typed in v1).
/// </summary>
[CardName("Exhume")]
public static class ExhumeFactory
{
    public const string CardName = "Exhume";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>Printed oracle text — cross-checked at import time
    /// against Scryfall.</summary>
    public const string OracleText =
        "Each player returns a creature card from their graveyard to the battlefield.";

    /// <summary>
    /// Build an Exhume sorcery owned by <paramref name="owner"/>. Card
    /// shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Exhume's resolve effect — each supplied player simultaneously
    /// returns a creature card from their own graveyard to the
    /// battlefield under their control. Single-arg overload restricts the
    /// scan to <paramref name="caster"/> only (mirrors ReanimateFactory).
    /// </summary>
    /// <param name="caster">Spell controller — used as the default
    /// player-set when no resolver is supplied.</param>
    /// <param name="zoneService">Optional. When supplied each graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers fire (CR 603.6a).</param>
    /// <param name="allPlayersResolver">Optional. When supplied every
    /// player it returns gets the choose-a-creature prompt; otherwise
    /// only the caster does. Production callers thread the full table
    /// here (CR 101.4 APNAP-ordered).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null,
        Func<IReadOnlyList<Player>>? allPlayersResolver = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: each player returns a creature card from their graveyard to the battlefield",
                () => Resolve(caster, zoneService, allPlayersResolver)),
        };
    }

    /// <summary>
    /// Resolution helper. Each player picks one creature card from their
    /// own graveyard; the picked card moves to that same player's
    /// battlefield. CR 110.2 — controller follows destination zone.
    /// </summary>
    private static void Resolve(
        Player caster,
        ZoneService? zoneService,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        var players = allPlayersResolver?.Invoke()
            ?? (IReadOnlyList<Player>)new[] { caster };

        foreach (var p in players)
        {
            if (p == null) continue;

            var candidates = p.Zones.Graveyard.GetCards()
                .OfType<Creature>()
                .Cast<ICard>()
                .ToList();
            if (candidates.Count == 0) continue;

            // CR 701.19 — non-targeting "returns" pick. Agent prompts the
            // player; no agent → deterministic first-creature fallback.
            var agent = AgentRegistry.Get(p);
            ICard? pick = agent != null
                ? agent.ChooseFromPileAsync(
                        p,
                        candidates,
                        $"{CardName}: choose a creature card from your graveyard to return to the battlefield",
                        BotIntent.None)
                    .GetAwaiter().GetResult()
                : candidates[0];
            if (pick == null) continue;

            // CR 701.20 — graveyard → that player's battlefield. Fx
            // helper routes through ZoneService when supplied so ETB
            // triggers fire (CR 603.6a).
            Fx.ReturnFromGraveyardToBattlefield(pick, p, zoneService);
        }
    }
}

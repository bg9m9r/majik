using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
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
///   in <paramref name="allPlayersResolver"/> (single-arg overload falls
///   back to just the caster), the first creature card in that player's
///   graveyard is moved to that player's own battlefield under that
///   player's control (CR 110.2 — "their" battlefield). Each player's
///   choice is independent and processed in APNAP-style order as supplied
///   by the resolver.
/// - Move routes through <see cref="ZoneService.MoveCard"/> when supplied
///   so ETB triggers fire on each reanimated creature (CR 603.6a). Raw-
///   zone fallback handles owner/controller bookkeeping for tests that
///   don't wire a ZoneService.
///
/// ## Deferred (v1 gaps)
/// - <b>Per-player choice prompt</b>: "returns A creature card" is each
///   player's own choice from their own graveyard. v1 picks the first
///   creature card deterministically (same shape as
///   <see cref="ReanimateFactory"/> + <see cref="AnimateDeadFactory"/>).
/// - <b>Simultaneous resolution + APNAP order</b>: the printed wording is
///   "each player returns ...". Per CR 101.4 / 608.2g, in a multi-player
///   game the active player's choice is made first, then each other in
///   turn order, then the cards are returned simultaneously. v1 iterates
///   the resolver's order and moves each card eagerly; ETB triggers
///   accumulate on the stack in iteration order. Wire APNAP into the
///   resolver to match a real cast flow.
/// </summary>
[CardName("Exhume")]
public static class ExhumeFactory
{
    public const string CardName = "Exhume";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>Printed oracle text — informational. Kept here so the
    /// data-driven import path can cross-check the named factory against
    /// Scryfall.</summary>
    public const string OracleText =
        "Each player returns a creature card from their graveyard to the battlefield.";

    /// <summary>
    /// Build an Exhume sorcery owned by <paramref name="owner"/>. Card
    /// shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
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
    /// Build Exhume's resolve effect — each player returns a creature
    /// card from their graveyard to the battlefield under their own
    /// control.
    /// </summary>
    /// <param name="caster">Spell controller. Used as the lone fallback
    /// player when <paramref name="allPlayersResolver"/> is null (so the
    /// minimum two-player wording still does something in single-player
    /// tests).</param>
    /// <param name="zoneService">Optional. When supplied each graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers fire (CR 603.6a).</param>
    /// <param name="allPlayersResolver">Optional. When supplied every
    /// returned player picks a creature from their OWN graveyard. When
    /// null, only the caster is processed.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null,
        Func<IReadOnlyList<Player>>? allPlayersResolver = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: each player reanimates a creature card from their own graveyard",
                () => Resolve(caster, zoneService, allPlayersResolver)),
        };
    }

    /// <summary>
    /// Shared resolution helper — iterates the supplied player list,
    /// picking each player's first creature card and moving it to that
    /// player's OWN battlefield (CR 110.2). Each step is independent;
    /// a player with no creature card in their graveyard is skipped
    /// without aborting the overall effect.
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

            var pick = p.Zones.Graveyard.GetCards()
                .OfType<Creature>()
                .FirstOrDefault();
            if (pick == null) continue;

            // CR 110.2 — each card returns under its OWN player's control
            // (the player whose graveyard it came from), not the caster's.
            // Same routing as Reanimate but the destination controller is
            // the per-iteration player, not the caster.
            Fx.ReturnFromGraveyardToBattlefield(pick, p, zoneService);
        }
    }
}

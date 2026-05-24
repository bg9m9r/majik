using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reanimate (Tempest, {B}).
///
/// Sorcery. Oracle text:
///   "Put target creature card from a graveyard onto the battlefield
///    under your control. You lose life equal to its mana value."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}.
/// - On-resolve effect via <see cref="BuildResolveEffect"/>:
///     1. Scan candidate graveyards (caster's only with the single-arg
///        overload; <paramref name="allPlayersResolver"/> opt-in to scan
///        every player's graveyard). v1 deterministic pick: the first
///        creature card found.
///     2. Move that card from its graveyard to the caster's battlefield.
///        Routes through <see cref="ZoneService.MoveCard"/> when supplied
///        so ETB triggers on the reanimated creature fire (CR 603.6a) and
///        owner-of-zone bookkeeping stays consistent across players.
///     3. Caster loses life equal to the reanimated creature's mana value
///        (CR 202.3b — converted mana cost / mana value, X = 0 by default).
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt</b>: "target creature card from a graveyard"
///   needs an agent-driven choose-one-from-multi-graveyards prompt. v1
///   picks deterministically — same shape as Priest of Fell Rites.
/// - <b>"Onto the battlefield under your control"</b>: control is set
///   via <see cref="Permanent.SetController"/> after the zone move. The
///   ZoneService path uses its built-in "controller follows destination"
///   semantics; the raw-zone fallback path is explicit.
/// </summary>
[CardName("Reanimate")]
public static class ReanimateFactory
{
    public const string CardName = "Reanimate";
    public const string PrintedManaCost = "{B}";

    /// <summary>Printed oracle text. Kept here so the data-driven import
    /// path can cross-check the named factory against Scryfall.</summary>
    public const string OracleText =
        "Put target creature card from a graveyard onto the battlefield " +
        "under your control. You lose life equal to its mana value.";

    /// <summary>
    /// Build a Reanimate sorcery owned by <paramref name="owner"/>. Card
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
    /// Build Reanimate's resolve effect — reanimate target creature card
    /// from any graveyard (deterministic first-match v1) and the caster
    /// loses life equal to its mana value.
    /// </summary>
    /// <param name="caster">Spell controller — destination battlefield +
    /// life-loss target.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers / replacements fire on the reanimated creature
    /// (CR 603.6a).</param>
    /// <param name="allPlayersResolver">Optional. When supplied every
    /// player's graveyard is scanned; otherwise only the caster's.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null,
        Func<IReadOnlyList<Player>>? allPlayersResolver = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: reanimate target creature card from a graveyard; caster loses life = its mana value",
                () => Resolve(caster, zoneService, allPlayersResolver)),
        };
    }

    /// <summary>
    /// Shared resolution helper — picks the first creature card across the
    /// configured graveyards, moves it to the caster's battlefield, and
    /// makes the caster lose life equal to its mana value. CR 117.x —
    /// "target" effect with no legal target is a no-op (also covers the
    /// life-loss tail: no creature → no life loss).
    /// </summary>
    private static void Resolve(
        Player caster,
        ZoneService? zoneService,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        var candidatePlayers = allPlayersResolver?.Invoke()
            ?? (IReadOnlyList<Player>)new[] { caster };

        foreach (var p in candidatePlayers)
        {
            if (p == null) continue;

            var pick = p.Zones.Graveyard.GetCards()
                .OfType<Creature>()
                .FirstOrDefault();
            if (pick == null) continue;

            // CR 701.20 — graveyard → battlefield. The Effects facade
            // routes through ZoneService when supplied so ETB triggers
            // fire (CR 603.6a); raw-zone fallback otherwise.
            Fx.ReturnFromGraveyardToBattlefield(pick, caster, zoneService);

            // CR 202.3b — mana value (the printed total mana cost, X = 0).
            // Lose life happens AFTER the move (CR 608.2c — resolve in
            // printed order) and is unconditional given a legal target.
            Fx.LoseLife(caster, pick.ManaCostValue.TotalValue);

            return; // CR 700.6 — "target" is a single object
        }
    }
}

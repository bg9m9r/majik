using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Etched Oracle (Fifth Dawn, {4}).
///
/// Artifact Creature — Human Wizard 1/1. Oracle text:
///   "Sunburst (This enters with a +1/+1 counter on it for each color of
///    mana spent to cast it.)"
///   "{2}, Remove three +1/+1 counters from Etched Oracle: Each player
///    draws three cards."
///
/// ## Implemented (v1)
/// - Artifact Creature {4} 1/1 with owner/controller wired + Human Wizard
///   subtypes.
/// - <b>Sunburst (CR 702.44)</b> wired via the shared
///   <see cref="SunburstFactory.Build"/> primitive. As an artifact
///   creature, Etched Oracle's Sunburst lands +1/+1 counters at ETB time
///   (CR 702.44a). The keyword reads <see cref="Card.PendingCastColors"/>
///   stamped by <see cref="Majik.Core.Game.TurnDriver"/> after the mana
///   resolver computes "colors of mana spent" from the cross-spend pool
///   diff. When wired against the supplied <paramref name="replacements"/>
///   bus, Hardened Scales / Doubling Season bumps apply.
/// - <b>Activated ability (CR 602.1)</b>: {2}, remove three +1/+1
///   counters: each player draws three cards. Mana cost wired as
///   <see cref="ManaCostCost"/>; the +1/+1-counter-removal cost is
///   performed by the resolve closure (no
///   <see cref="AdditionalCost.RemoveCounters"/> primitive yet — same
///   posture Engineered Explosives takes for its sacrifice cost). On
///   resolve the effect iterates every player in
///   <paramref name="allPlayersResolver"/> (or just the controller when
///   no resolver supplied, single-arg dispatcher path) and routes each
///   through <see cref="Fx.DrawCards"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Counter-removal additional cost</b>: same gap shape as the
///   sacrifice / discard cost family. Today the resolve closure
///   removes the three +1/+1 counters inline; a future
///   <c>AdditionalCost.RemoveCounters(card, type, n)</c> primitive
///   should hoist it to the declared cost list so cost-validation /
///   activation-legality scans see it.
/// - <b>Each-player draw ordering / APNAP</b>: CR 101.4 (each player
///   in APNAP order draws). v1 iterates the resolver's player list as
///   given; production callers should pass APNAP-ordered players.
/// </summary>
[CardName("Etched Oracle")]
public static class EtchedOracleFactory
{
    public const string CardName = "Etched Oracle";
    public const string PrintedManaCost = "{4}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int CountersToRemove = 3;
    public const int CardsDrawn = 3;

    /// <summary>
    /// Construct Etched Oracle with no live runtime wiring. Sunburst's
    /// ETB counters arrive via the +1/+1 branch when
    /// <see cref="Card.PendingCastColors"/> is set; the activated
    /// ability targets only the controller (single-arg dispatcher path).
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Etched Oracle. When <paramref name="replacements"/> is
    /// supplied, Sunburst's counter placement routes through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season bumps apply. When <paramref name="allPlayersResolver"/> is
    /// supplied, the activated ability draws three cards for every
    /// player in the list; otherwise only the controller draws.
    /// </summary>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        // CR 301.1 / 302.1 — Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sunburst (CR 702.44) — shared primitive. Reads PendingCastColors
        // at ETB resolve time and stamps +1/+1 counters (CR 702.44a —
        // Etched Oracle is a Creature). Routes through CountersService.Add
        // so Hardened Scales bumps the count.
        // ----------------------------------------------------------------
        SunburstFactory.Build(card, replacements);

        // ----------------------------------------------------------------
        // Activated ability (CR 602.1): {2}, remove three +1/+1 counters:
        // each player draws three cards.
        // - Mana cost: {2} → ManaCostCost.
        // - Counter-removal cost: inline in the resolve closure (no
        //   AdditionalCost.RemoveCounters primitive yet — same stub
        //   posture Engineered Explosives takes for sacrifice).
        // - Effect: iterate every player (resolver-supplied; falls back
        //   to controller-only when no resolver) and route each through
        //   Fx.DrawCards(player, 3).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: each player draws {CardsDrawn} cards",
            () =>
            {
                // Counter-removal payment is a no-op stub at the engine
                // level — perform it inline so visible state matches.
                if (card.Counters.Count(CounterType.PlusOnePlusOne) < CountersToRemove)
                {
                    // CR 602.1 — illegal activation. Bail out cleanly;
                    // the test surface checks the counter total before
                    // activating, but future runtime gates should reject
                    // earlier via CostValidator.
                    return;
                }
                card.Counters.Remove(CounterType.PlusOnePlusOne, CountersToRemove);

                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    Fx.DrawCards(p, CardsDrawn);
                }
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
            },
            effects: new IEffect[] { drawEffect });

        card.AddAbility(ability);

        return card;
    }
}

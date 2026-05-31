using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Consult the Star Charts (Khans of Tarkir, {1}{U}).
///
/// Instant. Oracle text (Scryfall):
///   "Kicker {1}{U} (You may pay an additional {1}{U} as you cast this spell.)
///    Look at the top X cards of your library, where X is the number of lands
///    you control. Put one of those cards into your hand. If this spell was
///    kicked, put two of those cards into your hand instead. Put the rest on
///    the bottom of your library in a random order."
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {1}{U} (blue).
/// - Kicker {1}{U} (CR 702.33) — a real <see cref="KickerAdditionalCost"/>
///   rider, exposed via <see cref="BuildAdditionalCost"/> and surfaced to the
///   bot's alt-cost discovery through
///   <see cref="KickerAltCostProbe.DefaultLookup"/> (registered there as a
///   {1}{U}-kicker card). The resolve body reads <see cref="Card.WasKicked"/>
///   at resolution time (CR 702.33b — "if this spell was kicked" is checked
///   when the spell resolves; the cast-time payment stamps the sentinel and
///   <see cref="Majik.Core.Game.SpellCastFlow"/> clears it after resolution).
/// - Resolve effect (<see cref="BuildResolveEffect"/>):
///   1. X = number of lands the caster controls (battlefield permanents with
///      <see cref="CardType.Land"/>). CR 700.3 / 109.5 — counted at resolution.
///   2. Peek the top X cards via <see cref="ScryAction.Peek"/> (tolerates a
///      short library — returns up to X).
///   3. Put one of those cards into the caster's hand (two if the spell was
///      kicked). The controller picks via the registered
///      <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>; the pre-agent
///      default keeps the first peeked card(s) (deterministic, matching
///      <see cref="AnticipateFactory"/> / every look-and-pick factory).
///   4. Put the rest on the bottom of the library "in a random order".
///      Hidden information — the exact bottom order is unobservable, so v1
///      bottoms them in peek order (legal; the randomization is cosmetic).
///
/// ## Edge cases
/// - X = 0 (no lands): peek returns empty; effect is a no-op. Consult has no
///   "draw" clause, so the empty-library SBA does NOT fire (CR 704.5b).
/// - Short library (fewer than X cards, or fewer than 2 when kicked): take
///   what is reachable; never throws.
///
/// ## Analogues
/// - Look-top-N + pick: <see cref="AnticipateFactory"/>.
/// - Kicker rider + resolve-time <c>WasKicked</c> branch:
///   <see cref="BurstLightningFactory"/>.
///
/// CR rule references: 701.18 (look at), 601.2 (casting), 501.4 (instants
/// in any step), 702.33 / 702.33b (kicker), 704.5b (empty-library SBA).
/// </summary>
[CardName("Consult the Star Charts")]
public static class ConsultTheStarChartsFactory
{
    public const string CardName = "Consult the Star Charts";
    public const string PrintedManaCost = "{1}{U}";
    public const string KickerCostText = "{1}{U}";

    private const int UnkickedTake = 1;
    private const int KickedTake = 2;

    /// <summary>CardDef DSL — card shape only. Resolve body lives in
    /// <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Construct Consult the Star Charts' kicker <see cref="IAdditionalCost"/>
    /// for the supplied <paramref name="card"/> instance — layer onto the cast
    /// via <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter. CR 702.33.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    /// <summary>
    /// Build Consult the Star Charts' resolve effect — look at the top X cards
    /// (X = lands controlled), put one (two if kicked) into hand, rest to the
    /// bottom of the library.
    /// </summary>
    /// <param name="card">The cast card instance — the resolve body reads
    /// <see cref="Card.WasKicked"/> off this same reference so the kicked
    /// "take two" branch fires only when the cast actually paid the rider
    /// (CR 702.33b).</param>
    /// <param name="caster">The resolving controller.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(ICard card, Player caster)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect($"{CardName}: look at top X (lands), put 1 (2 if kicked) in hand, rest to bottom.", async ctx =>
            {
                // CR 109.5 / 700.3 — X = number of lands the caster controls,
                // counted at resolution. Battlefield permanents typed Land.
                var x = caster.Zones.Battlefield.GetCards()
                    .Count(c => c.HasType(CardType.Land));
                if (x <= 0)
                {
                    // No lands → look at zero cards → no-op. No draw clause, so
                    // the empty-library SBA does not fire (CR 704.5b).
                    return;
                }

                // Peek up to X. ScryAction.Peek tolerates a short library
                // (returns up to X), so short- and empty-library handling
                // falls out for free.
                var peeked = ScryAction.Peek(caster, x).ToList();
                if (peeked.Count == 0)
                {
                    return;
                }

                // CR 702.33b — "if this spell was kicked" checked at
                // resolution. Card.WasKicked is stamped at cast-time by
                // KickerAdditionalCost.Pay and cleared post-resolve by the
                // cast flow. Take two cards when kicked, otherwise one — but
                // never more than were actually peeked.
                bool wasKicked = card is Card concrete && concrete.WasKicked;
                int take = Math.Min(wasKicked ? KickedTake : UnkickedTake, peeked.Count);

                var taken = await SelectForHandAsync(caster, peeked, take, ctx).ConfigureAwait(false);

                // Move each chosen card Library → Hand.
                foreach (var pick in taken)
                {
                    caster.Zones.Library.RemoveCard(pick);
                    caster.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }

                // Put the REST on the bottom of the library "in a random
                // order". The order is hidden information — v1 bottoms them in
                // peek order (legal; randomization is cosmetic). AddCard
                // appends, so the library tail is unchanged and the bottomed
                // cards sit at the very end (CR 701.18).
                foreach (var other in peeked)
                {
                    if (taken.Contains(other))
                    {
                        continue;
                    }
                    caster.Zones.Library.RemoveCard(other);
                    caster.Zones.Library.AddCard(other);
                    other.SetZone(ZoneType.Library);
                }
            }),
        };
    }

    /// <summary>
    /// Pick <paramref name="take"/> distinct cards from <paramref name="peeked"/>
    /// for the hand. Agent path: repeated
    /// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> over the remaining
    /// candidates (kind label surfaced verbatim to remote-agent UIs).
    /// Pre-agent fallback (or a declining/invalid agent pick): the first
    /// remaining peeked card — deterministic, matching every other
    /// look-and-pick factory. Consult is mandatory: the controller MUST put
    /// the cards into their hand.
    /// </summary>
    private static async ValueTask<List<ICard>> SelectForHandAsync(Player caster, List<ICard> peeked, int take, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(caster);
        var remaining = new List<ICard>(peeked);
        var chosen = new List<ICard>(take);

        for (var i = 0; i < take && remaining.Count > 0; i++)
        {
            ICard pick;
            if (agent != null)
            {
                var result = await agent.ChooseLibraryPickAsync(
                    ctx.Game,
                    candidates: remaining,
                    kindLabel: "card to put into your hand")
                    .ConfigureAwait(false);

                pick = result != null && remaining.Contains(result)
                    ? result
                    : remaining[0];
            }
            else
            {
                pick = remaining[0];
            }

            chosen.Add(pick);
            remaining.Remove(pick);
        }

        return chosen;
    }
}

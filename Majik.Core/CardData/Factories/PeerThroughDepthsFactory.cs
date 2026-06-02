using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Peer Through Depths (Champions of Kamigawa, {1}{U}).
///
/// Instant — Arcane. Oracle text (verified against Scryfall 2026-06-01):
///   "Look at the top five cards of your library. You may reveal an instant
///    or sorcery card from among them and put it into your hand. Put the rest
///    on the bottom of your library in any order."
///
/// ## Why it gets its own factory
/// This is the <i>instant-spell</i> analogue of
/// <see cref="AugurOfBolasFactory"/>'s ETB "look at top N, may reveal an
/// instant or sorcery to hand, rest on bottom" body — the only differences
/// are the look window (5 vs 3) and that the effect resolves off a spell on
/// the stack rather than an enters-the-battlefield trigger. Like
/// <see cref="DigThroughTimeFactory"/> the base card shape comes from the
/// embedded JSON definition (<c>peer-through-depths.json</c>) and the
/// resolve behaviour is layered on via <see cref="BuildResolveEffect"/>; the
/// JSON <c>CardDefinition</c> schema does not yet express a
/// "look-N / filtered-may-pick / bottom-rest" library effect.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{U} with the
///   <see cref="CardSubtype.Arcane"/> subtype (CR 205.3k), materialised from
///   the embedded JSON via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/> / the public
///   <see cref="ResolveAsync"/> seam):
///     1. CR 701.20 — "Look at the top five cards of your library." Snapshot up
///        to <see cref="LookCount"/> (5) cards (fewer if the library is short;
///        empty library is a clean no-op).
///     2. Filter the peeked pile to Instant OR Sorcery cards (CR 205.2 — card
///        type check), forming the eligible reveal pool. Same predicate as
///        <see cref="AugurOfBolasFactory"/> / <see cref="MysticalTutorFactory"/>.
///     3. "You may reveal…" — the controller chooses one eligible card or
///        declines (CR 603.6c / "may"). Pick resolution priority mirrors
///        <see cref="AugurOfBolasFactory"/>: a supplied <c>choosePick</c>
///        override, else the registered <see cref="IPlayerAgent"/> via
///        <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>, else the
///        deterministic pre-agent fallback (first eligible card). A null pick
///        is a legal decline.
///     4. The pick (if any) moves Library → Hand.
///     5. CR 701.20 — "Put the rest on the bottom of your library in any
///        order." Every non-picked peeked card is moved to the bottom of the
///        library in snapshot order.
///
/// ## Deferred (v1 gaps)
/// - <b>"In any order" agent prompt for re-bottoming</b>: v1 preserves snapshot
///   order; a multi-card library-place prompt plugs in here (same gap noted on
///   <see cref="AugurOfBolasFactory"/> / <see cref="DigThroughTimeFactory"/>).
/// - <b>Reveal-event emission</b>: the printed "reveal" should emit a
///   <see cref="Majik.Core.Events.CardRevealedEvent"/> for the picked card,
///   deferred behind the reveal-event plumbing pass (same gap as Augur of
///   Bolas / Mystical Tutor).
/// </summary>
[CardName("Peer Through Depths")]
public static class PeerThroughDepthsFactory
{
    public const string CardName = "Peer Through Depths";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "peer-through-depths";

    /// <summary>CR 701.20 — "Look at the top five cards of your library."</summary>
    public const int LookCount = 5;

    /// <summary>
    /// Result of the resolve. <see cref="Peeked"/> is every card looked at (top
    /// of library first), <see cref="Eligible"/> is the subset filtered to
    /// Instant or Sorcery, and <see cref="Picked"/> is the card the controller
    /// chose to reveal and put into hand — or <c>null</c> when the "may" was
    /// declined or no eligible card existed. After resolution the picked card
    /// (if any) is in the Hand zone and every other peeked card is at the bottom
    /// of the Library.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ICard> Peeked,
        IReadOnlyList<ICard> Eligible,
        ICard? Picked);

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / Arcane / {1}{U})
    /// from the embedded JSON definition. Resolve behaviour is supplied on
    /// demand via <see cref="BuildResolveEffect"/>, mirroring
    /// <see cref="DigThroughTimeFactory"/> / <see cref="EchoingTruthFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build Peer Through Depths's resolve effect — "look at top 5, you may
    /// reveal an instant or sorcery to hand, rest on bottom".
    /// </summary>
    /// <param name="caster">The spell's controller (CR 608.2 — resolves under
    /// its controller).</param>
    /// <param name="choosePick">Override for the eligible-card selector.
    /// Receives the instant/sorcery cards in the peeked five; returns the card
    /// to put into hand, or <c>null</c> to decline the "may" (CR 603.6c). When
    /// <c>null</c> the effect consults the resolution-time
    /// <see cref="IPlayerAgent"/> (or <see cref="AgentRegistry"/>); with no
    /// agent the deterministic fallback (first eligible card) applies.</param>
    /// <param name="onResolved">Optional callback invoked after the effect
    /// resolves with the full <see cref="Result"/>; lets tests observe the
    /// zone moves without re-querying every zone.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        Func<IReadOnlyList<ICard>, ICard?>? choosePick = null,
        Action<Result>? onResolved = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName} — look at top {LookCount}, may reveal an instant or sorcery to hand, "
                + "rest on bottom",
                async ctx =>
                {
                    var result = await ResolveAsync(caster, ctx, choosePick).ConfigureAwait(false);
                    onResolved?.Invoke(result);
                }),
        };
    }

    /// <summary>
    /// Execute the resolve body against <paramref name="controller"/>'s library.
    /// Public so tests and bots can drive resolution without a full cast flow.
    /// Logic mirrors <see cref="AugurOfBolasFactory.ResolveEtbAsync"/> with the
    /// look window widened to <see cref="LookCount"/> (5).
    /// </summary>
    public static async ValueTask<Result> ResolveAsync(
        Player controller,
        ResolutionContext ctx,
        Func<IReadOnlyList<ICard>, ICard?>? choosePick = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;

        // CR 701.20 — "Look at the top five cards of your library." Snapshot up
        // to LookCount cards (fewer if the library is short). An empty library
        // is a clean no-op (no draw-from-empty SBA here).
        var peeked = library.GetCards().Take(LookCount).ToList();
        if (peeked.Count == 0)
        {
            return new Result(
                Peeked: Array.Empty<ICard>(),
                Eligible: Array.Empty<ICard>(),
                Picked: null);
        }

        // Eligible reveal pool — Instant OR Sorcery (CR 205.2 type check).
        var eligible = peeked
            .Where(c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
            .ToList();

        // "You may reveal…" — controller picks one eligible card or declines
        // (CR 603.6c). Pick resolution priority (same as Augur of Bolas):
        //   1. Supplied choosePick override (test / production caller).
        //   2. Resolution-time agent (ctx.Agent) or AgentRegistry.
        //   3. Deterministic pre-agent fallback: first eligible card.
        ICard? pick = null;
        if (eligible.Count > 0)
        {
            if (choosePick != null)
            {
                pick = choosePick(eligible);
            }
            else
            {
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                if (agent != null)
                {
                    pick = await agent.ChooseLibraryPickAsync(
                        ctx.Game,
                        candidates: eligible,
                        kindLabel: "instant or sorcery card")
                        .ConfigureAwait(false);
                }
                else
                {
                    pick = eligible[0];
                }
            }

            // Defensive — never accept a pick outside the eligible pile; treat
            // it as a declined "may" rather than moving an ineligible card.
            if (pick != null && !eligible.Contains(pick))
            {
                pick = null;
            }
        }

        // Move the pick (if any) Library → Hand.
        if (pick != null)
        {
            library.RemoveCard(pick);
            controller.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
        }

        // CR 701.20 — "Put the rest on the bottom of your library in any order."
        // Move every non-picked peeked card to the bottom. Library.AddCard
        // appends to the end (bottom), preserving the existing library tail.
        foreach (var remainder in peeked)
        {
            if (ReferenceEquals(remainder, pick)) continue;
            library.RemoveCard(remainder);
            library.AddCard(remainder);
            remainder.SetZone(ZoneType.Library);
        }

        return new Result(
            Peeked: peeked,
            Eligible: eligible,
            Picked: pick);
    }
}

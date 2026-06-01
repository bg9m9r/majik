using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// Primitive shared builder for the Dredge keyword (CR 702.52).
///
/// <para>
/// CR 702.52a — "Dredge N" is a replacement effect that functions only
/// while the card with dredge is in a player's graveyard. "If you would
/// draw a card, you may instead mill N cards and return this card from
/// your graveyard to your hand." Per CR 702.52b, a player who has cards
/// in their library may NOT dredge if their library has fewer than N
/// cards — the replacement only applies when the controller's library
/// has at least N cards available to mill.
/// </para>
///
/// <para>
/// This factory attaches the canonical Dredge shape to a card:
/// <list type="number">
///   <item>A <see cref="KeywordAbility"/> marker — "Dredge" with the
///         dredge value <c>N</c> stamped on the ability — so consumers
///         (oracle audits, bot decision layer, future static effects)
///         can observe the keyword and its argument without reflecting
///         on the live replacement object.</item>
///   <item>A <see cref="LambdaReplacement{TIntent}"/> over
///         <see cref="DrawCardIntent"/> registered against the supplied
///         <paramref name="replacementBus"/> when one is provided. The
///         replacement gates on (a) the would-draw player being the
///         card's owner, (b) the card living in the owner's graveyard
///         at intent time (CR 702.52a — Dredge functions only from the
///         graveyard), (c) the owner's library having ≥ N cards
///         (CR 702.52b — illegal to dredge with fewer), and (d) the
///         owner's agent saying yes to the prompt
///         (<see cref="IPlayerAgent.ChooseYesNoAsync"/> with
///         <see cref="BotIntent.CardAdvantage"/> so the default
///         heuristic accepts). On yes the resolve body mills N cards
///         via <see cref="MillAction.Apply"/>, returns the source card
///         from graveyard to hand, and cancels the underlying draw
///         (returns null from the replacement). On no the bus returns
///         the intent unchanged and the draw resolves normally.</item>
/// </list>
/// </para>
///
/// <para>
/// CR 702.52b — "If a player has fewer cards in their library than the
/// number required by the dredge ability, the player can't choose to
/// dredge." This is gated in <see cref="LambdaReplacement{T}.Applies"/>
/// — the agent prompt is skipped entirely when the library is too small,
/// so the agent never sees a Dredge offer it couldn't legally accept.
/// </para>
///
/// <para>
/// CR 704.5b mid-mill empty-library — <see cref="MillAction.Apply"/>
/// halts cleanly when the library empties during the mill. The
/// state-based action loop reads
/// <see cref="Player.TriedToDrawFromEmptyLibrary"/> for the loss
/// condition; Dredge itself does NOT mark that flag (CR 702.52 mills,
/// it does not draw — milling from an empty / partially-empty library
/// is not a draw from an empty library). However the empty-library
/// marker DOES fire when the controller subsequently tries to draw with
/// no cards left, captured by the next <see cref="Fx.DrawCards"/> call.
/// </para>
///
/// <para>
/// The replacement is source-anchored: it ONLY fires while the source
/// card is in the controller's graveyard. Zone changes (return to hand,
/// exile, reanimate, shuffle into library) silently disable the
/// replacement without unregistering — the bus check returns false and
/// the replacement skips. Re-entering the graveyard re-enables it.
/// Persistent registration avoids the churn of subscribe/unsubscribe on
/// every zone move (CR 614.6 — replacement effects don't fire while
/// their source is in the wrong zone, which is exactly the shape this
/// gating produces).
/// </para>
/// </summary>
public static class DredgeFactory
{
    /// <summary>
    /// Attach Dredge N to <paramref name="source"/>: a
    /// <see cref="KeywordAbility"/> marker plus a graveyard-zoned
    /// <see cref="IReplacementEffect{TIntent}"/> over
    /// <see cref="DrawCardIntent"/> registered against
    /// <paramref name="replacementBus"/>.
    /// </summary>
    /// <param name="source">The card the Dredge ability lives on. Must
    /// have <see cref="ICard.Owner"/> wired — the replacement gates on
    /// the would-draw player matching <c>source.Owner</c>.</param>
    /// <param name="n">The Dredge value (mill count). CR 702.52 — must
    /// be positive; values ≤ 0 throw (no printed Dredge 0 exists).</param>
    /// <param name="replacementBus">Optional replacement bus the
    /// graveyard-draw replacement registers against. When null the
    /// keyword marker is still attached (shape-only path for unit tests
    /// that inspect ability lists without exercising the live
    /// replacement) but no replacement fires.</param>
    /// <param name="eventBus">Reserved for future "Whenever a player
    /// dredges" subscribers (CR 702.52 has no printed subscribers as of
    /// 2025-11-14, but the parameter keeps the shape parallel to
    /// <see cref="CyclingFactory.Build"/> for symmetry). v1 does not
    /// publish a CardDredgedEvent — when one is needed it will land
    /// alongside the first subscriber.</param>
    /// <returns>The attached <see cref="KeywordAbility"/> marker so
    /// callers can stamp additional metadata. The replacement itself is
    /// registered on <paramref name="replacementBus"/>; the caller does
    /// not need to retain a handle (the replacement gates on
    /// <c>source.Zone == Graveyard</c> so it stays dormant when the
    /// card isn't in the right zone).</returns>
    public static KeywordAbility Build(
        ICard source,
        int n,
        ReplacementBus? replacementBus = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (n <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(n),
                n,
                "DredgeFactory.Build: Dredge N must be positive (no printed Dredge 0).");
        }
        if (source.Owner is null)
        {
            throw new ArgumentException(
                "DredgeFactory.Build: card.Owner must be set before attaching Dredge — the replacement gates on the would-draw player matching source.Owner.",
                nameof(source));
        }

        var owner = source.Owner;

        // CR 702.52 — surface the keyword + its argument on the card so
        // consumers can observe Dredge N without reflecting on the
        // ReplacementBus. The Argument field on KeywordAbility carries
        // the N (matches the parameterised-keyword shape used by Ward N,
        // Annihilator N, Modular N, etc.).
        var marker = new KeywordAbility("Dredge", source, owner, arg: n);
        source.AddAbility(marker);

        // Shape-only path — no bus to register against. Marker-only
        // attachment is enough for shape / dispatcher tests.
        if (replacementBus is null) return marker;

        // CR 614 — graveyard-anchored draw replacement. Gates on:
        //   (a) the would-draw player matches source.Owner,
        //   (b) source.Zone == Graveyard (CR 702.52a),
        //   (c) owner.Zones.Library.Count >= n (CR 702.52b — can't
        //       dredge with fewer cards in library than N),
        //   (d) the owner's agent answers Yes to the dredge prompt.
        var replacement = new LambdaReplacement<DrawCardIntent>(
            applies: (intent, _) =>
            {
                if (!ReferenceEquals(intent.Player, owner)) return false;
                if (source.Zone != ZoneType.Graveyard) return false;
                if (owner.Zones.Library.Count < n) return false;
                return true;
            },
            // Sync path (ReplacementBus.Apply / direct-call unit tests) — the
            // no-resolution-context path. CR 702.52 prompting is INTENTIONALLY
            // NOT done here: the "dredge?" choice must be awaited, never bridged
            // sync-over-async, so the prompt lives exclusively on the async
            // path below. The deterministic no-prompt posture is "no" (a card
            // without a live agent never opts INTO the alternative), so the
            // straight draw resolves — identical to the historical no-agent
            // fallback.
            replace: (intent, _) => intent,
            // PLAN 08 — async path (ReplacementBus.ApplyAsync). Awaits the
            // owner's agent off the live ResolutionContext so the "Dredge?"
            // prompt never blocks a thread-pool thread on a human's think-time.
            replaceAsync: async (intent, _, ctx) =>
            {
                var agent = ctx.Agent ?? AgentRegistry.Get(owner);
                if (agent is null) return intent;

                var yes = await agent.ChooseYesNoAsync(
                        question: DredgeQuestion(source, n),
                        intent: Majik.Core.Cards.BotIntent.CardAdvantage,
                        ct: ctx.Ct)
                    .ConfigureAwait(false);
                return yes ? RunDredge(owner, source, n) : intent;
            },
            oneShot: false,
            tag: marker);

        replacementBus.Register(replacement);
        return marker;
    }

    private static string DredgeQuestion(ICard source, int n) =>
        $"Dredge {n}? (mill {n} and return {source.Name} from graveyard to hand)";

    /// <summary>
    /// CR 702.52 — resolve the dredge body, shared by the sync + async
    /// replacement paths:
    ///   1) mill N (CR 701.13) — <see cref="MillAction.Apply"/> halts cleanly
    ///      on mid-mill empty library; the empty-library loss marker does not
    ///      fire because Dredge mills, it does not draw from an empty library.
    ///   2) return <paramref name="source"/> from graveyard to hand
    ///      (source.Zone is guaranteed Graveyard by the Applies gate).
    /// Returns <c>null</c> so the bus cancels the original draw (CR 614.6 —
    /// the Dredge replacement consumes the draw entirely).
    /// </summary>
    private static DrawCardIntent? RunDredge(Player owner, ICard source, int n)
    {
        MillAction.Apply(owner, n);

        if (source.Zone == ZoneType.Graveyard)
        {
            owner.Zones.Graveyard.RemoveCard(source);
            owner.Zones.Hand.AddCard(source);
            source.SetZone(ZoneType.Hand);
        }

        return null;
    }
}

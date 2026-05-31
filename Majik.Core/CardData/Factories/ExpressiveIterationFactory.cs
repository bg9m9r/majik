using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Expressive Iteration (Strixhaven: School of
/// Mages, {U}{R}).
///
/// Sorcery. Oracle text:
///   "Look at the top three cards of your library. Put one of them into
///    your hand, put one of them on the bottom of your library, and exile
///    one of them. You may play the exiled card this turn."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {U}{R}, mana value 2, blue+red identity.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/>): peeks the top
///   three cards of the caster's library, then distributes them into three
///   mandatory destinations (CR 701.18 — "look at"; all three moves are
///   required, not optional):
///     1. One card → caster's hand (agent picks via
///        <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>, first slot,
///        kind label "card to put into your hand").
///     2. One card → bottom of caster's library (agent picks second slot,
///        kind label "card to put on the bottom of your library").
///     3. Remaining card → caster's exile zone, with a runtime exile-cast
///        grant (<see cref="Card.GrantRuntimeExileCast"/>) stamped for the
///        caster at the card's printed mana cost (CR 118.9) so it may be
///        played this turn via <see cref="Costs.ExileCastAlternativeCost"/>.
/// - When no agent is registered, the default pre-agent posture applies:
///   first peeked card → hand, second peeked card → bottom, third peeked
///   card → exile. This matches the deterministic first-pick fallback used
///   throughout the look-and-pick factory family (Sleight of Hand, Collected
///   Company, etc.).
/// - Short libraries: peek returns however many cards are available; each
///   destination is filled in order (hand first, then bottom, then exile)
///   from the available peeked cards. If fewer than 3 cards are available,
///   unfilled destinations are silently skipped. The exile grant is only
///   stamped when a card actually reaches exile.
/// - Empty library: no-op; the oracle text has no "draw" clause so the
///   draw-from-empty SBA (CR 704.5b) is never flagged.
/// - <b>"You may play the exiled card this turn"</b> duration: per the
///   oracle text, the grant is "this turn" (not "until end of your next
///   turn" as in Light Up the Stage). v1 ships the grant without a cleanup
///   subscription — the grant persists until cleared by callers or a
///   separate EOT mechanism. Full EOT wiring requires an IEventBus path;
///   that is deferred to v2 (same gap note as LightUpTheStageFactory's
///   "without a bus the grant persists" posture).
///
/// ## Deferred (v1 gaps)
/// - <b>"May play" includes lands</b>: the grant authorises casting only.
///   Playing the exiled card as a land would require a parallel land-play
///   grant. No Prowess/Modern shell exercised in deck validation requires
///   this corner-case.
/// - <b>EOT cleanup subscription</b>: no IEventBus path in v1. The grant
///   is cleared either by the card being cast (zone change removes the
///   card from exile) or manually by callers in tests.
/// </summary>
[CardName("Expressive Iteration")]
public static class ExpressiveIterationFactory
{
    public const string CardName = "Expressive Iteration";
    public const string PrintedManaCost = "{U}{R}";
    private const int LookAtCount = 3;

    /// <summary>
    /// Construct Expressive Iteration owned and controlled by
    /// <paramref name="owner"/>.
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
    /// Build Expressive Iteration's resolve effect — peek top 3 cards,
    /// put one in hand (agent choice), put one on the bottom of the
    /// library (agent choice), exile one with a play-this-turn grant.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                "Expressive Iteration: look at top 3, put 1 in hand, 1 on bottom, 1 in exile (may play this turn).",
                ctx => ResolveAsync(caster, ctx)),
        };
    }

    /// <summary>
    /// Execute Expressive Iteration's resolution against
    /// <paramref name="caster"/>'s library.
    /// </summary>
    public static async ValueTask ResolveAsync(Player caster, ResolutionContext ctx, IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        // Peek up to 3. ScryAction.Peek tolerates short/empty libraries.
        var peeked = ScryAction.Peek(caster, LookAtCount).ToList();
        if (peeked.Count == 0) return;

        agent = ctx.Agent ?? agent ?? AgentRegistry.Get(caster);

        // ── Slot 1: one card → hand ──────────────────────────────────────
        ICard pickForHand;
        if (agent != null && peeked.Count > 0)
        {
            // TODO: drop sync-over-async once IEffect.Execute becomes async.
            var chosen = await agent.ChooseLibraryPickAsync(
                ctx.Game,
                candidates: peeked,
                kindLabel: "card to put into your hand")
                .ConfigureAwait(false);

            // Defensive: fall back to first if agent returns null or invalid.
            pickForHand = chosen != null && peeked.Contains(chosen)
                ? chosen
                : peeked[0];
        }
        else
        {
            pickForHand = peeked[0];
        }

        caster.Zones.Library.RemoveCard(pickForHand);
        caster.Zones.Hand.AddCard(pickForHand);
        pickForHand.SetZone(ZoneType.Hand);

        var remaining = peeked.Where(c => !ReferenceEquals(c, pickForHand)).ToList();
        if (remaining.Count == 0) return;

        // ── Slot 2: one card → bottom of library ─────────────────────────
        ICard pickForBottom;
        if (agent != null && remaining.Count > 0)
        {
            // TODO: drop sync-over-async once IEffect.Execute becomes async.
            var chosen = await agent.ChooseLibraryPickAsync(
                ctx.Game,
                candidates: remaining,
                kindLabel: "card to put on the bottom of your library")
                .ConfigureAwait(false);

            pickForBottom = chosen != null && remaining.Contains(chosen)
                ? chosen
                : remaining[0];
        }
        else
        {
            pickForBottom = remaining[0];
        }

        // Library.AddCard appends to the bottom (index = tail). Remove
        // from current position first (it is still physically in the
        // library at peek-position), then re-add at the tail.
        caster.Zones.Library.RemoveCard(pickForBottom);
        caster.Zones.Library.AddCard(pickForBottom);
        pickForBottom.SetZone(ZoneType.Library);

        var forExile = remaining.Where(c => !ReferenceEquals(c, pickForBottom)).ToList();
        if (forExile.Count == 0) return;

        // ── Slot 3: remaining card → exile with play-this-turn grant ──────
        var exiledCard = forExile[0];

        caster.Zones.Library.RemoveCard(exiledCard);
        caster.Zones.Exile.AddCard(exiledCard);
        exiledCard.SetZone(ZoneType.Exile);

        if (exiledCard is Card concrete)
        {
            // CR 118.9 — grant matches ExileCastAlternativeCost.
            // Cost = printed mana cost (no alternate-cost rider for EI).
            concrete.GrantRuntimeExileCast(caster, concrete.ManaCostValue);
        }
    }
}

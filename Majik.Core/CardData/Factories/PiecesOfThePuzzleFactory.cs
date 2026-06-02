using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pieces of the Puzzle (Shadows over Innistrad, {2}{U}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Reveal the top five cards of your library. Put up to two instant and/or
///    sorcery cards from among them into your hand and the rest into your
///    graveyard."
///
/// ## Why it gets its own factory
/// Pieces of the Puzzle is the "reveal top N, take up-to-two by type, bin the
/// rest" dig spell. It combines the type-filtered reveal-and-take of
/// <see cref="AugurOfBolasFactory"/> (Instant OR Sorcery; agent-chosen picks
/// to hand) with a bulk Library → Graveyard move for everything not taken —
/// instead of Augur's bottom-of-library remainder. Both primitives already
/// ship (raw zone manipulation + the per-pick agent prompt), so no new engine
/// mechanic is required; the only new wrinkle versus Augur is that the pick is
/// "up to two" (a bounded loop of <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
/// calls) and the remainder lands in the graveyard.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{U}, blue. Card shape comes from the embedded
///   JSON (<c>pieces-of-the-puzzle.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/> /
///   <see cref="ResolveAsync"/>):
///     1. Reveal up to <see cref="RevealCount"/> (5) cards off the top of the
///        caster's library (fewer if the library is short — CR 701.16 reveal
///        tolerates a short library; same posture as
///        <see cref="AugurOfBolasFactory"/>). Empty library → clean no-op.
///     2. Build the eligible pool — Instant OR Sorcery cards among the revealed
///        five (CR 205.2 card-type check). Lands / creatures / artifacts /
///        enchantments / planeswalkers are excluded by the printed wording.
///     3. "Put up to two…" — the caster chooses zero, one, or two eligible
///        cards to put into their hand. Each pick is sourced (in priority
///        order) from the registered <see cref="IPlayerAgent"/> via
///        <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>, then a
///        deterministic pre-agent fallback (first eligible cards, up to two —
///        consistent with <see cref="AugurOfBolasFactory"/> /
///        <see cref="AnticipateFactory"/>). "Up to two" is a maximum, never a
///        requirement (CR 122.1c "up to") — an agent that declines (returns
///        <c>null</c>) stops the picking early.
///     4. "…and the rest into your graveyard." Every revealed card NOT put
///        into the hand is moved Library → Graveyard (CR 701.16 — the reveal
///        itself doesn't change zones; the put-to-graveyard does). No draw
///        clause, so the empty-library SBA never fires here (CR 704.5b).
///
/// ## Rules citations
/// - CR 701.16 — "Reveal" (the cards stay in the library until moved).
/// - CR 205.2 — Instant / Sorcery card-type check for the eligible pool.
/// - CR 122.1c — "up to two" is a maximum, not a requirement.
/// - CR 704.5b — no draw clause → no empty-library loss SBA.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: same gap as <see cref="AugurOfBolasFactory"/> /
///   <see cref="AnticipateFactory"/> — the cards move zones without publishing
///   a <see cref="Majik.Core.Events.CardRevealedEvent"/> (behind the
///   reveal-event plumbing pass).
/// - <b>"In any order" to graveyard</b>: the printed text fixes no order for
///   the remainder, and graveyard order is rarely observable; v1 bins them in
///   reveal order (top-of-library first).
/// </summary>
[CardName("Pieces of the Puzzle")]
public static class PiecesOfThePuzzleFactory
{
    public const string CardName = "Pieces of the Puzzle";
    public const string Slug = "pieces-of-the-puzzle";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>CR — "Reveal the top five cards of your library."</summary>
    public const int RevealCount = 5;

    /// <summary>CR 122.1c — "Put up to two … cards … into your hand."</summary>
    public const int MaxToHand = 2;

    /// <summary>
    /// Outcome of a Pieces of the Puzzle resolution. <see cref="Revealed"/> is
    /// every card the spell looked at (top of library first); <see cref="Eligible"/>
    /// is the subset filtered to Instant or Sorcery; <see cref="ToHand"/> is the
    /// (zero, one, or two) eligible cards moved to the hand; <see cref="ToGraveyard"/>
    /// is everything else, moved to the graveyard.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ICard> Revealed,
        IReadOnlyList<ICard> Eligible,
        IReadOnlyList<ICard> ToHand,
        IReadOnlyList<ICard> ToGraveyard);

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Pieces of the Puzzle: no
    /// modes, no X, no targets, no additional costs — the resolve body reveals
    /// the top five, takes up to two instants/sorceries to hand, and bins the
    /// rest.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build Pieces of the Puzzle's resolve effect — reveal top 5, take up to
    /// two instant/sorcery cards to hand, the rest to the graveyard.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                $"{CardName}: reveal top {RevealCount}, up to {MaxToHand} instant/sorcery to hand, rest to graveyard.",
                async ctx => { await ResolveAsync(caster, ctx).ConfigureAwait(false); }),
        };
    }

    /// <summary>
    /// Execute Pieces of the Puzzle's resolve body against
    /// <paramref name="caster"/>'s library. Public so tests and bots can drive
    /// the resolution directly. Returns the full <see cref="Result"/> after the
    /// zone moves are applied.
    /// </summary>
    public static async ValueTask<Result> ResolveAsync(Player caster, ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var library = caster.Zones.Library;

        // CR 701.16 — reveal the top five (fewer if the library is short).
        // Snapshot only; the cards stay in the library until moved. Empty
        // library → clean no-op (no draw clause, so no SBA fires).
        var revealed = library.GetCards().Take(RevealCount).ToList();
        if (revealed.Count == 0)
        {
            return new Result(
                Array.Empty<ICard>(),
                Array.Empty<ICard>(),
                Array.Empty<ICard>(),
                Array.Empty<ICard>());
        }

        // CR 205.2 — eligible pool is Instant OR Sorcery cards among the
        // revealed five.
        var eligible = revealed
            .Where(c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
            .ToList();

        // CR 122.1c — "Put up to two …" The caster chooses zero, one, or two
        // eligible cards. Agent path: ChooseLibraryPickAsync over the shrinking
        // eligible pool; a null return ("decline") stops early. Pre-agent
        // deterministic fallback: the first eligible cards, up to two.
        var toHand = new List<ICard>();
        var remainingEligible = new List<ICard>(eligible);
        var agent = ctx.Agent ?? AgentRegistry.Get(caster);

        for (var i = 0; i < MaxToHand && remainingEligible.Count > 0; i++)
        {
            ICard? pick;
            if (agent != null)
            {
                pick = await agent.ChooseLibraryPickAsync(
                    ctx.Game,
                    candidates: remainingEligible,
                    kindLabel: "instant or sorcery card to put into your hand")
                    .ConfigureAwait(false);

                // Defensive: only accept a pick the agent was actually offered.
                // A null / out-of-pool return is treated as declining the
                // remaining "up to" picks (CR 122.1c).
                if (pick == null || !remainingEligible.Contains(pick))
                {
                    break;
                }
            }
            else
            {
                // Pre-agent deterministic fallback: take the first eligible.
                pick = remainingEligible[0];
            }

            toHand.Add(pick);
            remainingEligible.Remove(pick);
        }

        // Move the picks Library → Hand.
        foreach (var card in toHand)
        {
            library.RemoveCard(card);
            caster.Zones.Hand.AddCard(card);
            card.SetZone(ZoneType.Hand);
        }

        // "…and the rest into your graveyard." Every revealed card not taken to
        // hand goes Library → Graveyard, in reveal order (top first).
        var toGraveyard = new List<ICard>();
        foreach (var card in revealed)
        {
            if (toHand.Contains(card))
            {
                continue;
            }

            library.RemoveCard(card);
            caster.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
            toGraveyard.Add(card);
        }

        return new Result(revealed, eligible, toHand, toGraveyard);
    }
}

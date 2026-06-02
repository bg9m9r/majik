using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Open the Armory (Future Sight / reprints, {1}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-02):
///   "Search your library for an Aura or Equipment card, reveal it, put it
///    into your hand, then shuffle."
///
/// ## Shape source
/// Card identity (name / Sorcery / {1}{W}) is materialised from the embedded
/// JSON definition (<c>open-the-armory.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="PeerThroughDepthsFactory"/> (a spell whose resolve body is
/// hand-rolled because the JSON <c>AbilityDefinition</c> schema cannot yet
/// express a library-search effect).
///
/// ## Why a named factory (vs. a peek-template broaden)
/// This is a <i>whole-library tutor</i> (CR 701.19a — "Search your library
/// for…"), not a look-at-top-N peek. The resolve logic mirrors
/// <see cref="FierceEmpathFactory"/>'s
/// <c>TutorOneBigCreatureToHandAsync</c> — search the entire library for ONE
/// card matching a predicate, consult the agent (which may decline; "you
/// may"-less here but the search itself can fail to find), move the pick
/// Library → Hand, then shuffle once (CR 701.20a) — differing only in the
/// predicate (Aura OR Equipment card) and in resolving off a Sorcery on the
/// stack rather than an enters-the-battlefield trigger.
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b> at printed cost {1}{W}.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/> / the public
///   <see cref="ResolveAsync"/> seam):
///     1. CR 701.19a — search the controller's ENTIRE library for cards that
///        are an Aura (CR 205.3h — Enchantment with the Aura subtype) or an
///        Equipment (CR 205.3g — Artifact with the Equipment subtype). The
///        check is on card subtype regardless of the card's other types, so a
///        creature that is also an Equipment, etc., would qualify.
///     2. The controller chooses one eligible card (CR 701.19a — a search is a
///        "find" the player may fail to complete; a null pick is legal). Pick
///        resolution priority mirrors <see cref="PeerThroughDepthsFactory"/>:
///        a supplied <c>choosePick</c> override, else the registered
///        <see cref="IPlayerAgent"/> via
///        <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>, else the
///        deterministic pre-agent fallback (first eligible card).
///     3. The pick (if any) moves Library → Hand.
///     4. CR 701.20a — shuffle ONCE after the search, whether or not a card was
///        found (the search still happened).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the printed "reveal it" should emit a
///   <see cref="Majik.Core.Events.CardRevealedEvent"/> for the tutored card —
///   deferred behind the reveal-event plumbing pass, the same gap shared by
///   every tutor factory (<see cref="FierceEmpathFactory"/>,
///   <see cref="PeerThroughDepthsFactory"/>). The card still reaches the hand,
///   so the observable game state is correct; only the public "reveal" UI
///   signal is absent.
/// </summary>
[CardName("Open the Armory")]
public static class OpenTheArmoryFactory
{
    public const string CardName = "Open the Armory";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "open-the-armory";

    /// <summary>
    /// Result of the resolve. <see cref="Eligible"/> is every Aura/Equipment
    /// card found in the library, and <see cref="Picked"/> is the card the
    /// controller chose to reveal and put into hand — or <c>null</c> when the
    /// search found nothing or the pick was declined. After resolution the
    /// picked card (if any) is in the Hand zone and the library has been
    /// shuffled (CR 701.20a).
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ICard> Eligible,
        ICard? Picked);

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {1}{W}) from the
    /// embedded JSON definition. Resolve behaviour is supplied on demand via
    /// <see cref="BuildResolveEffect"/>, mirroring
    /// <see cref="PeerThroughDepthsFactory"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build Open the Armory's resolve effect — "search your library for an
    /// Aura or Equipment card, reveal it, put it into your hand, then shuffle".
    /// </summary>
    /// <param name="caster">The spell's controller (CR 608.2 — resolves under
    /// its controller).</param>
    /// <param name="choosePick">Override for the eligible-card selector.
    /// Receives the Aura/Equipment cards found in the library; returns the card
    /// to put into hand, or <c>null</c> to find nothing (CR 701.19a). When
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
                $"{CardName} — search library for an Aura or Equipment card to hand, then shuffle",
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
    /// Mirrors <see cref="FierceEmpathFactory"/>'s whole-library tutor with the
    /// predicate widened to "Aura OR Equipment card".
    /// </summary>
    public static async ValueTask<Result> ResolveAsync(
        Player controller,
        ResolutionContext ctx,
        Func<IReadOnlyList<ICard>, ICard?>? choosePick = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;

        // CR 205.3g/h — Equipment is an Artifact subtype, Aura is an
        // Enchantment subtype. The card must simply HAVE one of those subtypes
        // ("an Aura or Equipment card"); we don't require it to currently be a
        // permanent on the battlefield.
        static bool IsEligible(ICard c) =>
            c.HasSubtype(CardSubtype.Aura) || c.HasSubtype(CardSubtype.Equipment);

        var eligible = library.GetCards().Where(IsEligible).ToList();

        // "Search your library for an Aura or Equipment card" — controller picks
        // one eligible card or finds none (CR 701.19a — a search may fail to
        // find even when candidates exist). Pick resolution priority (same as
        // Peer Through Depths):
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
                        kindLabel: "Aura or Equipment card")
                        .ConfigureAwait(false);
                }
                else
                {
                    pick = eligible[0];
                }
            }

            // Defensive — never accept a pick outside the eligible pile; treat
            // it as "found nothing" rather than moving an ineligible card.
            if (pick != null && !eligible.Contains(pick))
            {
                pick = null;
            }
        }

        // Move the pick (if any) Library → Hand (CR 701.19a — "put it into your
        // hand"). Prefer the registered ZoneService so any zone-change events
        // fire; fall back to raw zone manipulation for shape/unit tests.
        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(controller);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Hand, controller);
            }
            else
            {
                library.RemoveCard(pick);
                controller.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }
        }

        // CR 701.20a — "then shuffle." A single search effect performs one
        // shuffle, whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(controller, "open-the-armory");

        return new Result(Eligible: eligible, Picked: pick);
    }
}

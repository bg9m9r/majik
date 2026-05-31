using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glint-Nest Crane (Kaladesh, {1}{U}).
///
/// Creature — Bird 1/3. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, look at the top four cards of your library.
///    You may reveal an artifact card from among them and put it into your
///    hand. Put the rest on the bottom of your library in any order."
///
/// Glint-Nest Crane is the artifact-flavoured cousin of Augur of Bolas
/// (see <see cref="AugurOfBolasFactory"/>) — the same ETB dig-and-bottom
/// template, but it looks at the top <see cref="LookCount"/> (4, not 3)
/// cards, filters the reveal pool to <i>artifact</i> cards (not
/// instant/sorcery), and carries Flying.
///
/// The base shape (name, Creature, Bird subtype, {1}{U}, 1/3) is
/// materialised from the embedded JSON definition
/// (<c>glint-nest-crane.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <c>AbilityDefinition</c> schema does not express keyword markers or the
/// look-at-top-N → reveal-by-type → bottom-the-rest effect, so Flying + the
/// ETB trigger are layered on here (same posture as
/// <see cref="KitesailFreebooterFactory"/> for the JSON+Flying split and
/// <see cref="AugurOfBolasFactory"/> for the dig-and-bottom mechanic).
///
/// ## Implemented (v1)
/// - 1/3 <see cref="Creature"/> — Bird at {1}{U}; owner / controller wired.
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker (same shape as Kitesail Freebooter's Flying).
/// - <b>ETB triggered ability (CR 603.6a — "when this creature enters")</b>:
///     1. Peek the top <see cref="LookCount"/> (4) cards of the controller's
///        library (fewer if the library is short — CR 701.21; empty library
///        → clean no-op, no draw-from-empty SBA fires here).
///     2. Filter the peeked pile to Artifact cards (CR 205.2 / card-type
///        check) — the eligible reveal pool.
///     3. Ask the controller's registered <see cref="IPlayerAgent"/> (via
///        <see cref="AgentRegistry"/>) to pick an eligible card via
///        <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>. The printed
///        "you may" maps to the agent (or supplied picker) returning
///        <c>null</c> to decline (CR 603.6c). Pre-agent deterministic
///        fallback: first eligible artifact (consistent with
///        <see cref="AugurOfBolasFactory"/>).
///     4. Move the pick (if any) Library → Hand.
///     5. Put the rest — the peeked cards that were NOT picked — on the
///        bottom of the library in snapshot order (v1: identity ordering;
///        the "in any order" agent prompt is a future plug-in, same gap as
///        Augur of Bolas).
///
/// ## Deferred (v1 gaps — same posture as <see cref="AugurOfBolasFactory"/>)
/// - <b>"In any order" agent prompt for re-bottoming</b>: v1 preserves
///   snapshot order.
/// - <b>Reveal-event emission</b>: the printed "reveal" does not yet emit a
///   <see cref="Majik.Core.Events.CardRevealedEvent"/> (deferred behind the
///   reveal-event plumbing pass).
/// </summary>
[CardName("Glint-Nest Crane")]
public static class GlintNestCraneFactory
{
    public const string CardName = "Glint-Nest Crane";
    public const string Slug = "glint-nest-crane";
    public const int LookCount = 4;
    private const string FlyingKeyword = "Flying";

    /// <summary>
    /// Result of the ETB trigger resolution. <see cref="Peeked"/> is every
    /// card the ETB looked at (top of library first), <see cref="Eligible"/>
    /// is the subset filtered to Artifact, and <see cref="Picked"/> is the
    /// card the controller chose to reveal and put to hand — or <c>null</c>
    /// when the "may" was declined or no eligible card existed. The picked
    /// card has been moved to the Hand zone; all others have been moved to
    /// the bottom of the Library.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ICard> Peeked,
        IReadOnlyList<ICard> Eligible,
        ICard? Picked);

    /// <summary>
    /// Construct Glint-Nest Crane with no runtime services. Flying plus the
    /// ETB trigger are attached for shape inspection; the trigger is NOT
    /// registered with a <see cref="TriggerManager"/>. The ETB effect uses
    /// raw zone manipulation directly and consults <see cref="AgentRegistry"/>
    /// for picks (pre-agent fallback: first eligible artifact). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, choosePick: null, onEtbResolved: null);

    /// <summary>
    /// Construct Glint-Nest Crane with optional TriggerManager wiring, picker
    /// override, and result callback.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger is registered so
    /// a <see cref="CardMovedEvent"/> → Battlefield for this card lands on the
    /// stack automatically (CR 603.3).</param>
    /// <param name="choosePick">Override for the eligible-card selector.
    /// Receives the list of artifact cards in the top four; returns the card
    /// to put into hand, or <c>null</c> to decline the "may" (CR 603.6c).
    /// When <c>null</c> the factory consults <see cref="AgentRegistry"/>; if
    /// no agent is registered the deterministic fallback (first eligible
    /// artifact) applies.</param>
    /// <param name="onEtbResolved">Callback invoked after the ETB resolves
    /// with the full <see cref="Result"/>. Used by tests to observe zone
    /// moves without going through TriggerManager.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<ICard>, ICard?>? choosePick = null,
        Action<Result>? onEtbResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Bird
        // subtype, {1}{U}, 1/3). The JSON carries no abilities — Flying + the
        // ETB trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Keyword marker only; combat blocking restriction
        // (same shape as Kitesail Freebooter's Flying).
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB trigger (CR 603.6a):
        //   "When this creature enters, look at the top four cards of your
        //    library. You may reveal an artifact card from among them and put
        //    it into your hand. Put the rest on the bottom of your library in
        //    any order."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName} — look at top {LookCount}, may reveal an artifact to hand, rest on bottom",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                var result = await ResolveEtbAsync(controller, ctx, choosePick).ConfigureAwait(false);
                onEtbResolved?.Invoke(result);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Execute Glint-Nest Crane's ETB body against <paramref name="controller"/>'s
    /// library. Public so tests and bots can drive the resolution without
    /// going through TriggerManager.
    ///
    /// Peeks up to <see cref="LookCount"/> cards (fewer if the library is
    /// short), builds the eligible pile (Artifact), and asks
    /// <paramref name="choosePick"/> — or the registered
    /// <see cref="IPlayerAgent"/> via
    /// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> — which card (if any)
    /// to reveal and put into hand. The picked card is moved Library → Hand;
    /// all others are moved to the bottom of the library in snapshot order.
    /// </summary>
    public static async ValueTask<Result> ResolveEtbAsync(
        Player controller,
        ResolutionContext ctx,
        Func<IReadOnlyList<ICard>, ICard?>? choosePick = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;

        // CR 701.20 — "Look at the top four cards of your library." Snapshot up
        // to LookCount cards (fewer if the library is short). Empty library is a
        // clean no-op (no draw-from-empty SBA fires here).
        var peeked = library.GetCards().Take(LookCount).ToList();
        if (peeked.Count == 0)
        {
            return new Result(
                Peeked: Array.Empty<ICard>(),
                Eligible: Array.Empty<ICard>(),
                Picked: null);
        }

        // Eligible reveal pool — Artifact cards (CR 205.2 type check). All
        // non-artifact peeked cards are excluded by the printed wording.
        var eligible = peeked
            .Where(c => c.HasType(CardType.Artifact))
            .ToList();

        // "You may reveal…" — controller chooses one or declines (CR 603.6c).
        // Priority order for pick resolution:
        //   1. Supplied choosePick override (test / production caller).
        //   2. Registered IPlayerAgent via AgentRegistry.
        //   3. Deterministic fallback: first eligible artifact.
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
                        kindLabel: "artifact card")
                        .ConfigureAwait(false);
                }
                else
                {
                    // Pre-agent deterministic fallback (consistent with
                    // AugurOfBolasFactory).
                    pick = eligible[0];
                }
            }

            // Defensive — never accept a pick outside the eligible pile; treat
            // it as a declined "may" rather than silently moving an ineligible
            // card to hand.
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

        // CR 701.20 — "Put the rest on the bottom of your library in any
        // order." Move all non-picked peeked cards to the bottom of the
        // library. Library.AddCard appends to the end (bottom), so the existing
        // library tail is preserved.
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

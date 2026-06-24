using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wickerfolk Thresher (Modern Horizons 3, {3}{G}).
///
/// Artifact Creature — Scarecrow 5/4. Oracle text (verified against Scryfall):
///   "Delirium — Whenever this creature attacks, if there are four or more
///    card types among cards in your graveyard, look at the top card of your
///    library. If it's a land card, you may put it onto the battlefield. If
///    you don't put the card onto the battlefield, put it into your hand."
///
/// ## Shape source
/// Card identity (name, {3}{G}, 5/4, Artifact Creature — Scarecrow) is loaded
/// from <c>Majik.Core/CardData/Cards/wickerfolk-thresher.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The delirium attack trigger is wired
/// in code below.
///
/// ## Implementation
/// This card composes two engine primitives already shipped for the suggested
/// analogues:
///
/// - <b>Delirium intervening-if attack trigger (CR 702.105 / CR 603.4 /
///   CR 508.1f)</b>: a <see cref="TriggeredAbility"/> on
///   <see cref="Triggers.OnAttackSelf"/> whose <c>interveningIf</c> samples the
///   controller's graveyard for delirium via
///   <see cref="GrimFlayerFactory.IsDeliriumActive"/> (4+ distinct
///   <see cref="CardType"/> values — <see cref="TarmogoyfFactory.CountDistinctCardTypes"/>).
///   "Delirium —" is precisely a CR 603.4 intervening-if condition: it is
///   re-checked when the ability would go on the stack AND again as it
///   resolves, so the ability does nothing if delirium has been lost in
///   between. Same attack-trigger shape as
///   <see cref="SubterraneanSchoonerFactory"/>; same delirium predicate as
///   <see cref="GrimFlayerFactory"/>.
///
/// - <b>"Look at the top card; land → you may put onto battlefield, else →
///   hand" (CR 701.16a "look at" / CR 305.1)</b>: the reveal-then-branch body
///   is structurally identical to <see cref="CoilingOracleFactory"/>'s ETB
///   effect, with two differences keyed to the printed text:
///   1. <b>"Look at" (not "reveal")</b> — the card is examined privately by the
///      controller; no public <c>CardRevealedEvent</c> is published (CR 701.16a
///      vs. the reveal in Coiling Oracle).
///   2. <b>"you may put it onto the battlefield" + "if you don't … put it into
///      your hand"</b> — the land placement is OPTIONAL. The controller's
///      <see cref="IPlayerAgent"/> is asked via
///      <see cref="IPlayerAgent.ChooseYesNoAsync(string,BotIntent,System.Threading.CancellationToken)"/>
///      (intent <see cref="BotIntent.CheatIntoPlay"/> — a beneficial ramp, so
///      the default heuristic accepts). A land the controller declines, and any
///      nonland, both go to the controller's hand — the printed "if you don't
///      put the card onto the battlefield, put it into your hand" branch.
///   Putting a land onto the battlefield this way does NOT count as a land drop
///   (CR 305.2 — "put", not "play"); the land enters untapped (no text
///   qualifier). Empty library → no-op (CR 701.16 — nothing to look at).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + the attack trigger attached
///   (but NOT registered with a <see cref="TriggerManager"/>). The overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the attack
///   trigger so a bus-driven <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   for this creature places it on the stack automatically (CR 603.3).
///
/// ## Deferred (v1 gaps)
/// - <b>"Look at" privacy + raw-zone path</b>: the land moves Library →
///   Battlefield with raw-zone manipulation (no ZoneService routing), so
///   ETB-replacement effects (CR 614) on the placed land aren't observed —
///   same gap as <see cref="CoilingOracleFactory"/> v1.
/// - <b>Bot land-into-play value</b>: the "may" auto-accepts via the
///   CheatIntoPlay intent heuristic; the heuristic bot does not yet weigh
///   declining (e.g. to keep a land in hand) — consistent with the rest of the
///   optional-ramp factory family.
/// </summary>
[CardName("Wickerfolk Thresher")]
public static class WickerfolkThresherFactory
{
    public const string CardName = "Wickerfolk Thresher";
    public const string Slug = "wickerfolk-thresher";

    /// <summary>CR 702.105 — delirium is satisfied at 4+ card types in the graveyard.</summary>
    public const int DeliriumThreshold = GrimFlayerFactory.DeliriumThreshold;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Wickerfolk Thresher with its delirium attack trigger attached
    /// to the card shape but NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Wickerfolk Thresher with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the attack trigger
    /// is registered so the relevant
    /// <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/> places
    /// it on the stack automatically (CR 603.3), subject to its CR 603.4
    /// delirium intervening-if.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Delirium attack trigger — CR 702.105 / 603.4 / 508.1f.
        //   "Delirium — Whenever this creature attacks, if there are four or
        //    more card types among cards in your graveyard, look at the top
        //    card of your library. If it's a land card, you may put it onto
        //    the battlefield. If you don't put the card onto the battlefield,
        //    put it into your hand."
        //
        // "Delirium —" is a CR 603.4 intervening-if: re-checked when the
        // ability would go on the stack AND again as it resolves. Sampled live
        // from the controller's graveyard (GrimFlayerFactory.IsDeliriumActive).
        // ----------------------------------------------------------------
        var lookEffect = new Effect(
            $"{CardName}: look at top of library; land → you may battlefield, else → hand",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                await LookTopThenLandToPlayOrHandAsync(controller, ctx).ConfigureAwait(false);
            });

        bool DeliriumSatisfied()
        {
            var controller = card.Controller ?? owner;
            return GrimFlayerFactory.IsDeliriumActive(controller);
        }

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { lookEffect },
            interveningIf: DeliriumSatisfied,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 701.16a / CR 305.1 — look at the top card of
    /// <paramref name="player"/>'s library; if it's a land card the controller
    /// MAY put it onto the battlefield (asked via the agent — a beneficial ramp,
    /// auto-accepted by the default heuristic). A declined land, and any
    /// nonland, both go to the controller's hand ("if you don't put the card
    /// onto the battlefield, put it into your hand"). Empty library → no-op
    /// (CR 701.16 — nothing to look at). Putting a land this way is not a land
    /// drop (CR 305.2 — "put", not "play"); it enters untapped.
    /// </summary>
    private static async ValueTask LookTopThenLandToPlayOrHandAsync(Player player, ResolutionContext ctx)
    {
        var library = player.Zones.Library;
        var top = library.GetCards().FirstOrDefault();
        if (top == null) return; // empty library — no-op (CR 701.16)

        var putOntoBattlefield = false;
        if (top.HasType(CardType.Land))
        {
            // "you may put it onto the battlefield." Beneficial ramp →
            // BotIntent.CheatIntoPlay (default heuristic accepts).
            var agent = ctx.Agent ?? AgentRegistry.Get(player);
            putOntoBattlefield = agent != null
                ? await agent.ChooseYesNoAsync(
                        $"{CardName}: put the looked-at land onto the battlefield?",
                        BotIntent.CheatIntoPlay,
                        ctx.Ct).ConfigureAwait(false)
                : true; // no agent — default to the beneficial ramp.
        }

        library.RemoveCard(top);

        if (putOntoBattlefield)
        {
            // CR 305.1 — putting a land onto the battlefield this way does NOT
            // count as a land drop (CR 305.2 — "put", not "play"). Land enters
            // untapped (no text qualifier). Raw-zone wiring (same v1 gap as
            // CoilingOracleFactory): route through ZoneService in fully-wired
            // callers to fire ETB / replacement effects.
            player.Zones.Battlefield.AddCard(top);
            top.SetZone(ZoneType.Battlefield);
            if (top is Permanent perm)
            {
                perm.SetController(player);
                perm.MarkEnteredBattlefield();
            }
            else
            {
                top.SetController(player);
            }
        }
        else
        {
            // "If you don't put the card onto the battlefield, put it into your
            // hand." Covers a declined land AND every nonland.
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }
}

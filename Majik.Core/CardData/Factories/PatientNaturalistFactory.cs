using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Patient Naturalist (Modern Horizons 3, {2}{G}).
///
/// Creature — Human Scout 2/3. Oracle text (verified against Scryfall 2026-06-24):
///   "When this creature enters, mill three cards. Put a land card from among
///    the milled cards into your hand. If you can't, create a Treasure token.
///    (To mill three cards, put the top three cards of your library into your
///    graveyard.)"
///
/// ## Why it gets its own factory
/// Patient Naturalist is the land-flavoured ETB self-mill cousin of
/// <see cref="SatyrWayfinderFactory"/>, but the mechanic differs in two ways
/// that the reveal-and-choose primitive cannot express:
///   1. The three cards go to the graveyard FIRST (a real mill — CR 701.13),
///      and the land is then pulled <i>out of the graveyard</i> into hand. In
///      Satyr Wayfinder the rest go to the graveyard only AFTER the land is
///      lifted off the reveal pile (top-of-library reveal), so the land never
///      touches the graveyard. Here the land is genuinely milled and recovered.
///   2. The "If you can't" clause (CR 608.2 — an instruction the player is
///      unable to follow is skipped) mints a Treasure token (CR 111.10) as the
///      else-branch — the same Treasure mint as
///      <see cref="DeadlyDisputeFactory"/> (<see cref="TokenFactory.CreateTreasure"/>).
/// So it composes <see cref="Fx.Mill"/> (returns the milled cards, now in the
/// graveyard) with a graveyard→hand land pull and the Treasure fallback. All
/// three primitives already ship — no new engine mechanic is required.
///
/// The base shape (name, Creature, Human Scout subtypes, {2}{G}, 2/3) is
/// materialised from the embedded JSON definition (<c>patient-naturalist.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the JSON carries no abilities —
/// the ETB trigger is layered on here (same posture as
/// <see cref="SatyrWayfinderFactory"/> / <see cref="CivicWayfinderFactory"/>).
///
/// ## Implemented (v1)
/// - 2/3 <see cref="Creature"/> — Human Scout at {2}{G}; owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b> wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with
///   ActiveZones = Battlefield. On resolution it:
///   <list type="number">
///     <item><b>Mills three cards</b> (CR 701.13) via <see cref="Fx.Mill"/> —
///       clamped to library size (empty / short library is a clean no-op and
///       does not by itself cause loss, CR 104.3c). The milled cards are now in
///       the controller's graveyard.</item>
///     <item><b>Puts a land card into hand</b> — a <i>mandatory</i> "Put …"
///       instruction (CR 305 — any Land card type qualifies: basics, nonbasics,
///       land-typed duals). When more than one land was milled the controller's
///       agent picks which via <see cref="IPlayerAgent.ChooseFromPileAsync"/>
///       (deterministic first-land when no agent is registered); the picked
///       land moves graveyard→hand through the registered <c>ZoneService</c>
///       (raw zone mutation fallback for shape/dispatcher-test paths).</item>
///     <item><b>Else, Treasure (CR 608.2 / CR 111.10)</b> — when no land was
///       among the milled cards the "Put a land … into your hand" instruction
///       can't be followed, so the else-branch creates one Treasure token under
///       the controller's control via <see cref="TokenFactory.CreateTreasure"/>.
///       </item>
///   </list>
///
/// ## Rules citations
/// - CR 603.6a — ETB triggered ability.
/// - CR 701.13 — mill.
/// - CR 305 — "a land card" (any Land card type).
/// - CR 608.2 — "If you can't" (skip an impossible instruction → else-branch).
/// - CR 111.10 — Treasure token (colourless artifact, any-colour sac mana).
///
/// ## Deferred (v1 gaps)
/// - <b>Mill / reveal event</b>: no <c>CardsRevealedEvent</c> is published for
///   the milled pile — same gap as every self-mill factory; no live observer
///   cares yet.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached (not
///   registered with any <see cref="TriggerManager"/>). The overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — also registers the ETB
///   trigger so a qualifying <see cref="Majik.Core.Events.CardMovedEvent"/>
///   lands the ability on the stack automatically (CR 603.2).
/// </summary>
[CardName("Patient Naturalist")]
public static class PatientNaturalistFactory
{
    public const string CardName = "Patient Naturalist";
    public const string Slug = "patient-naturalist";

    /// <summary>CR 701.13 — "mill three cards."</summary>
    public const int MillCount = 3;

    /// <summary>
    /// Shape overload — attaches the ETB trigger without registering it with a
    /// <see cref="TriggerManager"/>. The overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Patient Naturalist with its ETB mill-three / land-to-hand /
    /// else-Treasure ability attached and optionally registered against the
    /// supplied <paramref name="triggers"/> manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so a
    /// qualifying <see cref="Majik.Core.Events.CardMovedEvent"/> automatically
    /// queues the ability on the stack (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human
        // Scout subtypes, {2}{G}, 2/3). The JSON carries no abilities — the ETB
        // mill effect is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, mill three cards. Put a land card from
        //    among the milled cards into your hand. If you can't, create a
        //    Treasure token."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: mill {MillCount}, put a land from among them into hand, " +
            "else create a Treasure token",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // 1. CR 701.13 — mill three. Fx.Mill returns the milled cards
                //    (now in the graveyard); clamped to library size, empty
                //    library is a clean no-op (CR 104.3c).
                var milled = Fx.Mill(controller, MillCount);

                // 2. CR 305 — a land card from among the milled cards. Any Land
                //    card type qualifies (basics, nonbasics, land-typed duals).
                var milledLands = milled.Where(c => c.HasType(CardType.Land)).ToList();

                if (milledLands.Count > 0)
                {
                    // Mandatory "Put … into your hand" — pick which land when
                    // more than one was milled (agentless → first land).
                    var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                    ICard land;
                    if (agent != null && milledLands.Count > 1)
                    {
                        var picked = await agent.ChooseFromPileAsync(
                            chooser: controller,
                            candidates: milledLands,
                            pileLabel: "Land card to put into your hand",
                            intent: BotIntent.Ramp | BotIntent.CardAdvantage,
                            ct: ctx.Ct).ConfigureAwait(false);
                        // Mandatory clause: a null / out-of-set return falls back
                        // to the first milled land (CR — the player must follow
                        // a "Put" instruction when able).
                        land = picked != null && milledLands.Contains(picked)
                            ? picked
                            : milledLands[0];
                    }
                    else
                    {
                        land = milledLands[0];
                    }

                    // Move the chosen land graveyard→hand through the registered
                    // ZoneService when available (so CardMovedEvent fires); fall
                    // back to raw zone mutation for shape/dispatcher-test paths.
                    var zones = ZoneServiceRegistry.Get(controller);
                    if (zones != null)
                    {
                        zones.MoveCard(land, ZoneType.Graveyard, ZoneType.Hand, controller);
                    }
                    else
                    {
                        controller.Zones.Graveyard.RemoveCard(land);
                        controller.Zones.Hand.AddCard(land);
                        land.SetZone(ZoneType.Hand);
                    }
                }
                else
                {
                    // 3. CR 608.2 — "If you can't" (no land among the milled
                    //    cards): create one Treasure token (CR 111.10) under the
                    //    controller's control.
                    TokenFactory.CreateTreasure(controller, ZoneServiceRegistry.Get(controller));
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}

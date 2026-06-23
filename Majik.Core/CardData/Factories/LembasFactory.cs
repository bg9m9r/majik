using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lembas (The Lord of the Rings: Tales of Middle-earth,
/// {2}).
///
/// Artifact — Food. Oracle text (verified against Scryfall 2026-06-23):
///   "When this artifact enters, scry 1, then draw a card.
///    {2}, {T}, Sacrifice this artifact: You gain 3 life.
///    When this artifact is put into a graveyard from the battlefield, its
///    owner shuffles it into their library."
///
/// ## Shape source
/// Card identity (name, {2}, Artifact — Food) AND the standard Food sacrifice
/// ability ("{2}, {T}, Sacrifice this artifact: You gain 3 life.") are
/// materialised from the embedded JSON definition
/// (<c>Majik.Core/CardData/Cards/lembas.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <see cref="ActivatedAbilityDefinition"/> schema already expresses the
/// <c>{2}</c> mana + <c>{T}</c> + sacrifice-self costs and the
/// <c>gain_life_self</c> effect, so that ability needs no hand-rolled C#
/// (same posture as <see cref="GingerbruteFactory"/>'s Food ability).
///
/// Two triggered abilities outgrow the JSON schema and are layered on here:
/// <list type="bullet">
///   <item><b>ETB trigger (CR 603.6a)</b>: "When this artifact enters, scry 1,
///   then draw a card." A single async effect that runs scry 1 (CR 701.20)
///   THEN draws 1 (CR 121.1), in that printed order. Crib of
///   <see cref="FaerieSeerFactory"/>'s ETB scry body followed by
///   <see cref="OptFactory"/>'s scry-then-draw sequencing — agent scry
///   decision via <see cref="IPlayerAgent.ChooseScryDecisionAsync"/> with the
///   pre-agent all-bottom fallback; draw routed through
///   <see cref="Fx.DrawCards"/> so draw-replacement + empty-library SBA loss
///   fire (CR 121.1 / CR 704.5c).</item>
///   <item><b>LTB trigger (CR 603.6c)</b>: "When this artifact is put into a
///   graveyard from the battlefield, its owner shuffles it into their
///   library." Fires on a Battlefield → Graveyard <see cref="CardMovedEvent"/>
///   matching this specific card via <see cref="Triggers.OnDies"/> (the
///   underlying "put into a graveyard from the battlefield" zone-move
///   predicate; CR 700.4's "dies" is the creature wording but the trigger
///   condition is the same B → G move). <c>activeZones</c> includes both
///   Battlefield and Graveyard so the trigger still matches after
///   <see cref="ZoneService"/> stamps the card's Zone = Graveyard before
///   publishing the event (Rancor / Mosswood Dreadknight posture). On
///   resolution the card is moved from its owner's graveyard into its owner's
///   library and the library is shuffled (CR 701.20) via
///   <see cref="LibraryShuffle.ShuffleLibrary"/>. CR 400.7 — "owner": the
///   destination is <see cref="ICard.Owner"/>'s library, not the
///   controller's, so a control-changed Lembas still shuffles back to its true
///   owner.</item>
/// </list>
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both triggers are attached to
///   the card for shape inspection; neither is registered with a
///   <see cref="TriggerManager"/>, and the LTB effect uses raw zone
///   manipulation. Suitable for dispatcher / structural tests. This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully wired.
///   When <paramref name="triggers"/> is supplied both triggers are registered
///   so the relevant <c>CardMovedEvent</c> places them on the stack
///   automatically (CR 603.3).
/// </summary>
[CardName("Lembas")]
public static class LembasFactory
{
    public const string CardName = "Lembas";
    public const string Slug = "lembas";
    private const int ScryAmount = 1;
    private const int DrawAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Lembas with no live wiring. Both triggers are attached to the
    /// card for shape inspection; neither is registered with any
    /// <see cref="TriggerManager"/>, and the LTB effect uses raw zone
    /// manipulation (no shuffle event). Suitable for dispatcher / structural
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Lembas with optional <see cref="TriggerManager"/> /
    /// <see cref="ZoneService"/> wiring. When <paramref name="triggers"/> is
    /// supplied, both triggers are registered so the relevant
    /// <c>CardMovedEvent</c> places them on the stack automatically (CR 603.3).
    /// When <paramref name="zones"/> is supplied the LTB graveyard → library
    /// move goes through <see cref="ZoneService"/>.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (Artifact — Food, {2}) AND the
        // "{2}, {T}, Sacrifice this artifact: You gain 3 life." activated
        // ability are materialised from the embedded JSON definition.
        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        //   "When this artifact enters, scry 1, then draw a card."
        // Scry 1 (CR 701.20) THEN draw 1 (CR 121.1), in printed order. The
        // controller closure re-resolves at execute time so blink /
        // control-change scenarios scry + draw for the correct player.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: scry {ScryAmount}, then draw {DrawAmount} (when this artifact enters)",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return ExecuteScryThenDrawAsync(controller, ctx);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability (CR 603.6c).
        //   "When this artifact is put into a graveyard from the battlefield,
        //    its owner shuffles it into their library."
        // Fires on the Battlefield → Graveyard move (Triggers.OnDies is the
        // B → G zone-move predicate). ActiveZones = {Battlefield, Graveyard}
        // so the zone-guard still matches after ZoneService stamps the card's
        // Zone = Graveyard before publishing the CardMovedEvent (Rancor
        // posture). CR 400.7 — "owner": the destination is card.Owner's
        // library, not the controller's.
        // ----------------------------------------------------------------
        var capturedZones = zones;
        var ltbEffect = new Effect(
            $"{CardName}: its owner shuffles it into their library",
            () =>
            {
                var dest = card.Owner ?? owner;

                if (capturedZones != null)
                {
                    capturedZones.MoveCard(
                        card,
                        ZoneType.Graveyard,
                        ZoneType.Library,
                        controller: null);
                }
                else
                {
                    // Raw zone manipulation — shape-only path.
                    dest.Zones.Graveyard.RemoveCard(card);
                    dest.Zones.Library.AddCard(card);
                    card.SetZone(ZoneType.Library);
                }

                // CR 701.20 — shuffle the owner's library after the card lands.
                LibraryShuffle.ShuffleLibrary(dest, $"{CardName} shuffled into library");
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { ltbEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }

    /// <summary>
    /// Scry 1 (CR 701.20) THEN draw 1 (CR 121.1), in printed order. Look at the
    /// top card; the registered agent (when present) decides whether it goes to
    /// the bottom, otherwise the pre-agent default sends it to the bottom (same
    /// fallback as <see cref="FaerieSeerFactory"/> / <c>CharmingPrince</c>
    /// mode 0). An empty / short library peeks up to N cards and is a clean
    /// no-op for the scry; the draw then proceeds (and an empty-library draw
    /// flags the SBA loss via <see cref="Fx.DrawCards"/>, CR 120.3 / 704.5b).
    /// </summary>
    private static async ValueTask ExecuteScryThenDrawAsync(Player controller, ResolutionContext ctx)
    {
        var peeked = ScryAction.Peek(controller, ScryAmount);
        if (peeked.Count > 0)
        {
            var agent = ctx.Agent ?? AgentRegistry.Get(controller);
            ScryAction.ScryDecision decision;
            if (agent != null)
            {
                decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                    .ConfigureAwait(false);
            }
            else
            {
                // Pre-agent default: all peeked cards to the bottom.
                decision = new ScryAction.ScryDecision(
                    ToBottom: peeked.ToList(),
                    TopOrder: Array.Empty<ICard>());
            }

            ScryAction.Apply(controller, peeked.Count, decision);
        }

        // CR 121.1 — draw AFTER the scry resolves. Routed through Fx.DrawCards
        // so draw-replacement + empty-library SBA loss fire.
        Fx.DrawCards(controller, DrawAmount);
    }
}

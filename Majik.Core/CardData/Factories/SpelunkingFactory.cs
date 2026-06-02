using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spelunking (The Lost Caverns of Ixalan, {2}{G}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "When this enchantment enters, draw a card, then you may put a land card
///    from your hand onto the battlefield. If you put a Cave onto the
///    battlefield this way, you gain 4 life.
///    Lands you control enter untapped."
///
/// The base shape (name, Enchantment, {2}{G}) is materialised from the
/// embedded JSON definition (<c>spelunking.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours (the
/// ETB draw + may-play-land trigger, and the "lands you control enter
/// untapped" static replacement) are layered on here, since the JSON
/// <c>AbilityDefinition</c> schema doesn't express the enters trigger or a
/// replacement-effect static (same posture as <see cref="GrowthSpiralFactory"/>
/// for the land drop and <see cref="ThaliaHereticCatharFactory"/> for the
/// enters-modifying static).
///
/// ## Implemented (v1)
/// - <b>ETB trigger (CR 603.6a)</b> — a <see cref="CardMovedEvent"/> trigger
///   that fires when THIS enchantment enters (ToZone == Battlefield). On
///   resolve, in printed order:
///     1. <b>Draw a card</b> (CR 121.1) via <see cref="Fx.DrawCards"/> — an
///        empty library stamps the CR 704.5b pending-loss flag rather than
///        throwing. The draw runs FIRST, so a land just drawn is itself a
///        legal candidate for the put-land step below.
///     2. <b>"You may put a land card from your hand onto the battlefield"</b>
///        (CR 113.6c) — identical primitive to <see cref="GrowthSpiralFactory"/>:
///        agent-driven opt-in + land-pick (intent <see cref="BotIntent.Ramp"/>)
///        when an agent is registered, deterministic no-agent fallback
///        otherwise. Putting a land directly onto the battlefield bypasses the
///        per-turn land-drop cap (CR 305.2) — this does NOT touch the land-drop
///        tracker (same as Growth Spiral).
///     3. <b>"If you put a Cave onto the battlefield this way, you gain 4
///        life."</b> (CR 119.3) — when the land actually placed this way has
///        the <see cref="CardSubtype.Cave"/> subtype the controller gains 4
///        life via <see cref="Fx.GainLife"/>. Gated on a land having actually
///        been put — declining the "may", or having no land in hand, gains no
///        life.
/// - <b>"Lands you control enter untapped." (CR 614.1c)</b> — a static
///   replacement wired via <see cref="SpelunkingLandsEnterUntappedEffect"/>:
///   while Spelunking is on the battlefield, any land entering under its
///   controller's control has its <see cref="ZoneMoveIntent.EntersTapped"/>
///   forced false (the structural inverse of Thalia, Heretic Cathar). The
///   lifecycle unregisters when Spelunking leaves the battlefield. Note the
///   land put by step 2 enters under the controller's control while Spelunking
///   is already on the battlefield, so a tap-land Cave put this way also
///   enters untapped.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt without an agent</b>: the no-agent fallback always
///   plays the first land in hand when one exists. A registered agent gets the
///   full opt-in + land-pick prompts. Same simplification every "may" factory
///   carries (Growth Spiral / Uro / Sakura-Tribe Scout).
/// - <b>CR 616.1 replacement ordering</b>: when a self-tapping land and the
///   "enter untapped" static both apply, the affected player chooses the
///   order. <see cref="SpelunkingLandsEnterUntappedEffect"/> realises the
///   controller's preferred (untapped) outcome deterministically by only
///   firing to UNDO a tap, so it lands last regardless of registration order
///   — see that type's CR 616.1 note. (Thalia, Heretic Cathar still carries
///   the registration-order caveat for its tap-direction effect.)
/// </summary>
[CardName("Spelunking")]
public static class SpelunkingFactory
{
    public const string CardName = "Spelunking";
    public const string Slug = "spelunking";
    public const int CaveLifeGain = 4;

    /// <summary>
    /// Construct Spelunking with no live wiring. The ETB trigger is attached
    /// for shape observability but the "lands enter untapped" replacement is
    /// NOT registered (no replacement bus). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, eventBus: null, triggers: null, replacementBus: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Spelunking.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for ETB tracking + the
    /// untapped-static lifecycle. May be null.</param>
    /// <param name="triggers">TriggerManager the ETB trigger is registered
    /// with so it surfaces as pending. May be null.</param>
    /// <param name="replacementBus">When supplied, the "lands you control
    /// enter untapped" replacement is registered while Spelunking is on the
    /// battlefield. May be null — the static simply won't activate.</param>
    /// <param name="zoneService">When supplied the put-land move routes
    /// through <see cref="ZoneService.MoveCard"/> so ETB triggers /
    /// replacements on the played land fire (CR 603.6a). May be null.</param>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacementBus,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {2}{G}). The JSON carries no abilities — both behaviours are layered
        // on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Enchantment)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When this enchantment enters, draw a card, then you may put a
        //    land card from your hand onto the battlefield. If you put a Cave
        //    onto the battlefield this way, you gain 4 life."
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: draw a card, then you may put a land from hand onto the battlefield (Cave => gain {CaveLifeGain} life).",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 121.1 — "draw a card." Empty library stamps the CR 704.5b
                // pending-loss flag via Fx.DrawCards (no throw). Runs FIRST so
                // a land just drawn is a legal put-land candidate below.
                Fx.DrawCards(controller, 1);

                // CR 113.6c — "then you may put a land card from your hand onto
                // the battlefield." CR 119.3 — "If you put a Cave onto the
                // battlefield this way, you gain 4 life."
                await PutLandThenMaybeGainLifeAsync(controller, zoneService, ctx)
                    .ConfigureAwait(false);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // "Lands you control enter untapped." — CR 614.1c. Registered as a
        // one-sided global ETB replacement while Spelunking is on the
        // battlefield (inverse of Thalia, Heretic Cathar).
        // ----------------------------------------------------------------
        if (replacementBus != null)
        {
            var lifecycle = new SpelunkingLandsEnterUntappedEffect(
                source: card,
                replacementBus: replacementBus,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 113.6c — optional "put a land card from your hand onto the
    /// battlefield", followed by CR 119.3 — gain 4 life iff the land actually
    /// put was a Cave. Candidate set = every land card in the controller's
    /// hand. Agent-driven opt-in + land-pick when an agent is registered
    /// (intent <see cref="BotIntent.Ramp"/>); no-agent fallback auto-accepts
    /// and takes the first land deterministically. No land in hand, or the
    /// "may" declined, is a clean no-op (no life gained). Movement prefers
    /// <paramref name="zoneService"/> (then the registry, then raw zone
    /// manipulation) so ETB-on-land triggers fire (CR 603.6a).
    /// </summary>
    private static async ValueTask PutLandThenMaybeGainLifeAsync(
        Player controller,
        ZoneService? zoneService,
        ResolutionContext ctx)
    {
        var candidates = controller.Zones.Hand.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();
        if (candidates.Count == 0) return; // No lands → "may" no-op.

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);

        ICard? land;
        if (agent != null)
        {
            // CR 117.1a — optional "you may" gesture, resolved by the agent.
            var optIn = await agent.ChooseYesNoAsync(
                    "Put a land card from your hand onto the battlefield?",
                    BotIntent.Ramp)
                .ConfigureAwait(false);
            if (!optIn) return;

            land = await agent.ChooseFromHandAsync(controller, candidates, BotIntent.Ramp)
                .ConfigureAwait(false);
            // CR 608.2b — re-validate the agent's pick at resolution.
            if (land == null || !candidates.Contains(land)) return;
        }
        else
        {
            // No-agent fallback: auto-accept + first land (v1 posture shared
            // with Growth Spiral / Uro / Sakura-Tribe Scout).
            land = candidates[0];
        }

        // CR 603.6a — prefer ZoneService.MoveCard so ETB triggers /
        // replacements on the played land fire (including Spelunking's own
        // "lands you control enter untapped" static). Fall back to the
        // registry, then raw zone manipulation for the shape/test path.
        var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(controller);
        if (effectiveZones != null)
        {
            effectiveZones.MoveCard(land, ZoneType.Hand, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Hand.RemoveCard(land);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            land.SetController(controller);
        }

        // CR 119.3 — "If you put a Cave onto the battlefield this way, you gain
        // 4 life." Gated on a land having actually been put this way; the Cave
        // subtype check uses CardSubtype.Cave (CR 205.3i).
        if (land.HasSubtype(CardSubtype.Cave))
        {
            Fx.GainLife(controller, CaveLifeGain);
        }
    }
}

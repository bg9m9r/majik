using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bonny Pall, Clearcutter (Bloomburrow Commander,
/// {3}{G}{U}{U}). Legendary Creature — Giant Scout, 6/5. Oracle text
/// (verified against Scryfall 2026-06-24):
///   "Reach
///    When Bonny Pall enters, create Beau, a legendary blue Ox creature token
///    with 'Beau's power and toughness are each equal to the number of lands
///    you control.'
///    Whenever you attack, draw a card, then you may put a land card from your
///    hand or graveyard onto the battlefield."
///
/// ## Shape source
/// Card identity (name, Legendary supertype, Creature — Giant Scout,
/// {3}{G}{U}{U}, 6/5) is materialised from the embedded JSON definition
/// (<c>bonny-pall-clearcutter.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="CultivatorColossusFactory"/>). Reach, the ETB token-create
/// trigger, and the attack trigger are layered on in code below — none of
/// those ability shapes is expressed in the JSON schema yet.
///
/// ## Implemented (v1)
/// - <b>Reach (CR 702.17)</b> — a <see cref="KeywordAbility"/> marker so
///   <c>ICard.Abilities</c> reflects the printed line and the combat-block
///   path reads it (same posture as <see cref="CanopySpiderFactory"/>).
/// - <b>ETB trigger — CR 603.1</b>: "When Bonny Pall enters, create Beau, a
///   legendary blue Ox creature token with 'Beau's power and toughness are
///   each equal to the number of lands you control.'" Fires on the
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> condition; on resolution a
///   single Beau token is minted via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (a legendary blue Ox; the
///   Legendary supertype is added with <see cref="Card.AddSupertype"/> since
///   <see cref="TokenFactory.TokenSpec"/> carries no supertype field). Beau's
///   characteristic-defining P/T (CR 604.3 / 613.7a) is wired the same way as
///   <see cref="KarnScionOfUrzaFactory"/>'s Construct: a
///   <see cref="CdaPowerToughnessEffect"/> whose power AND toughness evaluators
///   each count the lands the token's controller controls, registered on the
///   supplied <see cref="ContinuousEffectsService"/> (the token is wired to
///   consult the layer system via <see cref="Card.ActiveEffects"/>). "You" in
///   the token's reminder text is the token's controller (CR 109.5).
/// - <b>Attack trigger — CR 508.1 / 109.5</b>: "Whenever you attack, draw a
///   card, then you may put a land card from your hand or graveyard onto the
///   battlefield." Fires on <see cref="AttackersDeclaredEvent"/> when Bonny
///   Pall's controller is the attacking player ("Whenever you attack"). On
///   resolution: CR 121.1 draw a card (<see cref="Fx.DrawCards"/>), then the
///   optional "you may put a land card from your hand or graveyard onto the
///   battlefield" — a single land card (CR 113.6c — alt-zone "play", NOT a
///   land drop, so <see cref="Majik.Core.Game.LandDropTracker"/> is untouched).
///   The land may come from hand OR graveyard; moves route through a live
///   <see cref="ZoneService"/> when supplied so ETB-on-land triggers /
///   replacements fire (CR 603.6a). It enters untapped (the oracle does not say
///   "tapped").
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt without an agent</b>: the no-agent fallback auto-takes
///   the first land available (hand preferred, then graveyard). A registered
///   agent gets the opt-in + source/land pick. Same simplification every "may
///   put a land" factory carries (Cultivator Colossus / Uro / Growth Spiral).
/// - <b>Beau's legend rule / token uniqueness</b>: a single Beau is created per
///   ETB; the legend rule SBA (CR 704.5j) governs duplicates the same as any
///   other legendary permanent — no bespoke handling here.
/// </summary>
[CardName("Bonny Pall, Clearcutter")]
public static class BonnyPallClearcutterFactory
{
    public const string CardName = "Bonny Pall, Clearcutter";
    public const string Slug = "bonny-pall-clearcutter";

    /// <summary>Granted keyword — CR 702.17 Reach.</summary>
    public const string Reach = "Reach";

    /// <summary>The Beau token's printed name.</summary>
    public const string BeauName = "Beau";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Bonny Pall with correct identity + Reach + both triggers
    /// attached to the card shape, but NO live <see cref="ContinuousEffectsService"/>,
    /// <see cref="ZoneService"/>, event-bus, or <see cref="TriggerManager"/>
    /// wiring. On this path the ETB trigger still mints a Beau token (without the
    /// dynamic CDA P/T) and the attack trigger still draws + auto-takes a land
    /// when one is available. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, zoneService: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Bonny Pall, Clearcutter.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the Beau token's
    /// CDA P/T (<see cref="CdaPowerToughnessEffect"/>) registers against. May be
    /// null — the token is then a vanilla 0/0 shell.</param>
    /// <param name="zoneService">Routes the attack trigger's land move (hand or
    /// graveyard → battlefield) through <see cref="ZoneService.MoveCard"/> so
    /// ETB-on-land triggers fire (CR 603.6a). May be null (raw zone move).</param>
    /// <param name="triggers">TriggerManager the ETB + attack triggers register
    /// with so dispatched events land them on the stack. May be null.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature — Giant Scout, {3}{G}{U}{U}, 6/5). No abilities in the JSON —
        // all three layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.17 — Reach keyword marker.
        card.AddAbility(new KeywordAbility(Reach, card, owner));

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1. "When Bonny Pall enters, create Beau ..."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: ETB — create Beau (legendary blue Ox, P/T = lands you control)",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                CreateBeauToken(controller, zoneService, effects);
                return ValueTask.CompletedTask;
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
        // Attack trigger — CR 508.1 / 109.5. "Whenever you attack, draw a
        // card, then you may put a land card from your hand or graveyard
        // onto the battlefield."
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: on attack, draw a card, then may put a land from hand or graveyard onto the battlefield",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return ResolveAttackTriggerAsync(controller, zoneService, ctx);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
                // "Whenever you attack" — only when Bonny Pall's controller is
                // the attacking player (CR 508.1 / 109.5).
                ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner)),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Count the lands the controller controls at compute time (CR 109.5 — only
    /// the controller's lands count). Pure helper exposed for tests; mirrors the
    /// evaluator baked into Beau's CDA.
    /// </summary>
    public static int CountControllerLands(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Land));
    }

    /// <summary>
    /// Mint Beau — "a legendary blue Ox creature token with 'Beau's power and
    /// toughness are each equal to the number of lands you control.'" Exposed
    /// for tests; production callers go through the ETB trigger body.
    /// </summary>
    public static Creature CreateBeauToken(
        Player controller,
        ZoneService? zoneService,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // CR 111.4 — blue Ox token. Printed P/T is the CDA "*"; seed base 0/0
        // (CR 208.2c — "*" treated as 0; the Layer 7a CDA overwrites on every
        // Compute).
        var spec = new TokenFactory.TokenSpec(
            Name: BeauName,
            Power: 0,
            Toughness: 0,
            Subtypes: new[] { CardSubtype.Ox },
            Keywords: null,
            Colors: new[] { ManaColor.Blue });

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 205.4 — Beau is a *legendary* token. TokenSpec carries no supertype
        // field, so add it directly (same assembly; AddSupertype is internal).
        token.AddSupertype(CardSupertype.Legendary);

        // CR 604.3 / 613.7a — characteristic-defining P/T: power AND toughness
        // each equal the number of lands the token's controller controls
        // (CR 109.5 — "you" is the token's controller). Same wiring posture as
        // Karn, Scion of Urza's Construct token.
        if (effects != null)
        {
            token.ActiveEffects = effects;
            effects.Register(new CdaPowerToughnessEffect(
                token,
                _ => CountControllerLands(controller),
                _ => CountControllerLands(controller)));
        }

        return token;
    }

    // --- Attack trigger resolution (CR 508.1) ------------------------------

    /// <summary>
    /// CR 121.1 draw a card, then the optional "you may put a land card from
    /// your hand or graveyard onto the battlefield" (CR 113.6c — alt-zone play,
    /// NOT a land drop). Agent-driven opt-in + source/land pick when an agent is
    /// registered (intent <see cref="BotIntent.Ramp"/>); no-agent fallback
    /// auto-takes the first land (hand preferred, then graveyard).
    /// </summary>
    private static async ValueTask ResolveAttackTriggerAsync(
        Player controller,
        ZoneService? zoneService,
        ResolutionContext ctx)
    {
        // CR 121.1 — "draw a card". An empty library stamps the CR 704.5b
        // pending-loss flag (no throw).
        Fx.DrawCards(controller, 1);

        // "then you may put a land card from your hand or graveyard onto the
        // battlefield." Candidates: land cards in hand OR graveyard.
        var handLands = controller.Zones.Hand.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();
        var graveyardLands = controller.Zones.Graveyard.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();

        if (handLands.Count == 0 && graveyardLands.Count == 0) return;

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);

        ICard? land;
        ZoneType fromZone;
        if (agent != null)
        {
            // CR 117.1a — optional "you may", resolved by the agent.
            var optIn = await agent.ChooseYesNoAsync(
                    "Put a land card from your hand or graveyard onto the battlefield?",
                    BotIntent.Ramp)
                .ConfigureAwait(false);
            if (!optIn) return;

            // Offer the combined hand+graveyard land pool; default the pick to
            // the first eligible land if the agent declines to pick a member.
            var pool = handLands.Concat(graveyardLands).ToList();
            land = await agent.ChooseFromHandAsync(controller, pool, BotIntent.Ramp)
                .ConfigureAwait(false);
            if (land == null || !pool.Contains(land)) return; // CR 608.2b re-check
            fromZone = handLands.Contains(land) ? ZoneType.Hand : ZoneType.Graveyard;
        }
        else
        {
            // No-agent fallback: auto-take the first available land, preferring
            // hand over graveyard (v1 posture shared with Cultivator Colossus).
            if (handLands.Count > 0)
            {
                land = handLands[0];
                fromZone = ZoneType.Hand;
            }
            else
            {
                land = graveyardLands[0];
                fromZone = ZoneType.Graveyard;
            }
        }

        PutLandOntoBattlefield(controller, land, fromZone, zoneService);
    }

    /// <summary>
    /// Move <paramref name="land"/> from <paramref name="fromZone"/> (hand or
    /// graveyard) to <paramref name="controller"/>'s battlefield untapped (the
    /// oracle does not say "tapped"). Prefers <paramref name="zoneService"/>,
    /// then <see cref="ZoneServiceRegistry"/>, then raw zone manipulation (the
    /// shape/test path) — so ETB-on-land triggers / replacements fire when a
    /// live service is available (CR 603.6a).
    /// </summary>
    private static void PutLandOntoBattlefield(
        Player controller,
        ICard land,
        ZoneType fromZone,
        ZoneService? zoneService)
    {
        var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(controller);
        if (effectiveZones != null)
        {
            effectiveZones.MoveCard(land, fromZone, ZoneType.Battlefield, controller);
        }
        else
        {
            var source = fromZone == ZoneType.Hand
                ? controller.Zones.Hand
                : controller.Zones.Graveyard;
            source.RemoveCard(land);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            land.SetController(controller);
        }
    }
}

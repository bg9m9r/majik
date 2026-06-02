using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Simulacrum Synthesizer (The Brothers' War,
/// {2}{U}). Artifact. Oracle text (verified against Scryfall):
///   "When this artifact enters, scry 2.
///    Whenever another artifact you control with mana value 3 or greater
///    enters, create a 0/0 colorless Construct artifact creature token with
///    'This token gets +1/+1 for each artifact you control.'"
///
/// The base shape (name, Artifact type, {2}{U}) is materialised from the
/// embedded JSON definition (<c>simulacrum-synthesizer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Both triggered abilities are
/// layered on in code — the JSON ability schema does not express a scry
/// effect nor a token-creation trigger (same posture as
/// <see cref="UrzaLordHighArtificerFactory"/> /
/// <see cref="FaerieSeerFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>ETB scry-2 trigger (CR 603.1 / CR 603.6a / CR 701.20)</b>: "When
///   this artifact enters, scry 2." Unconditional self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. Resolution runs the
///   standard <see cref="ScryAction"/> pipeline for N=2, consulting the
///   registered <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseScryDecisionAsync"/> when present; the
///   pre-agent default sends all peeked cards to the bottom — identical
///   scry body to <see cref="FaerieSeerFactory"/>.
///
/// - <b>Another-artifact-ETB token trigger (CR 603.1 / CR 603.6a)</b>:
///   "Whenever another artifact you control with mana value 3 or greater
///   enters, create a 0/0 colorless Construct artifact creature token with
///   'This token gets +1/+1 for each artifact you control.'" Wired via
///   <see cref="EventTriggerCondition{T}"/> over <see cref="CardMovedEvent"/>
///   filtered to:
///     1. <c>ToZone == Battlefield</c>.
///     2. The entering card is NOT Simulacrum Synthesizer itself (the
///        printed "<em>another</em> artifact" exclusion — CR 109.5;
///        Synthesizer's own ETB only triggers the scry, not the token).
///     3. The entering card has <see cref="CardType.Artifact"/> (CR 301.1 —
///        covers Artifact Creatures / Artifact lands too).
///     4. The entering card's controller equals Synthesizer's controller
///        ("you control" — CR 109.5).
///     5. The entering card's mana value is 3 or greater (CR 202.3 —
///        <see cref="Card.ManaCostValue"/> total; tokens with no printed
///        mana cost have mana value 0 and never satisfy this gate).
///   On resolution the same 0/0 colourless Construct artifact-creature token
///   minted by Urza / Karn, Scion of Urza is created via
///   <see cref="KarnScionOfUrzaFactory.CreateConstructToken"/> — the dynamic
///   "+1/+1 per artifact you control" CDA P/T rider (CR 613.7a) is registered
///   on the supplied <see cref="ContinuousEffectsService"/>. Without an
///   effects service the token still enters as a 0/0 (SBA 704.5f would sweep
///   it absent the rider — test posture only).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Both triggers are
///   attached for shape inspection; neither is registered with a
///   <see cref="TriggerManager"/>, and the Construct token's +1/+1 rider
///   no-ops (no effects service). Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?, ContinuousEffectsService?)"/>
///   — fully wired. Both triggers are registered when
///   <paramref name="triggers"/> is supplied; <paramref name="zoneService"/>
///   threads the Construct token's battlefield entry through
///   <see cref="ZoneService"/> so its ETB publishes a
///   <see cref="CardMovedEvent"/>; <paramref name="effects"/> registers the
///   token's dynamic P/T rider.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Live TriggerManager wiring on the single-arg path</b>: both triggers
///   are attached structurally for shape inspection; the runtime overload
///   registers them so bus-driven ETB firing works (same posture as
///   <see cref="UrzaLordHighArtificerFactory"/>).
/// </summary>
[CardName("Simulacrum Synthesizer")]
public static class SimulacrumSynthesizerFactory
{
    public const string CardName = "Simulacrum Synthesizer";
    public const string Slug = "simulacrum-synthesizer";
    private const int ScryAmount = 2;

    /// <summary>CR 202.3 — the printed "mana value 3 or greater" gate.</summary>
    private const int ManaValueGate = 3;

    /// <summary>
    /// Construct Simulacrum Synthesizer with no live runtime services. Both
    /// triggers are attached structurally (not registered on a
    /// <see cref="TriggerManager"/>); the Construct token's +1/+1 rider
    /// no-ops without a continuous-effects service. Suitable for identity /
    /// shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null, effects: null);

    /// <summary>
    /// Construct Simulacrum Synthesizer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not consumed directly here; reserved for future
    /// LTB / lifecycle hooks.</param>
    /// <param name="triggers">Optional. When supplied, both the ETB scry
    /// trigger and the another-artifact-ETB token trigger are registered so
    /// <see cref="CardMovedEvent"/> publications auto-queue them.</param>
    /// <param name="zoneService">Optional. Forwarded to the Construct token
    /// spawn so its battlefield entry publishes <see cref="CardMovedEvent"/>
    /// and downstream ETB triggers fire.</param>
    /// <param name="effects">Optional. Used to register the Construct token's
    /// "+1/+1 per artifact you control" CDA P/T rider — without it the token
    /// is a 0/0 SBA victim.</param>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = eventBus;

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB scry-2 trigger — CR 603.1 / CR 603.6a / CR 701.20.
        //   "When this artifact enters, scry 2."
        // Unconditional self-ETB. The controller closure re-resolves at
        // execute time so blink / control-change scenarios scry for the
        // correct player. Same scry body as Faerie Seer.
        // ----------------------------------------------------------------
        var scryEffect = new Effect(
            $"{CardName}: scry {ScryAmount} (when this artifact enters)",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return ExecuteScryAsync(controller, ctx);
            });

        var scryTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { scryEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(scryTrigger);
        triggers?.RegisterTriggeredAbility(scryTrigger);

        // ----------------------------------------------------------------
        // Another-artifact-ETB token trigger — CR 603.1 / CR 603.6a.
        //   "Whenever another artifact you control with mana value 3 or
        //    greater enters, create a 0/0 colorless Construct artifact
        //    creature token with 'This token gets +1/+1 for each artifact
        //    you control.'"
        //
        // Predicate:
        //   - ToZone is Battlefield.
        //   - The entering card is NOT Synthesizer itself ("another" —
        //     CR 109.5; Synthesizer's own ETB only fires the scry).
        //   - The entering card has CardType.Artifact (CR 301.1 — covers
        //     Artifact Creatures + Artifact lands).
        //   - The entering card's controller is Synthesizer's controller
        //     ("you control" — CR 109.5).
        //   - The entering card's mana value is >= 3 (CR 202.3).
        // ----------------------------------------------------------------
        var tokenCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (ReferenceEquals(e.Card, card)) return false; // "another"
            if (!e.Card.HasType(CardType.Artifact)) return false;
            if (!ReferenceEquals(e.Card.Controller, card.Controller ?? owner)) return false;
            return ManaValueOf(e.Card) >= ManaValueGate;
        });

        var tokenEffect = new Effect(
            $"{CardName}: create 0/0 Construct artifact-creature token (+1/+1 per artifact)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return; // CR 603.6c
                var controller = card.Controller ?? owner;
                KarnScionOfUrzaFactory.CreateConstructToken(controller, zoneService, effects);
            });

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: tokenCondition,
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return card;
    }

    /// <summary>
    /// CR 202.3 — mana value of the entering card. Reads the printed mana
    /// cost via <see cref="Card.ManaCostValue"/>; non-Card / token entities
    /// with no printed mana cost report mana value 0 (CR 111.7 — tokens have
    /// no mana cost, so their mana value is 0).
    /// </summary>
    private static int ManaValueOf(ICard card)
    {
        if (card is Card c) return c.ManaCostValue.TotalValue;
        return 0;
    }

    /// <summary>
    /// Scry 2 (CR 701.20). Look at the top two cards of the library; the
    /// registered agent (when present) decides how many go to the bottom and
    /// the order of the rest. Pre-agent default: all peeked cards to the
    /// bottom (same fallback as <see cref="FaerieSeerFactory"/>). An empty /
    /// short library peeks up to N cards and is a clean no-op.
    /// </summary>
    private static async ValueTask ExecuteScryAsync(Player controller, ResolutionContext ctx)
    {
        var peeked = ScryAction.Peek(controller, ScryAmount);
        if (peeked.Count == 0) return;

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                .ConfigureAwait(false);
        }
        else
        {
            decision = new ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>());
        }

        ScryAction.Apply(controller, peeked.Count, decision);
    }
}

using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Experimental Synthesizer (Aetherdrift, {R}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "When this artifact enters or leaves the battlefield, exile the top
///    card of your library. Until end of turn, you may play that card.
///    {2}{R}, Sacrifice this artifact: Create a 2/2 white Samurai creature
///    token with vigilance. Activate only as a sorcery."
///
/// The base shape (name, Artifact, {R}) is materialised from the embedded
/// JSON definition (<c>experimental-synthesizer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (the enters-or-leaves impulse trigger and the sacrifice-for-token
/// activated ability) are layered on here, since the JSON
/// <c>AbilityDefinition</c> schema doesn't express the
/// enters-or-leaves trigger or the runtime exile-cast grant.
///
/// ## Implemented (v1)
/// - <b>Enters-or-leaves trigger (CR 603.6a / CR 603.10b)</b> — a
///   <see cref="CardMovedEvent"/> trigger that fires when THIS artifact
///   enters (ToZone == Battlefield) or leaves (FromZone == Battlefield) the
///   battlefield. On resolve: exile the top card of the controller's library
///   (CR 701.20) and stamp a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) so the controller may play
///   it until end of turn (CR 118.9) — the same impulse-draw primitive as
///   <see cref="EmberheartChallengerFactory"/> / <see cref="LightUpTheStageFactory"/>,
///   with an "until end of turn" duration (a single Cleanup, CR 514.2). The
///   trigger stays active in the graveyard (<see cref="ZoneType.Graveyard"/>)
///   so the LEAVES half still resolves after the artifact is gone — same
///   posture as <see cref="PashalikMonsFactory"/>'s self-dies trigger
///   (CR 603.6d — leaves-the-battlefield triggers look back in time).
/// - <b>{2}{R}, Sacrifice this artifact: create a 2/2 white Samurai token
///   with vigilance. Activate only as a sorcery.</b> — one
///   <see cref="ActivatedAbility"/> (CR 602.1) with a
///   <see cref="ManaCostCost"/> ({2}{R}) + <see cref="SacrificeSelfCost"/>
///   (CR 701.16), flagged <c>sorcerySpeed: true</c> (CR 307.5). On resolve:
///   mint the 2/2 white Samurai with Vigilance via
///   <see cref="TokenFactory.CreateOnBattlefield"/> — the exact token shape
///   The Wandering Emperor's −1 makes (CR 111.4 / CR 702.20).
///
/// ## Deferred (v1 gaps)
/// - <b>"May play that card" includes lands</b>: the runtime exile-cast
///   grant authorises casting; an exiled land would need a parallel "play
///   this land from exile" grant. v1 ships the spell-only authorisation,
///   matching the Emberheart / LightUpTheStage posture.
/// - <b>Empty-library exile</b>: if the library is empty the exile is a
///   no-op (CR 701.20 imposes no SBA flag for an exile that finds nothing).
/// - <b>"You may play" permission</b>: the grant is always stamped — "you
///   MAY play that card" is a permission, not a forced action, so no agent
///   prompt is needed at resolution.
/// </summary>
[CardName("Experimental Synthesizer")]
public static class ExperimentalSynthesizerFactory
{
    public const string CardName = "Experimental Synthesizer";
    public const string Slug = "experimental-synthesizer";
    public const string ActivatedManaCost = "{2}{R}";

    /// <summary>
    /// Construct Experimental Synthesizer with no live wiring. The
    /// enters-or-leaves trigger is attached for shape observability but its
    /// "until end of turn" cleanup is not scheduled (no event bus). Suitable
    /// for dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Experimental Synthesizer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the enters-or-leaves resolve
    /// effect schedules its "until end of turn" exile-cast cleanup on the
    /// next Cleanup step (CR 514.2).</param>
    /// <param name="triggers">TriggerManager the enters-or-leaves trigger is
    /// registered with so it surfaces as pending. May be null.</param>
    /// <param name="zoneService">When supplied the Samurai token's ETB
    /// publishes <see cref="CardMovedEvent"/> so downstream subscribers see
    /// it enter (CR 603.6a / CR 701.20).</param>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
        => Create(owner, eventBus, triggers, zoneService: null);

    /// <summary>
    /// Construct Experimental Synthesizer with optional runtime services,
    /// threading a <see cref="ZoneService"/> so the minted Samurai token's
    /// ETB publishes <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact, {R}).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Enters-or-leaves trigger — CR 603.6a (enters) / CR 603.10b (leaves).
        //   "When this artifact enters or leaves the battlefield, exile the
        //    top card of your library. Until end of turn, you may play that
        //    card."
        // ----------------------------------------------------------------
        var entersOrLeavesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Card, card)) return false;
            // Enters: anything → Battlefield. Leaves: Battlefield → anything.
            var enters = e.ToZone == ZoneType.Battlefield;
            var leaves = e.FromZone == ZoneType.Battlefield;
            return enters || leaves;
        });

        var impulseEffect = new Effect(
            $"{CardName}: exile the top card of your library; until end of turn you may play that card",
            () =>
            {
                var controller = card.Controller ?? owner;

                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — exile finds nothing (CR 701.20)

                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Exile.AddCard(top);
                top.SetZone(ZoneType.Exile);

                if (top is not Card concrete) return;

                // CR 118.9 — "you may play that card" with no alternate-cost
                // rider: the grant authorises casting for the printed mana
                // cost. Same impulse-draw primitive as Emberheart Challenger.
                concrete.GrantRuntimeExileCast(controller, concrete.ManaCostValue);

                // CR 514.2 — "until end of turn" is a SINGLE Cleanup: clear
                // the grant at the next Cleanup step. (The leaves half can
                // fire on an opponent's turn — the artifact may be sacrificed
                // or destroyed at instant speed — so we clear on the first
                // Cleanup seen rather than gating on the controller; CR 514.2
                // ends the duration at the current turn's cleanup regardless
                // of whose turn it is.) Without a bus the grant persists until
                // cleared by hand (test path).
                if (eventBus == null) return;

                Action<StepStartedEvent>? handler = null;
                handler = (se) =>
                {
                    if (se.StepType != PhaseStateType.Cleanup) return;
                    concrete.ClearRuntimeExileCast();
                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        var entersOrLeavesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: entersOrLeavesCondition,
            effects: new IEffect[] { impulseEffect },
            // CR 603.6d — keep active in the graveyard so the LEAVES half
            // still resolves after the artifact has left the battlefield
            // (e.g. sacrificed to its own activated ability).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(entersOrLeavesTrigger);
        triggers?.RegisterTriggeredAbility(entersOrLeavesTrigger);

        // ----------------------------------------------------------------
        // {2}{R}, Sacrifice this artifact: Create a 2/2 white Samurai
        // creature token with vigilance. Activate only as a sorcery.
        // CR 602.1 — activated ability. CR 701.16 — sacrifice cost.
        // CR 307.5 — "Activate only as a sorcery" rider.
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: create a 2/2 white Samurai creature token with vigilance",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateSamuraiToken(controller, zoneService);
            });

        var tokenAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedManaCost),
                new SacrificeSelfCost(card),
            },
            effects: new IEffect[] { tokenEffect },
            sorcerySpeed: true);

        card.AddAbility(tokenAbility);

        return card;
    }

    /// <summary>
    /// CR 111.4 / CR 702.20 — create one 2/2 white Samurai creature token
    /// with Vigilance under <paramref name="controller"/>'s control. Same
    /// token shape The Wandering Emperor's −1 mints.
    /// </summary>
    public static Creature CreateSamuraiToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Samurai",
            Power: 2,
            Toughness: 2,
            Subtypes: new[] { CardSubtype.Samurai },
            Keywords: new[] { "Vigilance" },
            // CR 105.2a / CR 111.4 — "2/2 white Samurai creature token".
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}

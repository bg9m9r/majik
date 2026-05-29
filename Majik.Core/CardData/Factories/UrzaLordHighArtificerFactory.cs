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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Urza, Lord High Artificer (Modern Horizons,
/// {2}{U}{U}). Legendary Creature — Human Artificer 1/4. Oracle text
/// (verified against Scryfall):
///   "When Urza enters, create a 0/0 colorless Construct artifact creature
///    token with 'This token gets +1/+1 for each artifact you control.'
///    Tap an untapped artifact you control: Add {U}.
///    {5}: Shuffle your library, then exile the top card. Until end of turn,
///    you may play that card without paying its mana cost."
///
/// The base shape (name, Legendary supertype, Human/Artificer subtypes,
/// {2}{U}{U}, 1/4) is materialised from the embedded JSON definition
/// (<c>urza-lord-high-artificer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// are layered on top here — the JSON <c>AbilityDefinition</c> schema
/// doesn't yet express token creation, the tap-another-artifact mana
/// ability, or the impulse-cast activated ability, so they live in the
/// factory (same posture as <see cref="StormscaleScionFactory"/> and the
/// other JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>ETB triggered ability</b> (CR 603.6a): "When Urza enters, create a
///   0/0 colorless Construct artifact creature token with 'This token gets
///   +1/+1 for each artifact you control.'" Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; the resolve body reuses
///   <see cref="KarnScionOfUrzaFactory.CreateConstructToken"/> — the exact
///   same 0/0 colourless Construct artifact-creature token with the dynamic
///   "+1/+1 per artifact you control" CDA P/T rider (CR 613.7a) registered
///   on the supplied <see cref="ContinuousEffectsService"/>. Karn, Scion of
///   Urza, Urza's Saga, and Urza himself all print the identical token, so
///   the spawn is centralised. Without an effects service the token still
///   enters as a 0/0 (SBA 704.5f sweeps it).
/// - <b>Mana ability — "Tap an untapped artifact you control: Add {U}"</b>
///   (CR 605.1). Wired as a stack-free <see cref="ManaAbility"/> using the
///   <c>tapsAsCost: false</c> overload — Urza himself is NOT tapped; the
///   entire activation cost is a <see cref="TapAnotherUntappedArtifactCost"/>
///   (CR 118.12). Gated on the existence of an eligible untapped artifact;
///   the cost taps it and the pool gains {U}. Distinct from Springleaf
///   Drum's "{T}, Tap an untapped creature" (which DOES self-tap).
/// - <b>Activated impulse ability — "{5}: Shuffle your library, then exile
///   the top card. Until end of turn, you may play that card without paying
///   its mana cost."</b> (CR 602.1). Wired as an
///   <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/> of {5}. On resolution the controller's
///   library is shuffled (<see cref="LibraryShuffle"/> — CR 701.20a), the
///   top card is moved Library → Exile, and a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) is stamped on it with a
///   ZERO mana cost — "without paying its mana cost" (CR 118.9 / 601.3e).
///   When an <see cref="IEventBus"/> is supplied the grant is revoked on
///   the first <see cref="PhaseStateType.Cleanup"/> step seen afterwards
///   ("until end of turn" — CR 514.2), mirroring
///   <see cref="LightUpTheStageFactory"/> / Containment Construct's
///   end-of-turn clear. Without a bus the stamp persists until cleared
///   manually (test posture).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for which artifact to tap</b>: the mana cost falls
///   back to the first eligible untapped artifact deterministically. Agents
///   may pre-set <see cref="TapAnotherUntappedArtifactCost.Target"/>. Same
///   posture as <see cref="SpringleafDrumFactory"/>'s creature tap.
/// - <b>"Play" includes lands</b>: the impulse grant authorises CASTING the
///   exiled card (<see cref="Card.GrantRuntimeExileCast"/>). A land exiled
///   off the top would need a parallel "play this land from exile" surface
///   — same corner-case deferral noted on
///   <see cref="LightUpTheStageFactory"/>.
/// - <b>Live TriggerManager wiring on the single-arg path</b>: the ETB
///   trigger is attached structurally for shape inspection; the runtime
///   overload registers it on the supplied <see cref="TriggerManager"/> so
///   bus-driven ETB firing works (same posture as
///   <see cref="ContainmentConstructFactory"/>).
/// </summary>
[CardName("Urza, Lord High Artificer")]
public static class UrzaLordHighArtificerFactory
{
    public const string CardName = "Urza, Lord High Artificer";
    public const string Slug = "urza-lord-high-artificer";
    public const int ImpulseManaCost = 5;

    /// <summary>
    /// Construct Urza with no live runtime services. The ETB trigger is
    /// attached structurally (not registered on a
    /// <see cref="TriggerManager"/>); the mana + impulse abilities are
    /// fully functional. Suitable for identity / shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null, effects: null);

    /// <summary>
    /// Construct Urza with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional. When supplied, the impulse {5}
    /// ability's "until end of turn" grant clears on the next
    /// <see cref="PhaseStateType.Cleanup"/> step (CR 514.2).</param>
    /// <param name="triggers">Optional. When supplied, the ETB trigger is
    /// registered so <see cref="CardMovedEvent"/> publications auto-queue
    /// it.</param>
    /// <param name="zoneService">Optional. Forwarded to the Construct token
    /// spawn so its battlefield entry publishes <see cref="CardMovedEvent"/>
    /// and downstream ETB triggers (e.g. Soul Warden) fire.</param>
    /// <param name="effects">Optional. Used to register the Construct
    /// token's "+1/+1 per artifact you control" CDA P/T rider — without it
    /// the token is a 0/0 SBA victim.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Urza enters, create a 0/0 colorless Construct artifact
        //    creature token with 'This token gets +1/+1 for each artifact
        //    you control.'"
        // Reuse the centralised Construct-token spawn (identical token to
        // Karn, Scion of Urza / Urza's Saga). The token tracks the
        // controller's artifact count dynamically via the CDA P/T rider.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create 0/0 Construct artifact-creature token (+1/+1 per artifact)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return; // CR 603.6c
                var controller = card.Controller ?? owner;
                KarnScionOfUrzaFactory.CreateConstructToken(controller, zoneService, effects);
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
        // Mana ability — CR 605.1.
        //   "Tap an untapped artifact you control: Add {U}."
        // No {T} on Urza himself — the whole activation cost is the
        // tap-another-artifact (tapsAsCost: false). Gated on an eligible
        // untapped artifact existing; the cost taps it on activation.
        // ----------------------------------------------------------------
        var tapArtifact = new TapAnotherUntappedArtifactCost(card);
        var manaAbility = new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("U"),
            canActivateCheck: () => tapArtifact.CanPay(card.Controller ?? owner),
            additionalCostPayer: p => tapArtifact.Pay(p),
            tapsAsCost: false);
        card.AddAbility(manaAbility);

        // ----------------------------------------------------------------
        // Activated impulse ability — CR 602.1.
        //   "{5}: Shuffle your library, then exile the top card. Until end
        //    of turn, you may play that card without paying its mana cost."
        // ----------------------------------------------------------------
        var impulseEffect = new Effect(
            $"{CardName}: shuffle library, exile top, may play it (free) until EOT",
            () => ResolveImpulse(card, owner, eventBus));

        var impulseAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost($"{{{ImpulseManaCost}}}") },
            effects: new IEffect[] { impulseEffect });
        card.AddAbility(impulseAbility);

        return card;
    }

    // --- Impulse resolution (CR 701.20a shuffle + CR 118.9 free-play grant) ---

    private static void ResolveImpulse(Creature card, Player owner, IEventBus? eventBus)
    {
        var controller = card.Controller ?? owner;

        // "Shuffle your library, then exile the top card." CR 701.20a —
        // shuffle FIRST, then read the (newly randomised) top card.
        LibraryShuffle.ShuffleLibrary(controller, "urza-impulse");

        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return; // empty library — nothing to exile

        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Exile.AddCard(top);
        top.SetZone(ZoneType.Exile);

        if (top is not Card stampable) return;

        // "Until end of turn, you may play that card without paying its mana
        // cost." CR 118.9 / 601.3e — grant a runtime exile-cast at ZERO cost
        // (the "without paying its mana cost" rider).
        stampable.GrantRuntimeExileCast(controller, ManaCost.Zero);

        ScheduleEndOfTurnGrantClear(stampable, controller, eventBus);
    }

    private static void ScheduleEndOfTurnGrantClear(
        Card stampable, Player controller, IEventBus? eventBus)
    {
        if (eventBus == null) return;

        // "Until end of turn" — CR 514.2. The ability is any-speed, so the
        // exile can happen on any player's turn; clear on the next Cleanup
        // regardless of who is active. Only revoke the live grant — a
        // re-stamp by a later effect overwrites and must not be clobbered.
        Action<StepStartedEvent>? handler = null;
        handler = (e) =>
        {
            if (e.StepType != PhaseStateType.Cleanup) return;
            if (ReferenceEquals(stampable.RuntimeExileCastAllowedCaster, controller))
            {
                stampable.ClearRuntimeExileCast();
            }
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}

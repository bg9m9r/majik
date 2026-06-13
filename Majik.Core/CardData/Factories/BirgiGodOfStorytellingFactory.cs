using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Birgi, God of Storytelling // Harnfel, Horn of Bounty (Kaldheim).
///
/// Front (Birgi, God of Storytelling) — Legendary Creature — God, {2}{R}, 3/3:
///   "Whenever you cast a spell, add {R}. (This mana doesn't empty from your
///    mana pool as steps and phases end.)"
///
/// Back (Harnfel, Horn of Bounty) — Legendary Artifact, {4}{R}:
///   "Discard a card: Exile the top two cards of your library. You may play
///    those cards this turn."
///
/// ## MDFC infra (CR 712.3 / 712.4) — modal PERMANENT back (deferral #19/#3)
///
/// A Kaldheim God MDFC: the controller CHOOSES which face to cast (CR 712.3).
/// The back is a NONLAND PERMANENT (an artifact), so it is cast as a spell and
/// resolves onto the battlefield AS Harnfel (the
/// <see cref="Majik.Core.Services.StackResolver"/> routes a permanent card to
/// the battlefield by type — CR 608.3). No transform happens (CR 712.4); only
/// the chosen face exists. The front-face card carries an
/// <see cref="MdfcState"/> with a castable <see cref="MdfcFace.Permanent"/>
/// back-face descriptor that <see cref="MdfcCastFlow"/> reads to offer the
/// face choice; <see cref="TurnDriver.DispatchCast"/> wires
/// <c>ActiveEffects</c> onto the permanent back so its body computes once it
/// enters.
///
/// ## Implemented (v1)
/// - Birgi front — Legendary Creature — God 3/3 at {2}{R}, with the
///   "whenever you cast a spell, add {R}" cast-trigger.
/// - Harnfel back — Legendary Artifact at {4}{R}, cast-either-face castable
///   back descriptor + the "Discard a card: Exile the top two cards of your
///   library. You may play those cards this turn." activated ability (CR
///   602.1). The temporary play permission and its end-of-turn EXPIRY are now
///   wired through the reusable <see cref="ExilePlayPermission"/> primitive
///   (CR 118.9 / 514.2): each exiled card receives a runtime exile-cast grant
///   consumed by <see cref="ExileCastAlternativeCost"/> +
///   <see cref="Majik.Core.Game.SpellCastFlow"/>, and a single shared
///   subscription revokes BOTH grants at the controller's next Cleanup step,
///   so the authorization does not linger past "this turn". This closes the
///   "temporary-play-this-card-permission-expiry" v1 deferral.
///
/// ## Boast-twice static (CR 702.135c)
/// - Birgi's "Creatures you control can boast twice during each of your turns
///   rather than once" is modelled by stamping the
///   <see cref="Majik.Core.Keywords.BoastAbility.BoastTwiceMarker"/> keyword on
///   the front face. Every Boast ability built through
///   <see cref="Majik.Core.Keywords.BoastAbility.Build"/> with
///   <see cref="Majik.Core.Keywords.BoastAbility.ControllerCapResolver"/> reads
///   the controller's battlefield for this marker and raises its per-turn cap
///   from 1 to 2 while a Birgi is controlled.
///
/// ## Deferred (v1 gaps, documented for v1-deferrals #19)
/// - Birgi's "this mana doesn't empty" rider (mana-no-empty) is not modelled —
///   the trigger adds {R} to the pool only.
///
/// ## Play-as-a-LAND corner (CR 305.2 / 601.1) — implemented
/// - "You may play those cards this turn" covers BOTH the spell-cast half and
///   the land-play half. A LAND in the exile pile is PLAYED, not cast (CR
///   601.1), so <see cref="ExilePlayPermission.GrantUntil"/> additionally
///   stamps a runtime exile land-play grant
///   (<see cref="Card.RuntimeExileLandPlayAllowedPlayer"/>) on each exiled
///   land. <see cref="ExilePlayPermission.PlayableLandsFromExile"/> surfaces it
///   to the land-drop enumeration so the controller may play it from exile
///   (still consuming the CR 305.2 land drop via
///   <see cref="Majik.Core.Game.LandDropTracker"/>); the SAME shared revocation
///   clears the land-play half at the controller's next Cleanup step.
/// </summary>
[CardName("Birgi, God of Storytelling")]
public static class BirgiGodOfStorytellingFactory
{
    public const string FrontName = "Birgi, God of Storytelling";
    public const string BackName = "Harnfel, Horn of Bounty";
    public const string FrontCost = "{2}{R}";
    public const string BackCost = "{4}{R}";

    /// <summary>
    /// Construct Birgi's front face (Legendary Creature — God 3/3) carrying the
    /// castable PERMANENT back-face descriptor for Harnfel (CR 712.3). No live
    /// trigger-manager wiring on the single-arg overload (shape / dispatcher
    /// path).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null, triggers: null);

    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var birgi = new Creature(
            name: FrontName,
            manaCost: FrontCost,
            power: 3,
            toughness: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.God });

        birgi.SetOwner(owner);
        birgi.SetController(owner);

        // CR 603.1 — "Whenever you cast a spell, add {R}." Fires on a
        // SpellCastEvent whose spell's controller is Birgi's controller.
        var castTrigger = new TriggeredAbility(
            source: birgi,
            controller: owner,
            condition: new EventTriggerCondition<Majik.Core.Domain.DomainEvents.SpellCastEvent>(
                (e, _) => ReferenceEquals(e.Spell.Controller, birgi.Controller ?? owner)),
            effects: new IEffect[]
            {
                new Effect($"{FrontName}: whenever you cast a spell, add {{R}}",
                    () =>
                    {
                        var c = birgi.Controller ?? owner;
                        c.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("{R}"));
                    }),
            },
            activeZones: new[] { ZoneType.Battlefield });
        birgi.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        // CR 702.135c — "Creatures you control can boast twice during each of
        // your turns rather than once." Stamp the Boast-twice marker keyword;
        // BoastAbility.ControllerCapResolver scans the controller's battlefield
        // for this marker and raises the per-turn Boast cap from 1 to 2 while a
        // Birgi is in play.
        birgi.AddAbility(new KeywordAbility(
            Majik.Core.Keywords.BoastAbility.BoastTwiceMarker, birgi, owner));

        // CR 712.3 — attach the MDFC face tracker WITH a castable PERMANENT
        // back-face descriptor (Harnfel — Legendary Artifact). MdfcCastFlow
        // offers the face choice; choosing the back casts Harnfel as a spell
        // that resolves onto the battlefield AS the artifact.
        var backFace = MdfcFace.Permanent(
            BackName,
            BackCost,
            buildCard: landOwner => BuildHarnfel(landOwner),
            buildDefinition: (caster, _, stack, zones) =>
                // Harnfel has no targeted ETB — a Vanilla permanent definition
                // suffices; StackResolver routes the artifact card to the
                // battlefield by type.
                SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()));
        birgi.MdfcState = new MdfcState(FrontName, BackName, backFace);

        return birgi;
    }

    /// <summary>How many cards Harnfel's activated ability exiles. (CR 602.1)</summary>
    public const int CardsExiled = 2;

    /// <summary>
    /// Materialize Harnfel, Horn of Bounty — Legendary Artifact at {4}{R} with
    /// the "Discard a card: Exile the top two cards of your library. You may
    /// play those cards this turn." activated ability (CR 602.1). Owner /
    /// controller wired. The single-arg overload uses no live event bus, so the
    /// play permission persists until cleared by hand (test path); use
    /// <see cref="BuildHarnfel(Player, IEventBus?)"/> to schedule the
    /// "this turn" expiry on a live bus.
    /// </summary>
    public static Artifact BuildHarnfel(Player owner) => BuildHarnfel(owner, eventBus: null);

    /// <summary>
    /// Materialize Harnfel with its activated ability, scheduling the temporary
    /// play permission's end-of-turn expiry on <paramref name="eventBus"/> when
    /// non-null. The exile move + grant + expiry route through the reusable
    /// <see cref="ExilePlayPermission"/> primitive.
    /// </summary>
    public static Artifact BuildHarnfel(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var harnfel = new Artifact(
            name: BackName,
            manaCost: BackCost,
            supertypes: new[] { CardSupertype.Legendary });
        harnfel.SetOwner(owner);
        harnfel.SetController(owner);

        // CR 711-companion back face tracker — this is the chosen face, so it
        // does not itself offer a further cast-either-face choice.
        harnfel.MdfcState = new MdfcState(FrontName, BackName);
        harnfel.MdfcState.Transform(); // mark back-face up (it IS Harnfel).

        harnfel.AddAbility(BuildExileTopTwoAbility(harnfel, owner, eventBus));

        return harnfel;
    }

    /// <summary>
    /// Build Harnfel's "Discard a card: Exile the top two cards of your
    /// library. You may play those cards this turn." activated ability
    /// (CR 602.1 / CR 118.9 / CR 514.2).
    ///
    /// <para>
    /// Cost = <see cref="DiscardACardCost"/> (CR 117.1). On resolution: exile
    /// the top two cards of the controller's library (CR 701.20), then stamp a
    /// runtime exile-cast grant on each via <see cref="ExilePlayPermission"/>
    /// for the controller's printed-mana-cost cast, and schedule a SINGLE
    /// shared revocation at the controller's next Cleanup step ("this turn",
    /// <see cref="ExilePlayExpiry.EndOfTurn"/>). The exiled cards become legal
    /// cast sources from exile (consumed by
    /// <see cref="ExileCastAlternativeCost"/> +
    /// <see cref="Majik.Core.Game.SpellCastFlow"/>) until that window closes.
    /// </para>
    ///
    /// <para>
    /// When <paramref name="eventBus"/> is null the bus is resolved at
    /// resolution time from <see cref="EventBusRegistry"/> (the controller's
    /// per-game bus) so a deck-loaded Harnfel on a live battlefield still
    /// expires its grants; with neither, the grants linger until cleared by
    /// hand (test path).
    /// </para>
    /// </summary>
    public static ActivatedAbility BuildExileTopTwoAbility(
        Artifact harnfel, Player owner, IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(harnfel);
        ArgumentNullException.ThrowIfNull(owner);

        return new ActivatedAbility(
            source: harnfel,
            controller: owner,
            costs: new ICost[] { new DiscardACardCost() },
            effects: new IEffect[]
            {
                new Effect(
                    $"{BackName}: exile top two, you may play those cards this turn",
                    () =>
                    {
                        var controller = harnfel.Controller ?? owner;
                        var stamped = new List<Card>(CardsExiled);
                        for (var i = 0; i < CardsExiled; i++)
                        {
                            var top = controller.Zones.Library.GetCards().FirstOrDefault();
                            if (top == null) break; // library underflow — no SBA flag for exile
                            if (top is not Card concrete) break;

                            controller.Zones.Library.RemoveCard(concrete);
                            controller.Zones.Exile.AddCard(concrete);
                            concrete.SetZone(ZoneType.Exile);

                            // CR 118.9 — "you may play those cards" with no
                            // alternate-cost rider → the printed mana cost.
                            // Grant WITHOUT scheduling per-card (bus passed null)
                            // so we can revoke all under ONE shared subscription.
                            ExilePlayPermission.GrantUntil(
                                concrete, controller, concrete.ManaCostValue,
                                ExilePlayExpiry.EndOfTurn, eventBus: null);
                            stamped.Add(concrete);
                        }

                        if (stamped.Count == 0) return;

                        // CR 514.2 — schedule the "this turn" expiry once for
                        // the whole batch, on the controller's per-game bus when
                        // no bus was supplied at build time.
                        var bus = eventBus ?? EventBusRegistry.Get(controller);
                        ExilePlayPermission.ScheduleRevocation(
                            controller, ExilePlayExpiry.EndOfTurn, bus,
                            () =>
                            {
                                // CR 305.2 — clear BOTH halves of the play
                                // permission (the spell-cast grant AND the
                                // exiled-land land-play grant) under the one
                                // shared "this turn" subscription.
                                foreach (var s in stamped)
                                {
                                    s.ClearRuntimeExileCast();
                                    s.ClearRuntimeExileLandPlay();
                                }
                            });
                    }),
            });
    }
}

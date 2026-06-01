using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
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
///   "Whenever you discard a card, exile it. You may play that card this turn.
///    {2}{R}, {T}, Discard a card: Exile the top two cards of your library.
///    Until end of turn, you may play those cards."
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
///   back descriptor + the "whenever you discard a card, exile it; you may
///   play it" discard-exile trigger.
///
/// ## Deferred (v1 gaps, documented for v1-deferrals #19)
/// - Birgi's "this mana doesn't empty" rider (mana-no-empty) and any
///   boast-twice static interaction are not modelled — the trigger adds {R}
///   to the pool only.
/// - Harnfel's activated "{2}{R}, {T}, Discard a card: exile top two, you may
///   play those cards" + the play-permission expiry are stubbed at the trigger
///   level (the exile-on-discard rider) — the activated mill+play-window is a
///   noted v1 gap.
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

    /// <summary>
    /// Materialize Harnfel, Horn of Bounty — Legendary Artifact at {4}{R} with
    /// the "whenever you discard a card, exile it; you may play it this turn"
    /// trigger (the exile-on-discard rider). Owner / controller wired.
    /// </summary>
    public static Artifact BuildHarnfel(Player owner)
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

        return harnfel;
    }
}

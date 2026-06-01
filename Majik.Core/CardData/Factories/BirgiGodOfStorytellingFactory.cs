using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Birgi, God of Storytelling // Harnfel, Horn of Bounty (Kaldheim).
///
/// This is a Modal Double-Faced Card whose BACK face is a nonland PERMANENT
/// (a Legendary Artifact) — the Kaldheim "God" MDFC shape. It exercises the
/// real cast-either-face path (deferral #3) extended to permanent backs
/// (deferral #3 residual / #2): at cast time the controller CHOOSES which
/// face to cast (CR 712.3); choosing the back casts Harnfel as an Artifact
/// spell (its own {4}{R} cost / type / effect) and, on resolution, the
/// Artifact enters the battlefield AS that face (CR 608.3). No transform
/// happens (CR 712.4) — only the chosen face exists.
///
/// Front — Birgi, God of Storytelling ({2}{R}):
///   Legendary Creature — God 3/3.
///   "Whenever you cast a spell, add {R}. Until end of turn, you don't lose
///    this mana as steps and phases end."
///   "Creatures you control can boast twice during each of your turns rather
///    than once."
///
/// Back — Harnfel, Horn of Bounty ({4}{R}):
///   Legendary Artifact.
///   "Discard a card: Exile the top two cards of your library. You may play
///    those cards this turn."
///
/// ## Implemented (v1)
/// - Front BODY: 3/3 Legendary Creature — God at {2}{R} (red), owner /
///   controller wired, with the cast-trigger mana ability (see below).
/// - <b>Birgi cast-trigger mana</b> (CR 603.1): a <see cref="TriggeredAbility"/>
///   over <see cref="SpellCastEvent"/> filtered to the controller — "Whenever
///   you cast a spell, add {R}." Adds one red to the controller's pool. (The
///   "doesn't empty as steps/phases end" rider is modelled as plain mana for
///   v1 — see deferrals.)
/// - <b>MDFC permanent back</b> (CR 712.3 / 608.3): the front-face card carries
///   an <see cref="MdfcState"/> with a castable <see cref="MdfcFace.Permanent"/>
///   descriptor for Harnfel (Legendary Artifact, {4}{R}). The cast flow offers
///   a face choice; choosing the back materializes a fresh Harnfel Artifact
///   instance (wired to the live <see cref="ReplacementBus"/>) and casts it,
///   landing on the battlefield as the Artifact.
/// - <b>Harnfel activated ability</b> (CR 602.1): "Discard a card: Exile the
///   top two cards of your library. You may play those cards this turn." A
///   <see cref="DiscardACardCost"/>-gated <see cref="ActivatedAbility"/> that
///   exiles the top two library cards and grants the controller a
///   cast-from-exile permission for each (CR 118.7 — "may play").
///
/// ## Deferred (v1 gaps)
/// - <b>Birgi's boast-twice static</b> (CR 702.111) — the per-turn boast
///   count modifier is not modelled (boast itself is a separate keyword
///   surface). The cast-trigger mana is the headline ability and is wired.
/// - <b>"Doesn't empty as steps/phases end"</b> on Birgi's mana — modelled as
///   ordinary mana for v1; the no-empty rider is the shared mana-persistence
///   gap.
/// - <b>"You may play those cards this turn"</b> (Harnfel) — the exile + the
///   per-card cast-from-exile grant are wired; the "until end of turn" expiry
///   of the permission is the shared duration-tracking gap (the grant persists
///   for the test/turn).
/// </summary>
[CardName("Birgi, God of Storytelling")]
public static class BirgiGodOfStorytellingFactory
{
    public const string FrontName = "Birgi, God of Storytelling";
    public const string BackName = "Harnfel, Horn of Bounty";
    public const string FrontCost = "{2}{R}";
    public const string BackCost = "{4}{R}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Birgi (front face) with no live TriggerManager wiring (shape
    /// / dispatcher path). The cast-trigger ability is attached but not
    /// registered; the MDFC permanent-back descriptor is attached.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Birgi (front face) with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the cast-trigger
    /// mana ability is registered so casting a spell lands it automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: FrontName,
            manaCost: FrontCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.God });
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 712.3 / 608.3 — MDFC permanent back. Choosing the back at cast
        // time casts Harnfel as a Legendary Artifact spell that enters the
        // battlefield as the Artifact. The Permanent face descriptor wires the
        // live card builder + a vanilla resolve-time SpellDefinition (the
        // StackResolver routes the Artifact card to the battlefield by type).
        var backFace = MdfcFace.Permanent(
            BackName,
            BackCost,
            buildCard: (backOwner, replacements) =>
                HarnfelHornOfBountyFactory.Create(backOwner, replacements),
            buildDefinition: (_, _, _, _) =>
                SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()));
        card.MdfcState = new MdfcState(FrontName, BackName, backFace);

        // CR 603.1 — "Whenever you cast a spell, add {R}." Filtered to the
        // controller's own spells (CR 700.7). Self-cast (casting Birgi) does
        // not contribute — Birgi isn't on the battlefield yet when its own
        // cast event fires; the active-zone gate keeps it dormant in hand.
        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>(
                (e, _) => ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)),
            effects: new IEffect[]
            {
                new Effect(
                    $"{FrontName}: add {{R}} (whenever you cast a spell)",
                    () => (card.Controller ?? owner).AddManaToPool(
                        ManaCost.Parse("{R}"), provenanceSource: card)),
            },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}

using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Monastery Swiftspear (Khans of Tarkir + many
/// reprints, Creature — Human Monk {R}).
///
/// Oracle text:
///   "Haste.
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)"
///
/// ## Implemented (v1)
/// - 1/2 Creature — Human Monk, mana cost {R}, owner/controller wired.
/// - <b>Haste</b> (CR 702.10) wired as a <see cref="KeywordAbility"/>
///   marker on the card; <c>CombatAbilities.HasHaste</c> reads it. Same
///   shape as Goblin Chieftain / Earthshaker Khenra / Goblin Rabblemaster.
/// - <b>Prowess</b> (CR 702.108) marker + the actual mechanic via
///   <see cref="ProwessFactory.Build"/>. Mirrors <see cref="MonasteryMentorFactory"/>'s
///   prowess wire-up: when a <see cref="ContinuousEffectsService"/> is
///   supplied, the prowess <see cref="TriggeredAbility"/> is attached and
///   (optionally) registered with the trigger manager for bus firing.
///   A <see cref="KeywordAbility"/> "Prowess" marker is ALSO attached so
///   shape-only inspection (dispatcher tests, bots scanning for the
///   keyword) can detect Prowess without inspecting the trigger body —
///   distinct from Monastery Mentor where the printed reminder text is
///   the only keyword surface and prowess is exposed solely as the
///   trigger description. Swiftspear is a vanilla-printed Prowess
///   creature, so the marker matches the printed keyword line.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Haste + Prowess
///   keyword markers attached; Prowess trigger is NOT wired (no effects
///   service). Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. Prowess trigger registered when <paramref name="effects"/>
///   is supplied; <see cref="TriggerManager"/> registration when
///   <paramref name="triggers"/> is supplied.
///
/// ## Deferred (v1 gaps)
/// - None for the printed card. (No ETB, no activated, no LTB clauses
///   to defer — Swiftspear is the minimum-viable Prowess body.)
/// </summary>
[CardName("Monastery Swiftspear")]
public static class MonasterySwiftspearFactory
{
    public const string CardName = "Monastery Swiftspear";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Monastery Swiftspear with no live wiring. Haste + Prowess
    /// keyword markers are attached; the Prowess trigger is NOT wired
    /// (no <see cref="ContinuousEffectsService"/>). Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Monastery Swiftspear with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not used directly by this factory; reserved
    /// for future lifecycle subscribers (e.g. LTB unregister).</param>
    /// <param name="triggers">TriggerManager for the Prowess trigger
    /// (via <see cref="ProwessFactory"/>). May be null — the trigger is
    /// still attached to the card shape.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess pump
    /// effect (CR 613.1f, Layer 7c). May be null — Prowess trigger is
    /// not wired when null.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Monk });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste. KeywordAbility marker; CombatAbilities.HasHaste
        // reads it. Same shape as Goblin Chieftain / Earthshaker Khenra /
        // Goblin Rabblemaster's printed Haste.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.108 — Prowess. KeywordAbility marker for shape-only
        // inspection (dispatcher tests, bot keyword scans). The marker
        // is independent of the actual trigger wiring below — same
        // posture as the Haste marker which sits alongside combat-
        // validator-driven mechanics.
        card.AddAbility(new KeywordAbility("Prowess", card, owner));

        // Prowess mechanic — Whenever you cast a noncreature spell,
        // Swiftspear gets +1/+1 until end of turn. Wired via
        // ProwessFactory.Build when a ContinuousEffectsService is
        // supplied; the prowess ability is registered with the trigger
        // manager when provided. When effects == null the prowess trigger
        // is not wired (shape-only path keeps the card lean).
        //
        // card.ActiveEffects is set to the effects service so that
        // card.Power / card.Toughness reads flow through the layers
        // compute (CR 613 — Layer 7c applies PumpUntilEndOfTurnEffect /
        // ProwessPumpEffect).
        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        return card;
    }
}

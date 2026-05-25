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
/// Named-card factory for Stormchaser Mage (Oath of the Gatewatch,
/// Creature — Human Wizard {(U/R)}{(U/R)} 1/3).
///
/// Oracle text:
///   "Flying, haste
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)"
///
/// ## Implemented (v1)
/// - 1/3 Creature — Human Wizard, mana cost {U/R}{U/R} (CR 107.4e hybrid
///   pips — <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> accepts
///   each pip and decomposes into two <c>HybridPip</c>s, same shape as
///   <see cref="BorosReckonerFactory"/>'s {R/W}{R/W}{R/W}).
/// - <b>Flying</b> (CR 702.9) + <b>Haste</b> (CR 702.10) wired as
///   <see cref="KeywordAbility"/> markers; read by
///   <c>CombatAbilities.HasFlying</c> / <c>CombatAbilities.HasHaste</c>
///   (evasion enforcement + summoning-sickness override per Rule 302.6).
///   Same wire-up shape as <see cref="SlickshotShowOffFactory"/>'s
///   Flying+Haste pair and <see cref="MantisRiderFactory"/>'s Flying line.
/// - <b>Prowess</b> (CR 702.108) marker + the actual mechanic via
///   <see cref="ProwessFactory.Build"/>. Same posture as
///   <see cref="MonasterySwiftspearFactory"/>: the <see cref="KeywordAbility"/>
///   "Prowess" marker is attached for shape-only inspection (dispatcher
///   tests, bot keyword scans) while the live trigger is wired only when
///   a <see cref="ContinuousEffectsService"/> is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Flying + Haste +
///   Prowess keyword markers attached; Prowess trigger is NOT wired (no
///   effects service). Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. Prowess trigger registered when <paramref name="effects"/>
///   is supplied; <see cref="TriggerManager"/> registration when
///   <paramref name="triggers"/> is supplied.
///
/// ## Deferred (v1 gaps)
/// - None for the printed card. Stormchaser Mage is a three-keyword
///   creature with no ETB / activated / LTB clauses to defer.
/// </summary>
[CardName("Stormchaser Mage")]
public static class StormchaserMageFactory
{
    public const string CardName = "Stormchaser Mage";
    public const string PrintedManaCost = "{U/R}{U/R}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Stormchaser Mage with no live wiring. Flying + Haste +
    /// Prowess keyword markers are attached; the Prowess trigger is NOT
    /// wired (no <see cref="ContinuousEffectsService"/>). Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Stormchaser Mage with optional runtime services.
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. CR 702.10 — Haste. Both shipped as keyword
        // markers consumed by CombatValidator / CombatAbilities (same wire-up
        // shape as Slickshot Show-Off and Mantis Rider).
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.108 — Prowess. KeywordAbility marker for shape-only
        // inspection (dispatcher tests, bot keyword scans). The marker is
        // independent of the actual trigger wiring below — same posture as
        // Monastery Swiftspear where the printed reminder text matches the
        // keyword line.
        card.AddAbility(new KeywordAbility("Prowess", card, owner));

        // Prowess mechanic — Whenever you cast a noncreature spell,
        // Stormchaser Mage gets +1/+1 until end of turn. Wired via
        // ProwessFactory.Build when a ContinuousEffectsService is supplied;
        // the prowess ability is registered with the trigger manager when
        // provided. When effects == null the prowess trigger is not wired
        // (shape-only path keeps the card lean).
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

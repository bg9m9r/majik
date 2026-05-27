using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormchaser Mage (Oath of the Gatewatch, {U}{R}).
///
/// Creature — Human Wizard 1/2. Oracle text:
///   "Flying
///    Haste
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)"
///
/// ## Implemented (v1)
/// - 1/2 Human Wizard, mana cost {U}{R}.
/// - <b>Flying (CR 702.9)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so combat code reads it (same shape as
///   <see cref="SpriteDragonFactory"/>).
/// - <b>Haste (CR 702.10)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so summoning-sickness check skips Stormchaser the turn it
///   resolves.
/// - <b>Prowess (CR 702.108)</b> — wired via
///   <see cref="ProwessFactory.Build"/> when a
///   <see cref="ContinuousEffectsService"/> is supplied. Mirrors
///   <see cref="SoulScarMageFactory"/> / <see cref="MonasteryMentorFactory"/>
///   — the keyword marker is surfaced as a <see cref="TriggeredAbility"/>;
///   no separate keyword marker is added since the prowess pump effect is
///   the authoritative wiring.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Prowess is NOT wired
///   (no effects service); only the Flying + Haste markers attach.
///   Suitable for dispatcher / structural tests. Mirrors
///   <see cref="SoulScarMageFactory.Create(Player)"/>'s posture.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?, TriggerManager?)"/>
///   — fully wired. Prowess trigger is registered when
///   <paramref name="effects"/> is supplied; <paramref name="triggers"/>
///   registers the prowess trigger with the live <see cref="TriggerManager"/>
///   so a <see cref="SpellCastEvent"/> queues a pending trigger.
///
/// ## Deferred (v1 gaps)
/// - None at this layer — Flying / Haste are marker-only (combat code
///   reads them), Prowess inherits the existing
///   <see cref="ProwessFactory"/> wiring used by every prowess creature.
/// </summary>
[CardName("Stormchaser Mage")]
public static class StormchaserMageFactory
{
    public const string CardName = "Stormchaser Mage";
    public const string PrintedManaCost = "{U}{R}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Stormchaser Mage with no effects-service / bus / trigger-
    /// manager wiring. Flying + Haste markers attach; Prowess is NOT wired
    /// (no effects service). Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Stormchaser Mage with optional effects / bus / trigger
    /// wiring. When <paramref name="effects"/> is supplied the Prowess
    /// trigger is built via <see cref="ProwessFactory.Build"/>; when
    /// <paramref name="triggers"/> is supplied that trigger is registered
    /// with the <see cref="TriggerManager"/> so a live
    /// <see cref="SpellCastEvent"/> queues a pending trigger.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers)
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

        // CR 702.9 — Flying. KeywordAbility marker; combat code reads it.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.10 — Haste. KeywordAbility marker; summoning-sickness
        // checker reads it.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.108 — Prowess. Built only when an effects service is
        // available (the pump registers a layer-7c +1/+1 ContinuousEffect).
        // No effects service → marker-only path, same as Soul-Scar Mage's
        // shape-only Create(Player) overload.
        if (effects != null)
        {
            var prowess = ProwessFactory.Build(card, effects);
            card.AddAbility(prowess);
            triggers?.RegisterTriggeredAbility(prowess);
        }

        // eventBus is currently unused on the Stormchaser surface — the
        // ProwessFactory subscribes via the supplied TriggerManager, which
        // owns its own bus wiring. Kept on the signature for parity with
        // other UR factories so future per-card bus hooks (e.g. surveil on
        // cast) plug in without breaking call sites. _ to silence analyzer.
        _ = eventBus;

        return card;
    }
}

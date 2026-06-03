using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prompto Argentum (Final Fantasy, {1}{R}).
///
/// Legendary Creature — Human Scout 2/2. Oracle text:
///   "Haste.
///    Selfie Shot — Whenever you cast a noncreature spell, if at least four
///    mana was spent to cast it, create a Treasure token."
///
/// ## Implemented (v1)
/// - 2/2 Legendary Creature — Human Scout, mana cost {1}{R}, owner/controller
///   wired.
/// - Haste (CR 702.10) keyword marker via <see cref="KeywordAbility"/>.
/// - <b>Selfie-Shot cast-trigger</b> (CR 603.1) over
///   <see cref="SpellCastEvent"/> filtered to (a) the controller and (b) a
///   non-Creature spell (same predicate as
///   <see cref="SlickshotShowOffFactory"/> / prowess). The "if at least four
///   mana was spent to cast it" clause is a CR 603.4 <b>intervening-if</b>
///   read off the watched spell's
///   <see cref="Majik.Core.Spells.Spell.TotalManaSpentThisCast"/> — the
///   total-amount sibling of the per-color spent-count ledger
///   (<see cref="Card.SpentAtLeast"/>). Stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> from the resolved cast cost
///   (CR 118.10), so X spells / cost reductions are all reflected: only a
///   noncreature spell whose actual mana spent reached 4 fires the token.
/// - On resolve, creates a Treasure token via
///   <see cref="TokenFactory.CreateTreasure"/>, threading the optional
///   <see cref="ZoneService"/> so the token's own ETB CardMovedEvent fires
///   (mirrors <see cref="TirelessProvisionerFactory"/>'s token routing).
///
/// Prompto's own cast does NOT self-trigger: the SpellCastEvent for Prompto
/// fires while Prompto is on the stack as a Creature spell (CR 110.4),
/// failing the noncreature predicate.
///
/// ## Deferred (v1 gaps)
/// - <b>Treasure sac-cost enforcement</b>: the created Treasure carries the
///   same v1 limitation as every other Treasure (the "{T}, Sacrifice" mana
///   ability is surfaced without a real sac additional cost) — unchanged.
/// </summary>
[CardName("Prompto Argentum")]
public static class PromptoArgentumFactory
{
    public const string CardName = "Prompto Argentum";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>CR 118.10 — the "if at least four mana was spent to cast it"
    /// threshold the Selfie-Shot intervening-if gates on.</summary>
    public const int ManaSpentThreshold = 4;

    /// <summary>
    /// Construct Prompto Argentum with no live wiring. The Selfie-Shot trigger
    /// is attached to the card for shape observability but not registered with
    /// any <see cref="TriggerManager"/>, and the resolved token bypasses
    /// ZoneService. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Prompto Argentum with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the Selfie-Shot cast-trigger is
    /// registered so a <see cref="SpellCastEvent"/> automatically queues the
    /// ability; when <paramref name="zoneService"/> is supplied the created
    /// Treasure is placed via ZoneService so its ETB CardMovedEvent fires.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste keyword marker.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 603.1 / CR 603.4 — "Whenever you cast a noncreature spell, if at
        // least four mana was spent to cast it, create a Treasure token."
        // Trigger condition matches the controller + non-Creature spell; the
        // intervening-if on TotalManaSpentThisCast is folded into the same
        // EventTriggerCondition predicate (so the ability never even queues
        // when < 4 mana was spent — CR 603.4 evaluates the intervening-if
        // when the trigger would fire). Prompto's own cast fails the
        // noncreature predicate (CR 110.4).
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, card.Controller ?? owner))
                return false;
            if (e.Spell.Card.HasType(CardType.Creature)) return false;
            // CR 118.10 — read the total mana spent off the watched spell.
            return e.Spell is Majik.Core.Spells.Spell s
                && s.TotalManaSpentThisCast >= ManaSpentThreshold;
        });

        var treasureEffect = new Effect(
            $"{CardName}: create a Treasure token (≥{ManaSpentThreshold} mana spent on a noncreature spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateTreasure(controller, zoneService);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { treasureEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}

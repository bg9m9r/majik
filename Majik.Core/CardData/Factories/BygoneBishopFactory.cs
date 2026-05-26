using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bygone Bishop (Shadows over Innistrad,
/// {2}{W}).
///
/// Creature — Spirit Cleric 2/3. Oracle text:
///   "Flying
///    Whenever you cast a creature spell with mana value 3 or less,
///    investigate. (Create a Clue token. It's an artifact with '{2},
///    Sacrifice this token: Draw a card.')"
///
/// Bygone Bishop is the original Investigate-on-cast value engine —
/// each small creature cast banks a Clue for later card draw. Pairs
/// with Tireless Tracker's land-fall Clue generator and Hard Evidence
/// 's standalone Clue-from-instant pattern (same token primitive,
/// different triggers).
///
/// ## Implemented (v1)
/// - 2/3 Creature — Spirit Cleric at {2}{W}, owner / controller wired.
/// - <b>Flying</b> keyword marker (CR 702.9) via
///   <see cref="KeywordAbility"/>.
/// - <b>Cast-trigger</b> (CR 603.1 / CR 603.6a / CR 701.39 —
///   Investigate): fires on <see cref="SpellCastEvent"/> where (a) the
///   spell's controller is Bygone Bishop's controller, (b) the spell's
///   card has <see cref="CardType.Creature"/>, and (c) the spell's
///   card has a printed mana value ≤ 3 (CR 202.3 — generic + colored +
///   hybrid + phyrexian via <see cref="ValueObjects.ManaCost.TotalValue"/>).
///   On resolve the trigger creates a Clue token (CR 111.10) via the
///   shared <see cref="TokenFactory.CreateClue"/> helper — same Clue
///   primitive used by Tireless Tracker / Hard Evidence, with the
///   built-in "{2}, Sacrifice this token: Draw a card." activated
///   ability attached by <see cref="TokenFactory.CreateClue"/>.
///
/// ## Notes
/// - <b>Self-trigger</b>: Bygone Bishop is itself a creature spell with
///   mana value 3 — casting Bishop does NOT trigger its own ability,
///   because the trigger is only active while Bishop is on the
///   battlefield (CR 603.6a — characteristic ETB-style cast trigger;
///   <c>activeZones = {Battlefield}</c>) and Bishop is on the stack
///   when its own cast event fires. This matches Talrand, Sky Summoner
///   / Young Pyromancer / Monastery Mentor's "self does not trigger"
///   posture (CR 603.6a — the source must be on the battlefield at
///   trigger evaluation time).
/// - <b>"Mana value 3 or less" + X spells</b>: CR 202.3b — X is 0 on
///   the stack for cards in zones other than the stack, but on the
///   stack X is the chosen value. v1 reads
///   <see cref="ValueObjects.ManaCost.TotalValue"/> which sums the
///   printed pips ignoring X. For an X creature spell cast with X = 2
///   at total {X}{X}, the printed mana value is 0 here — the trigger
///   fires. Same lossiness as Tireless Tracker's mv-based gates and
///   Skyclave Apparition's mv-4 cap. The engine-wide "stack mana
///   value with X resolved" surface is tracked as a separate gap.
///
/// ## Deferred (v1 gaps)
/// - <b>Clue-token "Investigate (CR 701.39)" intent</b>: TokenFactory
///   already stamps the Clue with its sac-draw activated ability;
///   reusing it here matches Tireless Tracker's posture exactly.
///   No deferral on the Clue side.
/// - <b>LTB unregister</b>: when Bygone Bishop leaves the battlefield
///   the trigger's <c>activeZones</c> guard short-circuits future
///   cast events (CR 603.6d), so no explicit unregister is required.
/// </summary>
[CardName("Bygone Bishop")]
public static class BygoneBishopFactory
{
    public const string CardName = "Bygone Bishop";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 3;
    public const int MaxTriggeringManaValue = 3;

    /// <summary>
    /// Construct Bygone Bishop with no live runtime services. The
    /// Investigate cast-trigger is attached for shape inspection; the
    /// Clue tokens it would create bypass <see cref="ZoneService"/>
    /// when invoked manually. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Bygone Bishop. When
    /// <paramref name="zoneService"/> is supplied the Clue tokens are
    /// placed onto the battlefield via the ZoneService so each token's
    /// <see cref="CardMovedEvent"/> fires (downstream ETB listeners
    /// observe the Clue's arrival). When <paramref name="triggers"/>
    /// is supplied the cast trigger is registered with the bus so a
    /// matching <see cref="SpellCastEvent"/> automatically queues the
    /// Investigate effect.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1 / CR 603.6a / CR 701.39 (Investigate).
        //   "Whenever you cast a creature spell with mana value 3 or
        //    less, investigate."
        // Predicate: spell controller is Bishop's controller, spell
        // card has the Creature type, mana value ≤ 3.
        // ----------------------------------------------------------------
        var investigateCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 109.5 — "you cast" reads the cast spell's controller
            // against Bygone Bishop's controller.
            if (!ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)) return false;

            var spellCard = e.Spell.Card;

            // Filter to creature spells only. CR 302.1 — Creature card
            // type; HasType works against the spell card's type set
            // including any additive types (Enchantment Creatures,
            // Artifact Creatures still qualify).
            if (!spellCard.HasType(CardType.Creature)) return false;

            // CR 202.3 — printed mana value. ManaCostValue lives on
            // Card (not ICard); the cast guards against rare future
            // ICard-only spell shapes.
            var mv = spellCard is Card cc ? cc.ManaCostValue.TotalValue : 0;
            return mv <= MaxTriggeringManaValue;
        });

        var investigateEffect = new Effect(
            $"{CardName}: investigate (create a Clue token, CR 701.39)",
            () => TokenFactory.CreateClue(card.Controller ?? owner, zoneService));

        var investigateTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: investigateCondition,
            effects: new IEffect[] { investigateEffect },
            // CR 603.6a — cast trigger only active while Bishop is on
            // the battlefield (this also explains why casting Bishop
            // itself does NOT trigger: Bishop is on the stack at the
            // moment its own SpellCastEvent fires).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(investigateTrigger);
        triggers?.RegisterTriggeredAbility(investigateTrigger);

        return card;
    }
}

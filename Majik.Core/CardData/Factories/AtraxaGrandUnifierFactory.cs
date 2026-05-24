using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Atraxa, Grand Unifier (Phyrexia: All Will Be One,
/// {3}{W}{U}{B}{R}{G}).
///
/// Legendary Creature — Phyrexian Angel 7/7. Oracle text:
///   "Flying, vigilance, deathtouch, lifelink.
///    When Atraxa, Grand Unifier enters, reveal the top ten cards of your
///    library. Put one card of each card type from among them into your
///    hand and the rest on the bottom of your library in a random order."
///
/// ## Implemented (v1)
/// - 7/7 Legendary Creature — Phyrexian Angel at {3}{W}{U}{B}{R}{G}.
/// - Flying + Vigilance + Deathtouch + Lifelink keyword markers
///   (CR 702.9 / 702.20 / 702.2 / 702.15) wired as <see cref="KeywordAbility"/>
///   instances — the combat helpers in
///   <see cref="Majik.Core.Combat.CombatAbilities"/> read these directly.
/// - ETB triggered ability (CR 603.6a) fires when Atraxa moves to the
///   battlefield. Resolution:
///     1. Peek up to top 10 cards of the controller's library.
///     2. For each <see cref="CardType"/> value (Artifact, Creature,
///        Enchantment, Instant, Land, Planeswalker, Sorcery, Tribal),
///        pick the first peeked card with that type and move it to hand.
///        A single card with multiple types (e.g. Artifact Creature) is
///        only ever taken once — once it leaves the peeked pool it can't
///        be assigned to a second type slot. This matches the printed
///        "one card of each card type from among them" clause.
///     3. Any remaining peeked cards are returned to the bottom of the
///        library in a random order (CR 701.20a — Fisher-Yates via
///        <see cref="Random.Shared"/>).
///
/// ## "Battle" card type
/// The printed oracle text includes Battle (MoM+); the engine's
/// <see cref="CardType"/> enum predates that release and has no Battle
/// entry. The iteration walks every <see cref="CardType"/> value the
/// engine knows about — when Battle is added, Atraxa will pick a Battle
/// from the peeked pool with no factory changes. Tribal is included in
/// the iteration even though no modern card has it (CR 308 was removed
/// in 2024) — it is harmless because no peeked card will match.
///
/// ## Deferred (v1 gaps)
/// - Reveal-event publication. The peeked cards are picked over directly;
///   no <c>CardsRevealedEvent</c> fires. No live observer cares yet (same
///   gap as the rest of the reveal-and-pick factories — Ancient Stirrings,
///   Goblin Matron, Mystical Tutor).
/// - Bottom order is randomised via <see cref="Random.Shared"/>; once the
///   engine exposes a deterministic RNG seam for replay this should
///   consume it instead (same hook as Ancient Stirrings).
/// - The ETB trigger is attached for shape; the single-arg dispatcher
///   path here produces the correct card shape without TriggerManager
///   registration. Tests drive the effect by invoking it directly.
/// </summary>
[CardName("Atraxa, Grand Unifier")]
public static class AtraxaGrandUnifierFactory
{
    public const string CardName = "Atraxa, Grand Unifier";
    public const string Cost = "{3}{W}{U}{B}{R}{G}";

    /// <summary>
    /// Construct Atraxa, Grand Unifier owned and controlled by
    /// <paramref name="owner"/>. The ETB trigger is attached to the card
    /// for shape inspection but not registered with a TriggerManager —
    /// tests / callers invoke the trigger's effects directly to drive
    /// the reveal-and-pick (mirrors WurmcoilEngineFactory.Create(owner)).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: 7,
            toughness: 7,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Angel });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Evergreen keywords (CR 702.9 Flying, CR 702.20 Vigilance,
        // CR 702.2 Deathtouch, CR 702.15 Lifelink).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // ----------------------------------------------------------------
        // ETB trigger (CR 603.6a):
        //   "When Atraxa, Grand Unifier enters, reveal the top ten cards
        //    of your library. Put one card of each card type from among
        //    them into your hand and the rest on the bottom of your
        //    library in a random order."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: reveal top 10, take one of each card type to hand, " +
            "rest to bottom of library in random order",
            () => ResolveEtb(owner));

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);

        return card;
    }

    /// <summary>
    /// Execute Atraxa's ETB resolution against <paramref name="controller"/>'s
    /// library + hand. Public so tests and bots can invoke the effect
    /// directly without going through TriggerManager. Walks up to 10
    /// cards from the top of the library, picks the first card of each
    /// known <see cref="CardType"/>, moves picks to hand, and re-bottoms
    /// the remainder in a shuffled order.
    /// </summary>
    public static void ResolveEtb(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;
        var peeked = library.GetCards().Take(10).ToList();
        if (peeked.Count == 0) return;

        var picks = SelectOnePerCardType(peeked);

        // Move picks to hand (preserving the peeked order so the test
        // output is deterministic when no two types share a slot).
        foreach (var c in peeked)
        {
            if (!picks.Contains(c)) continue;
            library.RemoveCard(c);
            controller.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }

        // Remainder → bottom in a random order (CR 701.20a).
        var remainder = peeked.Where(c => !picks.Contains(c)).ToList();
        foreach (var c in remainder) library.RemoveCard(c);
        Shuffle(remainder);
        foreach (var c in remainder)
        {
            library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    /// <summary>
    /// Pure helper: from a peeked card pool, pick at most one card per
    /// <see cref="CardType"/>. A card with multiple types claims the
    /// first type slot it matches (in enum-declaration order) and is
    /// then unavailable for further slots — so an Artifact Creature is
    /// taken once, not twice. Exposed for tests.
    /// </summary>
    public static IReadOnlySet<ICard> SelectOnePerCardType(IReadOnlyList<ICard> peeked)
    {
        ArgumentNullException.ThrowIfNull(peeked);

        var picks = new HashSet<ICard>(ReferenceEqualityComparer.Instance);
        foreach (var type in Enum.GetValues<CardType>())
        {
            foreach (var card in peeked)
            {
                if (picks.Contains(card)) continue;
                if (!card.HasType(type)) continue;
                picks.Add(card);
                break;
            }
        }
        return picks;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        // Fisher-Yates via Random.Shared. Tests that need determinism can
        // drive the resolver directly without invoking the bottom step,
        // or stub through SelectOnePerCardType to keep the bottom pile
        // empty.
        var rng = System.Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Troll of Khazad-dûm (The Lord of the Rings:
/// Tales of Middle-earth, {5}{B}).
///
/// Creature — Troll 6/5. Oracle text (Scryfall):
///   "This creature can't be blocked except by three or more creatures.
///    Swampcycling {1} ({1}, Discard this card: Search your library for
///    a Swamp card, reveal it, put it into your hand, then shuffle.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Troll {5}{B} 6/5</b>. A large black creature whose
///   sheer size requires a gang of three blockers to stop, making it a
///   natural reanimation target in GrixisReanimator.
/// - <b>"Can't be blocked except by three or more creatures" (CR 509.1b)</b>:
///   attached as a <see cref="KeywordAbility"/>
///   ("CantBeBlockedExceptByMinBlockers", arg: 3). Evaluated at
///   block-assignment time by
///   <see cref="Majik.Core.Combat.BlockLegality.MinBlockersSatisfied"/>.
///   This is the N-generalised form of the Menace (N=2) primitive, with
///   N=3. The restriction is per-attacker-count: a block declaration with
///   fewer than 3 creatures assigned to this attacker is illegal (CR
///   509.1b — every block restriction must be satisfied). Unblocked
///   declarations (zero blockers) remain legal.
/// - <b>Swampcycling {1}</b> (CR 702.32d) — routed through the shared
///   <see cref="TypedCyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{1}") and predicate
///   <c>c =&gt; c.HasSubtype(CardSubtype.Swamp)</c>. Exact analogue of
///   <see cref="TwistedAbominationFactory"/> (Swampcycling {2}) but at
///   {1} cost. Enables GrixisReanimator to fetch a Swamp on turn 2 while
///   leaving the Troll in the graveyard as a reanimation target.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Cycling ability
///   attached without an event bus (no
///   <see cref="CardCycledEvent"/> publication).
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling
///   resolve publishes <see cref="CardCycledEvent"/> so CR 702.32d
///   "Whenever a player cycles" triggers fire.
///
/// CR rule references: 205.3m (Troll subtype), 205.4a (Swamp
/// basic-land subtype), 509.1b (block restrictions), 701.19a (library
/// search), 701.20a (shuffle), 702.32 (Cycling), 702.32d (typecycling).
/// </summary>
[CardName("Troll of Khazad-dûm")]
public static class TrollOfKhazadDumFactory
{
    public const string CardName = "Troll of Khazad-dûm";
    public const string PrintedManaCost = "{5}{B}";
    public const int Power = 6;
    public const int Toughness = 5;
    public const string CyclingCost = "{1}";
    public const int MinBlockers = 3;

    /// <summary>
    /// Construct Troll of Khazad-dûm with no event bus. The
    /// swampcycling activated ability is attached to the card shape;
    /// activation is gated to the controller's hand by
    /// <see cref="DiscardSelfCost.CanPay"/>. Shape-only — no
    /// <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Troll of Khazad-dûm. When <paramref name="eventBus"/>
    /// is supplied the cycling resolve body publishes
    /// <see cref="CardCycledEvent"/> so CR 702.32d "Whenever a player
    /// cycles a card" triggers fire.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Troll });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Can't be blocked except by three or more creatures" (CR 509.1b).
        // Represented as a KeywordAbility marker with
        // keyword = "CantBeBlockedExceptByMinBlockers" and Arg = 3.
        // Evaluated at block-declaration time by
        // BlockLegality.MinBlockersSatisfied(attacker, blockerCount).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(
            "CantBeBlockedExceptByMinBlockers",
            source: card,
            controller: owner,
            arg: MinBlockers));

        // ----------------------------------------------------------------
        // Swampcycling {1} — CR 702.32d. Routed through the shared
        // TypedCyclingFactory primitive with predicate
        //   c => c.HasSubtype(CardSubtype.Swamp)
        // for the Swamp-card tutor target. The primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a), attaches both
        // the "Swampcycling" typed keyword + the generic "Cycling"
        // marker (CR 702.32d — typecycling IS Cycling), and on resolve
        // tutors a Swamp card via agent prompt with deterministic
        // first-match fallback (CR 701.19a) + shuffles (CR 701.20a) +
        // publishes CardCycledEvent (CR 702.32d).
        // ----------------------------------------------------------------
        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: c => c.HasSubtype(CardSubtype.Swamp),
            typedKeyword: "Swampcycling",
            kindLabel: "Swamp card",
            eventBus: eventBus);

        return card;
    }
}

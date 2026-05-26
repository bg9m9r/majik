using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Twisted Abomination (Torment, {5}{B}).
///
/// Creature — Zombie Mutant 5/3. Oracle text (Scryfall):
///   "Swampcycling {2} ({2}, Discard this card: Search your library for
///    a Swamp card, reveal it, put it into your hand, then shuffle.)
///    {B}: Regenerate this creature."
///
/// ## Implemented (v1)
///
/// - <b>Creature — Zombie Mutant {5}{B} 5/3</b>. The defining Torment
///   "swampcycler" body — discarded for a Swamp far more often than
///   cast, with the side benefit of being a legal Living End / Hypnox
///   reanimation target sitting in the graveyard once cycled.
/// - <b>Swampcycling {2}</b> (CR 702.32d) — routed through the shared
///   <see cref="TypedCyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{2}") and predicate
///   <c>c =&gt; c.HasSubtype(CardSubtype.Swamp)</c>. The primitive
///   attaches the <see cref="ActivatedAbility"/> + a
///   <see cref="KeywordAbility"/>("Swampcycling") typed marker + a
///   "Cycling" generic marker (CR 702.32d — typecycling IS Cycling),
///   layers <see cref="DiscardSelfCost"/> (CR 702.32a hand-zone gate)
///   on the cost stack, and on resolve tutors the first Swamp card
///   from the controller's library to hand (agent prompt with
///   deterministic first-match fallback — CR 701.19a) + shuffles
///   (CR 701.20a) + publishes <see cref="CardCycledEvent"/> for the
///   CR 702.32d "Whenever a player cycles" subscribers.
///
/// ## Deferred (v1 gap)
///
/// - <b>"{B}: Regenerate this creature."</b> (CR 701.15) — regenerate
///   is not yet a first-class engine surface. The "can't be
///   regenerated" rider exists in destroy-effect closures (Terminate,
///   Wrath of God, Boil) but no card today actually generates a
///   regeneration shield. Wire the activated ability once the
///   regenerate shield + SBA suppression hook ships (CR 701.15a — the
///   replacement effect that overrides destruction). The cycle leg
///   alone covers the Living End / swamp-tutor enabler role this card
///   was printed for.
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
/// CR rule references: 205.3m (Zombie + Mutant subtypes), 205.4a
/// (Swamp basic-land subtype), 701.19a (library search), 701.20a
/// (shuffle), 702.32 (Cycling), 702.32d (typecycling).
/// </summary>
[CardName("Twisted Abomination")]
public static class TwistedAbominationFactory
{
    public const string CardName = "Twisted Abomination";
    public const string PrintedManaCost = "{5}{B}";
    public const int Power = 5;
    public const int Toughness = 3;
    public const string CyclingCost = "{2}";

    /// <summary>
    /// Construct Twisted Abomination with no event bus. The
    /// swampcycling activated ability is attached to the card shape;
    /// activation is gated to the controller's hand by
    /// <see cref="DiscardSelfCost.CanPay"/>. Shape-only — no
    /// <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Twisted Abomination. When <paramref name="eventBus"/>
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
            subtypes: new[] { CardSubtype.Zombie, CardSubtype.Mutant });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Swampcycling {2} — CR 702.32d. Routed through the shared
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

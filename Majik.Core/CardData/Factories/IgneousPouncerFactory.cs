using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Igneous Pouncer (Hour of Devastation, {4}{B}{R}).
///
/// Creature — Elemental 5/1. Oracle text (Scryfall):
///   "Haste
///    Swampcycling {2}, mountaincycling {2} ({2}, Discard this card:
///    Search your library for a Swamp or Mountain card, reveal it, put it
///    into your hand, then shuffle.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Elemental {4}{B}{R} 5/1</b>. The Rakdos swampcycler /
///   mountaincycler beater — a hasty 5-power body that, far more often,
///   gets pitched to dig up a dual-color land. Mana value 6, two colored
///   pips (CR 105.2 — black + red).
///
/// - <b>Haste (CR 702.10)</b> — wired as a <see cref="KeywordAbility"/>
///   marker, the same shape as Bloodbraid Elf / Slickshot Show-Off /
///   Earthshaker Khenra. Consumed by the combat / summoning-sickness
///   reader so Igneous Pouncer can attack the turn it enters.
///
/// - <b>Swampcycling {2}, Mountaincycling {2}</b> (CR 702.29 / 702.32d) —
///   TWO distinct typed-cycling activated abilities, each routed through
///   the shared <see cref="TypedCyclingFactory.Build"/> primitive. Per the
///   single combined reminder text, BOTH abilities search for a "Swamp or
///   Mountain card", so both use the union predicate
///   <c>c =&gt; c.HasSubtype(CardSubtype.Swamp) || c.HasSubtype(CardSubtype.Mountain)</c>.
///   Each Build call attaches its own <see cref="ActivatedAbility"/> + a
///   typed <see cref="KeywordAbility"/> marker ("Swampcycling" /
///   "Mountaincycling") + the generic "Cycling" marker (CR 702.32d —
///   typecycling IS Cycling), layers <see cref="DiscardSelfCost"/> on the
///   cost stack (CR 702.32a hand-zone gate), and on resolve tutors the
///   first matching Swamp-or-Mountain card from the controller's library
///   to hand (agent prompt with deterministic first-match fallback — CR
///   701.19a) + shuffles (CR 701.20a) + publishes
///   <see cref="CardCycledEvent"/> (CR 702.32d) when an event bus is
///   supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Both cycling
///   activated abilities attached without an event bus (no
///   <see cref="CardCycledEvent"/> publication). Suitable for dispatcher /
///   shape / Haste-marker tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling
///   resolve publishes <see cref="CardCycledEvent"/> so CR 702.32d
///   "Whenever a player cycles" triggers fire.
///
/// CR rule references: 105.2 (color from pips), 202.3 (mana value),
/// 205.3m (Elemental subtype), 205.4a (Swamp / Mountain basic-land
/// subtypes), 701.19a (library search), 701.20a (shuffle), 702.10
/// (Haste), 702.29 (Swampcycling / Mountaincycling), 702.32 (Cycling),
/// 702.32d (typecycling).
/// </summary>
[CardName("Igneous Pouncer")]
public static class IgneousPouncerFactory
{
    public const string CardName = "Igneous Pouncer";
    public const string PrintedManaCost = "{4}{B}{R}";
    public const int Power = 5;
    public const int Toughness = 1;
    public const string SwampcyclingCost = "{2}";
    public const string MountaincyclingCost = "{2}";

    /// <summary>
    /// Construct Igneous Pouncer with no event bus. The swampcycling +
    /// mountaincycling activated abilities are attached to the card shape;
    /// activation is gated to the controller's hand by
    /// <see cref="DiscardSelfCost.CanPay"/>. Shape-only — no
    /// <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Igneous Pouncer. When <paramref name="eventBus"/> is
    /// supplied each cycling resolve body publishes
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
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Haste — CR 702.10. KeywordAbility marker consumed by the combat /
        // summoning-sickness reader. Same shape as Bloodbraid Elf /
        // Slickshot Show-Off / Earthshaker Khenra's printed Haste.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // Swampcycling {2} + Mountaincycling {2} — CR 702.29 / 702.32d.
        // The single combined reminder text — "Search your library for a
        // Swamp or Mountain card …" — means BOTH abilities tutor the same
        // union of Swamp-OR-Mountain cards. Each is its own typed-cycling
        // activated ability, routed through TypedCyclingFactory.Build with
        // the union predicate. The primitive appends DiscardSelfCost (CR
        // 702.32a hand-zone gate), attaches the typed keyword + the generic
        // "Cycling" marker (CR 702.32d), and on resolve tutors a matching
        // card via agent prompt / deterministic first-match fallback (CR
        // 701.19a) + shuffles (CR 701.20a) + publishes CardCycledEvent (CR
        // 702.32d) when an event bus is supplied.
        // ----------------------------------------------------------------
        Func<ICard, bool> swampOrMountain = c =>
            c.HasSubtype(CardSubtype.Swamp) || c.HasSubtype(CardSubtype.Mountain);

        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(SwampcyclingCost),
            predicate: swampOrMountain,
            typedKeyword: "Swampcycling",
            kindLabel: "Swamp or Mountain card",
            eventBus: eventBus);

        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(MountaincyclingCost),
            predicate: swampOrMountain,
            typedKeyword: "Mountaincycling",
            kindLabel: "Swamp or Mountain card",
            eventBus: eventBus);

        return card;
    }
}

using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Striped Riverwinder (Hour of Devastation,
/// {6}{U}).
///
/// Creature — Serpent 5/5. Oracle text (Scryfall):
///   "Hexproof (This creature can't be the target of spells or abilities
///    your opponents control.)
///    Cycling {U} ({U}, Discard this card: Draw a card.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Serpent {6}{U} 5/5</b>. New
///   <see cref="CardSubtype.Serpent"/> creature subtype (CR 205.3m).
/// - <b>Hexproof</b> (CR 702.11) wired as a <see cref="KeywordAbility"/>
///   marker — consumed by the targeting validator
///   (<c>Majik.Core.Targeting.TargetingValidator</c>) to deny opponent-
///   controlled spells / abilities from selecting Riverwinder as a
///   target. Same wiring shape as the test-fixture Hexproof bears under
///   <c>Majik.Core.Tests/Targeting/TargetLegalityTests.cs</c>.
/// - <b>Cycling {U}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{U}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers the <see cref="DiscardSelfCost"/>
///   hand-zone gate (CR 702.32a) onto the cost stack, and on resolve
///   publishes <see cref="CardCycledEvent"/> for CR 702.32d subscribers
///   (Lightning Rift, Curator of Mysteries, the Living End cascade
///   chain).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Cycling activated
///   ability attached without an event bus (no CardCycledEvent
///   publication). Suitable for dispatcher / shape / Hexproof targeting
///   tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling
///   resolve publishes <see cref="CardCycledEvent"/> so
///   "Whenever a player cycles" triggers fire.
///
/// CR rule references: 205.3m (Serpent subtype), 702.11 (Hexproof),
/// 702.32 (Cycling).
/// </summary>
[CardName("Striped Riverwinder")]
public static class StripedRiverwinderFactory
{
    public const string CardName = "Striped Riverwinder";
    public const string PrintedManaCost = "{6}{U}";
    public const int Power = 5;
    public const int Toughness = 5;
    public const string CyclingCost = "{U}";

    /// <summary>
    /// Construct Striped Riverwinder with no event bus. The cycling
    /// activated ability is attached to the card shape; activation is
    /// gated to the controller's hand by
    /// <see cref="DiscardSelfCost.CanPay"/>. Shape-only — no
    /// <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Striped Riverwinder. When <paramref name="eventBus"/>
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
            subtypes: new[] { CardSubtype.Serpent });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.11 — Hexproof. KeywordAbility marker; the targeting
        // validator denies opponent-controlled spells / abilities from
        // selecting this creature as a target.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Hexproof", card, owner));

        // ----------------------------------------------------------------
        // Cycling {U} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}

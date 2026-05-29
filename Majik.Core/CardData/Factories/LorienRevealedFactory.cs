using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lórien Revealed (The Lord of the Rings: Tales of
/// Middle-earth, {3}{U}{U}).
///
/// Sorcery. Oracle text (Scryfall):
///   "Draw three cards.
///    Islandcycling {1} ({1}, Discard this card: Search your library for an
///    Island card, reveal it, put it into your hand, then shuffle.)"
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b>, mana cost {3}{U}{U}.
/// - <b>Resolve effect (via <see cref="BuildResolveEffect"/>)</b>: draws
///   three cards from the top of the caster's library (CR 121.1). Empty
///   library mid-draw flags the SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> and short-circuits
///   the remaining draws. Same posture as
///   <see cref="CounselOfTheSoratamiFactory.BuildResolveEffect"/> (draw two).
/// - <b>Islandcycling {1}</b> (CR 702.32d — typecycling) — routed through
///   the shared <see cref="TypedCyclingFactory.Build"/> primitive with cycle
///   cost <see cref="ManaCostCost"/>("{1}") and predicate
///   <c>c =&gt; c.HasSubtype(CardSubtype.Island)</c> for the Island-card
///   tutor target (matches basic Islands and any nonbasic land with the
///   Island land type). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   ("Islandcycling") typed marker + a "Cycling" generic marker
///   (CR 702.32d — typecycling IS Cycling), layers
///   <see cref="DiscardSelfCost"/> (CR 702.32a hand-zone gate) on the cost
///   stack, and on resolve tutors the first Island card from the
///   controller's library to hand (agent prompt with deterministic
///   first-match fallback — CR 701.19a) + shuffles (CR 701.20a) + publishes
///   <see cref="CardCycledEvent"/> for the CR 702.32d "Whenever a player
///   cycles" subscribers (Lightning Rift, Astral Slide, etc.). Mirrors
///   <see cref="GenerousEntFactory"/>'s Forestcycling wiring.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Islandcycling ability
///   attached with no event bus (no <see cref="CardCycledEvent"/>
///   publication). Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Islandcycling
///   resolve publishes <see cref="CardCycledEvent"/> so CR 702.32d
///   "Whenever a player cycles" triggers fire.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect draws</b>: the "Draw three cards" resolve uses
///   direct top-of-library zone moves (same posture as
///   <see cref="CounselOfTheSoratamiFactory"/>), not a centralised
///   "Player.DrawCard" pipeline — draw-replacement effects won't see these
///   draws until a unified draw API lands (engine-wide gap, not
///   card-specific).
///
/// CR rule references: 121.1 (draw), 704.5b (empty-library loss),
/// 701.19a (search), 701.20a (shuffle), 702.32 (Cycling),
/// 702.32d (typecycling).
/// </summary>
[CardName("Lórien Revealed")]
public static class LorienRevealedFactory
{
    public const string CardName = "Lórien Revealed";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const string CyclingCost = "{1}";
    public const int DrawCount = 3;

    /// <summary>
    /// Build Lórien Revealed with no event bus. The Islandcycling activated
    /// ability is attached to the card shape; activation is gated to the
    /// controller's hand by <see cref="DiscardSelfCost.CanPay"/>. Shape-only
    /// — no <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Build Lórien Revealed. When <paramref name="eventBus"/> is supplied
    /// the Islandcycling resolve body publishes <see cref="CardCycledEvent"/>
    /// so CR 702.32d "Whenever a player cycles" triggers fire.
    /// </summary>
    public static Sorcery Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Islandcycling {1} — CR 702.32d. Routed through the shared
        // TypedCyclingFactory primitive with predicate
        //   c => c.HasSubtype(CardSubtype.Island)
        // for the Island-card tutor target. The primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a), attaches both the
        // "Islandcycling" typed keyword + the generic "Cycling" marker
        // (CR 702.32d — typecycling IS Cycling), and on resolve tutors an
        // Island card via agent prompt with deterministic first-match
        // fallback (CR 701.19a) + shuffles (CR 701.20a) + publishes
        // CardCycledEvent (CR 702.32d).
        // ----------------------------------------------------------------
        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: c => c.HasSubtype(CardSubtype.Island),
            typedKeyword: "Islandcycling",
            kindLabel: "Island card",
            eventBus: eventBus);

        return card;
    }

    /// <summary>
    /// Build Lórien Revealed's resolve effect — draw three cards from the
    /// top of the caster's library (CR 121.1). Empty library mid-draw flags
    /// the SBA loss (CR 704.5b) and short-circuits the remaining draws.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect($"{CardName}: draw three cards.", () =>
            {
                // CR 121.1 — three simple top-of-library draws. Empty
                // library mid-draw flags the SBA loss (CR 704.5b) and
                // short-circuits the remaining draws.
                for (var i = 0; i < DrawCount; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            }),
        };
    }
}

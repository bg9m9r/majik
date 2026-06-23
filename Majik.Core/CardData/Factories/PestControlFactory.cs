using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pest Control (Modern Horizons 3, {W}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy all nonland permanents with mana value 1 or less.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// ## Why a named factory (no template covers it)
/// Pest Control pairs a mana-value-filtered mass-destruction sweep —
/// the same shape as <see cref="BrotherhoodsEndFactory"/>'s mode 1 and
/// <see cref="PathOfPerilFactory"/>, only widened to <i>all nonland
/// permanents</i> (CR 109.5 / 305.1) rather than a single permanent type —
/// with the shared Cycling primitive (CR 702.32), exactly like
/// <see cref="MiscalculationFactory"/> (counter + cycling) and
/// <see cref="LorienRevealedFactory"/> (draw + typecycling). No single
/// spell template binds the destroy sweep + cycling together, so it gets a
/// named factory.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {W}{B}, white + black (multicolour). Card
///   shape comes from the embedded JSON (<c>pest-control.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Destroy all nonland permanents with mana value 1 or less</b>
///   (via <see cref="BuildResolveEffect"/>): untargeted symmetric sweep
///   (CR 109.5 — "all" reaches every battlefield regardless of controller)
///   over every supplied player's battlefield; every permanent (CR 110)
///   that is NOT a land (CR 305.1) and whose mana value (CR 202.3) is
///   1 or less is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7). Indestructible
///   (CR 702.12) / regeneration (CR 701.15) honoured by the Destroy gate.
///   Mana value 0 permanents (most tokens, MV-0 artifacts) are caught
///   because 0 ≤ 1.
/// - <b>Cycling {2}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{2}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + the Cycling keyword marker, layers
///   the <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a) onto the
///   cost stack, and on resolve publishes <see cref="CardCycledEvent"/> for
///   CR 702.32d subscribers.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Cycling activated
///   ability attached without an event bus (no CardCycledEvent
///   publication). Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling
///   resolve publishes <see cref="CardCycledEvent"/> so "Whenever a
///   player cycles" triggers fire.
///
/// CR rule references: 109.5 (symmetric "all" sweep), 110 (permanent),
/// 202.3 (mana value), 305.1 (land — excluded), 701.7 (destroy),
/// 701.15 (regeneration), 702.12 (indestructible), 702.32 (Cycling),
/// 702.32d ("Whenever a player cycles").
/// </summary>
[CardName("Pest Control")]
public static class PestControlFactory
{
    public const string CardName = "Pest Control";
    public const string Slug = "pest-control";
    public const string PrintedManaCost = "{W}{B}";
    public const string CyclingCost = "{2}";

    /// <summary>CR 202.3 — the destroy sweep hits permanents of mana value
    /// at most this (CR 701.7).</summary>
    public const int ManaValueCeiling = 1;

    /// <summary>
    /// Construct Pest Control with no event bus. The cycling activated
    /// ability is attached to the card shape; activation is gated to the
    /// controller's hand by <see cref="DiscardSelfCost.CanPay"/>. Shape-only
    /// — no <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Pest Control. The card shape (Sorcery {W}{B}, white +
    /// black) is materialized from the embedded JSON definition; the
    /// Cycling {2} activated ability is then layered on via the shared
    /// primitive. When <paramref name="eventBus"/> is supplied the cycling
    /// resolve body publishes <see cref="CardCycledEvent"/> so CR 702.32d
    /// "Whenever a player cycles a card" triggers fire.
    /// </summary>
    public static Sorcery Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(def, owner);

        // ----------------------------------------------------------------
        // Cycling {2} — CR 702.32. Routed through the shared CyclingFactory
        // primitive; the primitive appends the DiscardSelfCost hand-zone
        // gate (CR 702.32a) and the CardCycledEvent publish (CR 702.32d)
        // automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }

    /// <summary>
    /// Build Pest Control's resolve effect — destroy every nonland permanent
    /// (CR 110 / 305.1) with mana value 1 or less (CR 202.3) across every
    /// supplied player's battlefield. Untargeted symmetric mass destruction
    /// (CR 109.5 — "all", no controller restriction). Snapshot to a list
    /// before applying so same-step zone moves don't disturb the
    /// enumeration. Destroy via
    /// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
    /// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7); indestructible
    /// (CR 702.12) / regeneration (CR 701.15) honoured by the Destroy gate.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep
    /// should reach (CR 109.5 — "all"). Pass <c>Game.Players</c> for the
    /// printed symmetric sweep; an empty list makes the resolve a no-op.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy all nonland permanents with mana value {ManaValueCeiling} or less.",
                () =>
                {
                    var seen = new HashSet<Permanent>();
                    foreach (var pl in allPlayers)
                    {
                        // Snapshot — MoveToGraveyard mutates the source
                        // battlefield in place.
                        foreach (var perm in pl.Zones.Battlefield.GetCards()
                                     .OfType<Permanent>()
                                     .ToList())
                        {
                            // CR 305.1 — lands are excluded ("nonland").
                            if (perm.HasType(CardType.Land)) continue;
                            // CR 202.3 — mana value is the total mana cost.
                            if (perm.ManaCostValue.TotalValue > ManaValueCeiling) continue;
                            if (!seen.Add(perm)) continue;
                            // CR 701.7 — Destroy.
                            OracleSpellBinder.MoveToGraveyard(perm, ZoneMoveReason.Destroy);
                        }
                    }
                }),
        };
    }
}
